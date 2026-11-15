using UnityEngine;
using UnityEngine.UI;

// Small aim marker for the local laser gun: a dot-and-ring reticle drawn on
// the surface where a shot fired now would land (the same ray as the gun:
// from the muzzle, straight ahead, ignoring the player's own body).
//
// - It sits at the real hit point, so both eyes see it at the right depth.
// - Its size is scaled with distance, so it always looks the same size.
// - Cyan normally, orange when it is on another player, grey while the gun
//   cannot fire (reloading, empty, no match running); it pulses when a hit
//   is confirmed.
// - Hidden while the menu is open, the controller points at a menu, the
//   view is blacked out (head in a wall), or the player is eliminated.
//
// Visual and local only: nothing is synchronised and the server still
// decides every hit. Created by LaserGunShooting. No per-frame allocations.
[DefaultExecutionOrder(1000)] // after tracking has moved the controller
public class AimMarker : MonoBehaviour
{
    private const float MaxDistance = 100f;
    private const float AngularSize = 1.4f;   // degrees across
    private const float SurfaceGap = 0.02f;   // metres towards the viewer
    private const float CanvasPixels = 64f;
    private const float PulseTime = 0.15f;

    private static readonly Color Normal = new Color(0.3f, 0.9f, 1f, 0.9f);
    private static readonly Color OnPlayer = new Color(1f, 0.6f, 0.18f, 1f);
    private static readonly Color CannotFire = new Color(0.75f, 0.75f, 0.75f, 0.45f);
    private static readonly Color HitPulse = new Color(1f, 0.95f, 0.4f, 1f);

    private readonly RaycastHit[] hits = new RaycastHit[16];
    private LaserGunShooting gun;
    private Transform muzzle;
    private Transform view;
    private Image image;
    private float sizeFactor;
    private float pulseUntil;
    private int hitboxLayer;

    public static AimMarker Create(LaserGunShooting gun)
    {
        GameObject go = new GameObject("AimMarker", typeof(RectTransform));
        AimMarker marker = go.AddComponent<AimMarker>();
        marker.gun = gun;
        marker.Build();
        return marker;
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 40;
        ((RectTransform)transform).sizeDelta = new Vector2(CanvasPixels, CanvasPixels);

        GameObject child = new GameObject("Reticle", typeof(RectTransform));
        child.transform.SetParent(transform, false);
        RectTransform rect = (RectTransform)child.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        image = child.AddComponent<Image>();
        image.sprite = HudArt.AimReticle;
        image.raycastTarget = false;
        image.material = HudArt.OverlayMaterial; // drawn on top of the arena
        image.color = Normal;
        image.enabled = false;

        // World size of the canvas = 2 * d * tan(size / 2); per metre of distance:
        sizeFactor = 2f * Mathf.Tan(AngularSize * 0.5f * Mathf.Deg2Rad) / CanvasPixels;
        hitboxLayer = GameLayers.PlayerHitbox;
    }

    private void OnEnable()
    {
        PlayerCombat.LocalFeedback += OnFeedback;
    }

    private void OnDisable()
    {
        PlayerCombat.LocalFeedback -= OnFeedback;
        if (image != null)
            image.enabled = false;
    }

    private void OnFeedback(CombatFeedback type, string message)
    {
        if (type == CombatFeedback.HitConfirmed)
            pulseUntil = Time.time + PulseTime;
    }

    private void LateUpdate()
    {
        if (gun == null)
        {
            Destroy(gameObject);
            return;
        }

        muzzle = gun.muzzlePoint;
        if (view == null && Camera.main != null)
            view = Camera.main.transform;

        PlayerCombat combat = PlayerCombat.Local;
        bool inGame = combat != null && combat.IsSpawned;

        bool show = muzzle != null && view != null && gun.isActiveAndEnabled &&
                    !VRMenuUI.IsVisible && !HeadCollisionFade.IsViewBlocked &&
                    !(inGame && combat.IsEliminated.Value) &&
                    !(gun.uiRayInteractor != null && gun.uiRayInteractor.IsOverUIGameObject());

        if (!show || !FindAimPoint(combat, out Vector3 point, out bool onPlayer))
        {
            if (image.enabled)
                image.enabled = false;
            return;
        }

        // Slightly in front of the surface, facing the viewer.
        Vector3 toView = view.position - point;
        float distance = toView.magnitude;
        if (distance < 0.05f)
        {
            image.enabled = false;
            return;
        }

        Vector3 position = point + toView / distance * Mathf.Min(SurfaceGap, distance * 0.5f);
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(-toView, view.up));
        transform.localScale = Vector3.one * (sizeFactor * distance);

        // The short pause between shots does not count as "cannot fire".
        bool canFire = !inGame || combat.CanFireLocally(out ShotRejectReason reason) ||
                       reason == ShotRejectReason.TooFast;
        float scale = 1f;
        Color color;

        if (Time.time < pulseUntil)
        {
            color = HitPulse;
            scale = 1.4f;
        }
        else if (!canFire)
            color = CannotFire;
        else
            color = onPlayer ? OnPlayer : Normal;

        if (scale != 1f)
            transform.localScale *= scale;

        if (image.color != color)
            image.color = color;
        if (!image.enabled)
            image.enabled = true;
    }

    // Same ray as the gun's predicted beam (LaserGunShooting.PredictEndPoint).
    private bool FindAimPoint(PlayerCombat self, out Vector3 point, out bool onPlayer)
    {
        point = Vector3.zero;
        onPlayer = false;

        int count = Physics.RaycastNonAlloc(muzzle.position, muzzle.forward, hits, MaxDistance,
            ~GameLayers.ShotIgnoreMask, QueryTriggerInteraction.Ignore);

        float closest = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (self != null && self.OwnsCollider(hit.collider))
                continue;

            if (hit.distance < closest)
            {
                closest = hit.distance;
                point = hit.point;
                onPlayer = hit.collider.gameObject.layer == hitboxLayer;
                found = true;
            }
        }

        return found;
    }
}
