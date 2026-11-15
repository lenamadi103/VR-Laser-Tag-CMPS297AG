using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// HUD for the local player, laid out like a flat-screen shooter HUD:
// rank | time | score along the top with the game mode below, health
// bottom-left, ammo bottom-right, big status messages in the middle.
//
// - The canvas is fixed in the player's view (ComfortHud, "Lock To View")
//   1.3 m away, and drawn on top of the arena (HudOverlay shader), so walls
//   and cover never cut through it.
// - The panels sit towards the edges of the view (top bar high, health
//   bottom-left, ammo bottom-right), like a flat-screen shooter, so the
//   middle stays clear for aiming. Their angles and size are adjustable
//   (Placement settings), also while the game runs in the Editor.
// - There is no head-centred crosshair on purpose: in VR the shot follows
//   the gun, not the head, so a crosshair in the middle of the view would
//   point somewhere else than the gun.
//
// The layout is built in code when the game starts (BuildLayout). The panels
// created in the scene by the editor setup scripts are replaced, so their
// look no longer matters. The damage vignette stays on the separate
// head-locked overlay (damageFlash).
//
// It only reads synchronized state; it never changes gameplay. Text and bars
// are only touched when a value changes (no per-frame UI rebuilds).
public class PlayerHUD : MonoBehaviour
{
    // Set by the editor setup (LaserTagSetup.SetupHud), which still assigns
    // them. BuildLayout re-points them at the new layout; healthBar and
    // hitMarker (old layout) are no longer used.
    [Header("Stats")]
    [SerializeField] private GameObject statsGroup;
    [SerializeField] private Text healthText;
    [SerializeField] private Image healthBar;
    [SerializeField] private Text ammoText;
    [SerializeField] private Text timerText;
    [SerializeField] private Text scoreText;

    [Header("Messages")]
    [SerializeField] private Text centerText;
    [SerializeField] private Text feedText;
    [SerializeField] private Text hitMarker;
    [SerializeField] private Image damageFlash;

    [Header("Timing")]
    [SerializeField] private float feedDuration = 2f;
    [SerializeField] private float hitMarkerDuration = 0.15f;
    [SerializeField] private float goMessageDuration = 1.5f;

    [Header("Placement (degrees from the centre of the view)")]
    // Defaults stay inside comfortable eye movement (about 25 degrees to the
    // side, less upwards than downwards) and the sharp part of Quest lenses.
    [Tooltip("How far left / right the corner panels (rank, score, health, ammo) sit.")]
    [Range(8f, 35f)] [SerializeField] private float sideAngle = 22f;
    [Tooltip("How far up the top row (rank, time, score) sits.")]
    [Range(8f, 30f)] [SerializeField] private float topAngle = 17f;
    [Tooltip("How far down the bottom row (health, ammo) sits.")]
    [Range(8f, 30f)] [SerializeField] private float bottomAngle = 20f;
    [Tooltip("Size of the panels (1 = original size).")]
    [Range(0.7f, 1.6f)] [SerializeField] private float panelScale = 1.15f;

    // HUD distance (1.3 m) in canvas pixels (1 px = 1 mm with the HUD's 0.001
    // scale): turns a view angle into a position on the canvas.
    private const float DistancePixels = 1300f;

    // Nominal canvas size in canvas pixels (the panels may sit outside it).
    private static readonly Vector2 CanvasSize = new Vector2(1040f, 660f);

    private const int HealthSegments = 10;

    private static readonly Color Cyan = new Color(0.3f, 0.85f, 1f);
    private static readonly Color CyanGlow = new Color(0.3f, 0.85f, 1f, 0.25f);
    private static readonly Color Orange = new Color(1f, 0.6f, 0.18f);
    private static readonly Color PanelFill = new Color(0.02f, 0.06f, 0.1f, 0.78f);
    private static readonly Color LabelColor = new Color(0.82f, 0.9f, 1f);
    private static readonly Color SegmentOff = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color HealthGreen = new Color(0.3f, 0.95f, 0.45f);
    private static readonly Color HealthYellow = new Color(1f, 0.82f, 0.25f);
    private static readonly Color HealthRed = new Color(1f, 0.3f, 0.25f);
    private static readonly Color HitYellow = new Color(1f, 0.95f, 0.4f);
    private static readonly Color TextShadow = new Color(0f, 0f, 0f, 0.75f);

    // Built at runtime.
    private Text rankValue;
    private Image healthIcon;
    private Image hitMarkerImage;
    private RectTransform ammoBar;
    private RectTransform timePanelRect, rankPanelRect, scorePanelRect, modeTagRect, healthPanelRect, ammoPanelRect;
    private readonly List<Image> healthFill = new List<Image>();
    private readonly List<Image> ammoFill = new List<Image>();
    private Material material;
    private Font font;

    private float feedHideTime;
    private float hitMarkerHideTime;
    private float damageFlashAlpha;
    private float playingStartedTime = -100f;
    private float nextRankUpdate;

    // Last values shown, so the UI is only rebuilt when something changes.
    private int shownHealth = -1;
    private int shownAmmo = -1;
    private int shownMagazine = -1;
    private bool shownReloading;
    private int shownRank = -1;
    private int shownScore = -1;
    private int shownTimeKey = -1;
    private string shownCenter = "";

    private void Awake()
    {
        BuildLayout();
    }

    private void OnEnable()
    {
        PlayerCombat.LocalFeedback += OnLocalFeedback;
        MatchManager.StateChanged += OnMatchStateChanged;
    }

    private void OnDisable()
    {
        PlayerCombat.LocalFeedback -= OnLocalFeedback;
        MatchManager.StateChanged -= OnMatchStateChanged;
    }

    private void Start()
    {
        SetFlashAlpha(0f);
    }

    private void Update()
    {
        PlayerCombat combat = PlayerCombat.Local;
        MatchManager match = MatchManager.Instance;
        bool inGame = combat != null && combat.IsSpawned;

        // While the VR menu is open it shows the same information;
        // hide the big HUD parts so the two do not overlap.
        bool menuOpen = VRMenuUI.IsVisible;
        bool showStats = inGame && !menuOpen;

        if (statsGroup.activeSelf != showStats)
            statsGroup.SetActive(showStats);

        if (showStats)
            UpdateStats(combat, match);

        string center = showStats ? BuildCenterMessage(combat, match) : "";
        if (center != shownCenter)
        {
            shownCenter = center;
            centerText.text = center;
        }

        if (feedText.text.Length > 0 && Time.time >= feedHideTime)
            feedText.text = "";

        if (hitMarkerImage.enabled && Time.time >= hitMarkerHideTime)
            hitMarkerImage.enabled = false;

        if (damageFlashAlpha > 0f)
        {
            damageFlashAlpha = Mathf.MoveTowards(damageFlashAlpha, 0f, Time.deltaTime * 0.8f);
            SetFlashAlpha(damageFlashAlpha);
        }
    }

    // ------------------------------------------------------------------
    // Stats
    // ------------------------------------------------------------------

    private void UpdateStats(PlayerCombat combat, MatchManager match)
    {
        // Health: value, 10 segments (partly filled), colour by amount left.
        int health = combat.Health.Value;
        if (health != shownHealth)
        {
            shownHealth = health;
            float fraction = combat.MaxHealth > 0 ? Mathf.Clamp01((float)health / combat.MaxHealth) : 0f;
            Color color = fraction > 0.5f ? HealthGreen : fraction > 0.25f ? HealthYellow : HealthRed;

            healthText.text = health + "<size=22> HP</size>";
            healthIcon.color = color;
            SetSegments(healthFill, fraction, color);
        }

        // Ammo: one segment per shot in the magazine.
        int magazine = Mathf.Max(1, combat.MagazineSize);
        int ammo = combat.Ammo.Value;
        bool reloading = combat.IsReloading.Value;

        if (magazine != shownMagazine)
        {
            shownMagazine = magazine;
            BuildSegments(ammoBar, ammoFill, magazine);
            shownAmmo = -1;
        }

        if (ammo != shownAmmo || reloading != shownReloading)
        {
            shownAmmo = ammo;
            shownReloading = reloading;

            ammoText.text = reloading
                ? "<size=28>RELOADING</size>"
                : ammo + "<size=26> / " + magazine + "</size>";
            ammoText.color = ammo == 0 && !reloading ? HealthRed : Color.white;
            SetSegments(ammoFill, reloading ? 0f : (float)ammo / magazine, Orange);
        }

        // Time: only re-formatted when the shown second or state changes.
        int timeKey = TimeKey(match);
        if (timeKey != shownTimeKey)
        {
            shownTimeKey = timeKey;
            timerText.text = DescribeTimer(match);
        }

        // Rank and score: standings are sorted a few times per second only.
        if (Time.unscaledTime >= nextRankUpdate)
        {
            nextRankUpdate = Time.unscaledTime + 0.25f;
            PlayerInfo me = PlayerInfo.FindByClientId(combat.OwnerClientId);
            int rank = me != null ? MatchManager.GetStandings().IndexOf(me) + 1 : 0;
            int score = me != null ? me.Score.Value : 0;

            if (rank != shownRank)
            {
                shownRank = rank;
                rankValue.text = rank > 0 ? "#" + rank : "-";
            }

            if (score != shownScore)
            {
                shownScore = score;
                scoreText.text = score.ToString();
            }
        }
    }

    private static int TimeKey(MatchManager match)
    {
        if (match == null || !match.IsSpawned)
            return 0;

        return (int)match.State.Value * 100000 + Mathf.CeilToInt(match.RemainingSeconds);
    }

    private static string DescribeTimer(MatchManager match)
    {
        if (match == null || !match.IsSpawned)
            return "-:--";

        switch (match.State.Value)
        {
            case MatchState.Playing:
            case MatchState.Countdown:
                return MatchManager.FormatTime(match.RemainingSeconds);
            case MatchState.Finished:
                return "0:00";
            default:
                return "-:--";
        }
    }

    private string BuildCenterMessage(PlayerCombat combat, MatchManager match)
    {
        bool finished = match != null && match.IsSpawned && match.State.Value == MatchState.Finished;

        if (finished)
        {
            string headline;

            if (match.WinnerClientId.Value == combat.OwnerClientId)
                headline = "<color=#FFD54A>YOU WIN!</color>";
            else if (match.WinnerClientId.Value == MatchManager.NoWinner)
                headline = "IT'S A TIE";
            else
                headline = "MATCH OVER";

            return headline + "\n<size=36>" + match.ResultText.Value.ToString() + "</size>";
        }

        if (combat.IsEliminated.Value)
        {
            double now = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0;
            int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(combat.RespawnTime.Value - now)));

            return "<color=#FF5A4A>ELIMINATED</color>\n<size=40>Respawning in " + seconds + "</size>";
        }

        if (match != null && match.IsSpawned)
        {
            if (match.State.Value == MatchState.Countdown)
                return "GET READY\n<size=96>" + Mathf.CeilToInt(match.RemainingSeconds) + "</size>";

            if (match.State.Value == MatchState.Playing && Time.time - playingStartedTime < goMessageDuration)
                return "<i><size=120><color=#6BFF7A>GO!</color></size></i>";
        }

        return "";
    }

    // ------------------------------------------------------------------
    // Feedback
    // ------------------------------------------------------------------

    private void OnLocalFeedback(CombatFeedback type, string message)
    {
        switch (type)
        {
            case CombatFeedback.HitConfirmed:
                hitMarkerImage.enabled = true;
                hitMarkerHideTime = Time.time + hitMarkerDuration;
                ShowFeed(message, new Color(0.5f, 1f, 0.5f));
                break;

            case CombatFeedback.Damaged:
                damageFlashAlpha = 0.45f;
                SetFlashAlpha(damageFlashAlpha);
                ShowFeed(message, new Color(1f, 0.45f, 0.4f));
                break;

            case CombatFeedback.Eliminated:
                damageFlashAlpha = 0.7f;
                SetFlashAlpha(damageFlashAlpha);
                ShowFeed(message, new Color(1f, 0.45f, 0.4f));
                break;

            case CombatFeedback.EliminatedOther:
                ShowFeed(message, new Color(1f, 0.85f, 0.3f));
                break;

            case CombatFeedback.Respawned:
                ShowFeed(message, Color.white);
                break;

            case CombatFeedback.ShotRejected:
                if (!string.IsNullOrEmpty(message))
                    ShowFeed(message, new Color(0.8f, 0.8f, 0.8f));
                break;
        }
    }

    private void OnMatchStateChanged(MatchState previous, MatchState current)
    {
        if (current == MatchState.Playing)
            playingStartedTime = Time.time;
    }

    private void ShowFeed(string message, Color color)
    {
        if (string.IsNullOrEmpty(message))
            return;

        feedText.text = message;
        feedText.color = color;
        feedHideTime = Time.time + feedDuration;
    }

    private void SetFlashAlpha(float alpha)
    {
        if (damageFlash == null)
            return;

        Color color = damageFlash.color;
        color.a = alpha;
        damageFlash.color = color;
        damageFlash.enabled = alpha > 0.001f;
    }

    // ------------------------------------------------------------------
    // Layout (built once, at start-up)
    // ------------------------------------------------------------------

    private void BuildLayout()
    {
        material = HudArt.OverlayMaterial;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Replace the panels the editor setup scripts created in the scene.
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        ((RectTransform)transform).sizeDelta = CanvasSize;

        // Messages (always available, also outside a match).
        centerText = NewText(transform, "CenterMessage", new Vector2(0f, 60f), new Vector2(940f, 240f), 72, TextAnchor.MiddleCenter, Color.white);
        centerText.text = "";

        feedText = NewText(transform, "Feed", new Vector2(0f, 128f), new Vector2(860f, 44f), 28, TextAnchor.MiddleCenter, Color.white);
        feedText.text = "";

        hitMarkerImage = NewImage(transform, "HitMarker", Vector2.zero, new Vector2(56f, 56f), HudArt.HitMarker, HitYellow);
        hitMarkerImage.enabled = false;

        // In-match stats (hidden while the menu is open).
        statsGroup = NewRect(transform, "Stats", Vector2.zero, CanvasSize).gameObject;
        Transform stats = statsGroup.transform;

        // Top centre: match time.
        RectTransform timePanel = NewPanel(stats, "TimePanel", new Vector2(0f, 262f), new Vector2(232f, 112f), Cyan);
        NewImage(timePanel, "Icon", new Vector2(-32f, 27f), new Vector2(26f, 26f), HudArt.Clock, LabelColor);
        NewText(timePanel, "Label", new Vector2(32f, 27f), new Vector2(90f, 28f), 22, TextAnchor.MiddleLeft, LabelColor).text = "TIME";
        timerText = NewText(timePanel, "Value", new Vector2(0f, -14f), new Vector2(210f, 62f), 56, TextAnchor.MiddleCenter, Color.white);
        AddGlow(timerText);

        // Top left: rank.
        RectTransform rankPanel = NewPanel(stats, "RankPanel", new Vector2(-214f, 268f), new Vector2(192f, 96f), Cyan);
        NewImage(rankPanel, "Icon", new Vector2(-34f, 20f), new Vector2(26f, 26f), HudArt.Trophy, LabelColor);
        NewText(rankPanel, "Label", new Vector2(30f, 20f), new Vector2(90f, 28f), 22, TextAnchor.MiddleLeft, LabelColor).text = "RANK";
        rankValue = NewText(rankPanel, "Value", new Vector2(0f, -16f), new Vector2(170f, 48f), 42, TextAnchor.MiddleCenter, Cyan);
        AddGlow(rankValue);

        // Top right: score.
        RectTransform scorePanel = NewPanel(stats, "ScorePanel", new Vector2(214f, 268f), new Vector2(192f, 96f), Cyan);
        NewImage(scorePanel, "Icon", new Vector2(-42f, 20f), new Vector2(26f, 26f), HudArt.Target, LabelColor);
        NewText(scorePanel, "Label", new Vector2(22f, 20f), new Vector2(90f, 28f), 22, TextAnchor.MiddleLeft, LabelColor).text = "SCORE";
        scoreText = NewText(scorePanel, "Value", new Vector2(0f, -16f), new Vector2(170f, 48f), 42, TextAnchor.MiddleCenter, Color.white);
        AddGlow(scoreText);

        // Game mode tag under the time.
        RectTransform modeTag = NewPanel(stats, "ModeTag", new Vector2(0f, 186f), new Vector2(214f, 48f), Orange);
        NewText(modeTag, "Label", Vector2.zero, new Vector2(200f, 30f), 22, TextAnchor.MiddleCenter, Orange).text = "FREE FOR ALL";

        // Bottom left: health.
        RectTransform healthPanel = NewPanel(stats, "HealthPanel", new Vector2(-336f, -262f), new Vector2(340f, 108f), Cyan);
        healthIcon = NewImage(healthPanel, "Icon", new Vector2(-128f, 16f), new Vector2(46f, 46f), HudArt.Heart, HealthGreen);
        NewText(healthPanel, "Label", new Vector2(-40f, 18f), new Vector2(120f, 28f), 22, TextAnchor.MiddleLeft, LabelColor).text = "HEALTH";
        healthText = NewText(healthPanel, "Value", new Vector2(70f, 18f), new Vector2(150f, 52f), 46, TextAnchor.MiddleRight, Color.white);
        RectTransform healthBarRow = NewRect(healthPanel, "Bar", new Vector2(0f, -28f), new Vector2(292f, 14f));
        BuildSegments(healthBarRow, healthFill, HealthSegments);

        // Bottom right: ammo.
        RectTransform ammoPanel = NewPanel(stats, "AmmoPanel", new Vector2(336f, -262f), new Vector2(340f, 108f), Cyan);
        NewImage(ammoPanel, "Icon", new Vector2(-128f, 16f), new Vector2(46f, 46f), HudArt.Bullets, Orange);
        NewText(ammoPanel, "Label", new Vector2(-40f, 18f), new Vector2(120f, 28f), 22, TextAnchor.MiddleLeft, LabelColor).text = "AMMO";
        ammoText = NewText(ammoPanel, "Value", new Vector2(60f, 18f), new Vector2(170f, 52f), 46, TextAnchor.MiddleRight, Color.white);
        ammoBar = NewRect(ammoPanel, "Bar", new Vector2(0f, -28f), new Vector2(292f, 14f));
        // Ammo segments are built on the first update (magazine size).

        timePanelRect = timePanel;
        rankPanelRect = rankPanel;
        scorePanelRect = scorePanel;
        modeTagRect = modeTag;
        healthPanelRect = healthPanel;
        ammoPanelRect = ammoPanel;
        ApplyPlacement();
    }

    // Moves the panels to their view angles and applies the panel size.
    private void ApplyPlacement()
    {
        float x = Mathf.Tan(sideAngle * Mathf.Deg2Rad) * DistancePixels;
        float top = Mathf.Tan(topAngle * Mathf.Deg2Rad) * DistancePixels;
        float bottom = Mathf.Tan(bottomAngle * Mathf.Deg2Rad) * DistancePixels;
        float s = panelScale;

        // Four corners: rank and score above health and ammo, with their
        // outer edges lined up (the top panels are 148 px narrower).
        float cornerX = x + 74f * s;

        PlacePanel(timePanelRect, 0f, top, s);
        PlacePanel(modeTagRect, 0f, top - 76f * s, s);
        PlacePanel(rankPanelRect, -cornerX, top, s);
        PlacePanel(scorePanelRect, cornerX, top, s);
        PlacePanel(healthPanelRect, -x, -bottom, s);
        PlacePanel(ammoPanelRect, x, -bottom, s);
    }

    private static void PlacePanel(RectTransform panel, float x, float y, float scale)
    {
        panel.anchoredPosition = new Vector2(x, y);
        panel.localScale = new Vector3(scale, scale, 1f);
    }

    // Editor only: lets the Placement sliders be tuned while the game runs.
    private void OnValidate()
    {
        if (Application.isPlaying && timePanelRect != null)
            ApplyPlacement();
    }

    // Row of rounded segments: a dim background and a coloured fill each.
    private void BuildSegments(RectTransform row, List<Image> fills, int count)
    {
        for (int i = row.childCount - 1; i >= 0; i--)
            Destroy(row.GetChild(i).gameObject);
        fills.Clear();

        const float gap = 4f;
        float width = row.sizeDelta.x;
        float segment = (width - gap * (count - 1)) / count;

        for (int i = 0; i < count; i++)
        {
            float x = -width * 0.5f + segment * 0.5f + i * (segment + gap);
            Image back = NewImage(row, "Segment" + i, new Vector2(x, 0f), new Vector2(segment, row.sizeDelta.y), HudArt.Pill, SegmentOff);

            Image fill = NewImage(back.transform, "Fill", Vector2.zero, Vector2.zero, HudArt.Pill, Color.white);
            RectTransform rect = fill.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            fills.Add(fill);
        }
    }

    // Fills segments from the left; the last one may be partly filled.
    private static void SetSegments(List<Image> fills, float fraction, Color color)
    {
        int count = fills.Count;

        for (int i = 0; i < count; i++)
        {
            float amount = Mathf.Clamp01(fraction * count - i);
            Image fill = fills[i];
            fill.color = color;
            fill.enabled = amount > 0.001f;
            fill.rectTransform.anchorMax = new Vector2(amount, 1f);
        }
    }

    // Dark panel with cut corners and a glowing outline.
    private RectTransform NewPanel(Transform parent, string name, Vector2 center, Vector2 size, Color outline)
    {
        Image fill = NewImage(parent, name, center, size, HudArt.FrameFill, PanelFill);
        NewImage(fill.transform, "Outline", Vector2.zero, size, HudArt.FrameLine, outline);
        return fill.rectTransform;
    }

    private RectTransform NewRect(Transform parent, string name, Vector2 center, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = gameObject.layer;

        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = center;
        rect.sizeDelta = size;
        return rect;
    }

    private Image NewImage(Transform parent, string name, Vector2 center, Vector2 size, Sprite sprite, Color color)
    {
        Image image = NewRect(parent, name, center, size).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        image.material = material;
        return image;
    }

    private Text NewText(Transform parent, string name, Vector2 center, Vector2 size, int fontSize, TextAnchor alignment, Color color)
    {
        Text text = NewRect(parent, name, center, size).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.material = material;

        Shadow shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = TextShadow;
        shadow.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    // Soft cyan halo around the big numbers.
    private static void AddGlow(Text text)
    {
        Outline glow = text.gameObject.AddComponent<Outline>();
        glow.effectColor = CyanGlow;
        glow.effectDistance = new Vector2(2f, 2f);
    }
}
