using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Head-locked HUD for the local player (child of the VR camera).
// Shows health, ammo, timer, score and big status messages
// (countdown, eliminated + respawn timer, match results),
// plus short feedback for hits and damage.
//
// It only reads synchronized state; it never changes gameplay.
public class PlayerHUD : MonoBehaviour
{
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

    private float feedHideTime;
    private float hitMarkerHideTime;
    private float damageFlashAlpha;
    private float playingStartedTime = -100f;

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
        if (feedText != null) feedText.text = "";
        if (hitMarker != null) hitMarker.enabled = false;
        if (centerText != null) centerText.text = "";
        SetFlashAlpha(0f);
    }

    private void Update()
    {
        PlayerCombat combat = PlayerCombat.Local;
        MatchManager match = MatchManager.Instance;
        bool inGame = combat != null && combat.IsSpawned;

        if (statsGroup != null)
            statsGroup.SetActive(inGame);

        if (inGame)
            UpdateStats(combat, match);

        if (centerText != null)
            centerText.text = inGame ? BuildCenterMessage(combat, match) : "";

        if (feedText != null && feedText.text.Length > 0 && Time.time >= feedHideTime)
            feedText.text = "";

        if (hitMarker != null && hitMarker.enabled && Time.time >= hitMarkerHideTime)
            hitMarker.enabled = false;

        if (damageFlashAlpha > 0f)
        {
            damageFlashAlpha = Mathf.MoveTowards(damageFlashAlpha, 0f, Time.deltaTime * 0.8f);
            SetFlashAlpha(damageFlashAlpha);
        }
    }

    private void UpdateStats(PlayerCombat combat, MatchManager match)
    {
        float healthFraction = combat.MaxHealth > 0 ? (float)combat.Health.Value / combat.MaxHealth : 0f;

        if (healthText != null)
            healthText.text = "HP " + combat.Health.Value + " / " + combat.MaxHealth;

        if (healthBar != null)
        {
            // The bar is stretched over its background; shrink it from the right.
            RectTransform barRect = healthBar.rectTransform;
            barRect.anchorMax = new Vector2(Mathf.Clamp01(healthFraction), barRect.anchorMax.y);
            healthBar.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.3f, 0.95f, 0.4f), healthFraction);
        }

        if (ammoText != null)
        {
            if (combat.IsReloading.Value)
                ammoText.text = "RELOADING...";
            else
                ammoText.text = "AMMO " + combat.Ammo.Value + " / " + combat.MagazineSize;

            ammoText.color = combat.Ammo.Value == 0 && !combat.IsReloading.Value
                ? new Color(1f, 0.35f, 0.3f)
                : Color.white;
        }

        if (timerText != null)
            timerText.text = DescribeTimer(match);

        if (scoreText != null)
        {
            PlayerInfo me = PlayerInfo.FindByClientId(combat.OwnerClientId);

            if (me != null)
            {
                var standings = MatchManager.GetStandings();
                int rank = standings.IndexOf(me) + 1;
                scoreText.text = me.ColoredName + "   Score " + me.Score.Value +
                                 "   #" + rank + " of " + standings.Count;
            }
            else
            {
                scoreText.text = "";
            }
        }
    }

    private static string DescribeTimer(MatchManager match)
    {
        if (match == null || !match.IsSpawned)
            return "";

        switch (match.State.Value)
        {
            case MatchState.Playing: return MatchManager.FormatTime(match.RemainingSeconds);
            case MatchState.Countdown: return "GET READY";
            case MatchState.Finished: return "MATCH OVER";
            default: return "WAITING";
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

            return headline + "\n<size=40>" + match.ResultText.Value.ToString() + "</size>";
        }

        if (combat.IsEliminated.Value)
        {
            double now = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0;
            int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(combat.RespawnTime.Value - now)));

            return "<color=#FF5A4A>ELIMINATED</color>\n<size=44>Respawning in " + seconds + "</size>";
        }

        if (match != null && match.IsSpawned)
        {
            if (match.State.Value == MatchState.Countdown)
                return "GET READY\n<size=90>" + Mathf.CeilToInt(match.RemainingSeconds) + "</size>";

            if (match.State.Value == MatchState.Playing && Time.time - playingStartedTime < goMessageDuration)
                return "<color=#6BFF7A>GO!</color>";
        }

        return "";
    }

    private void OnLocalFeedback(CombatFeedback type, string message)
    {
        switch (type)
        {
            case CombatFeedback.HitConfirmed:
                if (hitMarker != null)
                {
                    hitMarker.enabled = true;
                    hitMarkerHideTime = Time.time + hitMarkerDuration;
                }
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
        if (feedText == null || string.IsNullOrEmpty(message))
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
}
