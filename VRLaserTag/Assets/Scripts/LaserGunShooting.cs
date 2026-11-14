using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Local laser gun on the XR Origin's right controller.
//
// In multiplayer, this script only *requests* shots. The local
// player's PlayerCombat sends the request to the server, which decides
// hits and damage. The beam drawn here is a prediction so firing feels
// instant; hit confirmation arrives from the server.
//
// Without a network session the gun still works as a local practice laser.
public class LaserGunShooting : MonoBehaviour
{
    public Transform muzzlePoint;
    public InputActionReference shootAction;

    [Tooltip("Reload button. Defaults: right controller A (primary) button, or F on the keyboard.")]
    public InputAction reloadAction = new InputAction(
        "Reload", InputActionType.Button);

    public float shootDistance = 100f;
    public float laserDuration = 0.1f;
    public LineRenderer laserLine;

    [Header("Feedback")]
    [Tooltip("Optional text on the gun showing ammo / reload state.")]
    public Text ammoText;

    [Tooltip("Ray used for UI. While it points at a UI panel, the trigger clicks UI instead of firing.")]
    public XRRayInteractor uiRayInteractor;

    private Coroutine laserCoroutine;
    private string lastAmmoLabel;

    private void Reset()
    {
        AddDefaultReloadBindings();
    }

    private void Awake()
    {
        if (reloadAction.bindings.Count == 0)
            AddDefaultReloadBindings();

        if (uiRayInteractor == null)
            uiRayInteractor = GetComponentInParent<XRRayInteractor>();
    }

    private void AddDefaultReloadBindings()
    {
        // Usage-based path: works for Quest controllers (A button)
        // and for the XR Device Simulator (B key).
        reloadAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
        reloadAction.AddBinding("<Keyboard>/f");
    }

    void OnEnable()
    {
        if (shootAction != null)
        {
            shootAction.action.Enable();
        }

        reloadAction.Enable();
    }

    void OnDisable()
    {
        if (shootAction != null)
        {
            shootAction.action.Disable();
        }

        reloadAction.Disable();

        if (laserCoroutine != null)
        {
            StopCoroutine(laserCoroutine);
            laserCoroutine = null;
        }

        if (laserLine != null)
        {
            laserLine.enabled = false;
        }
    }

    void Update()
    {
        UpdateAmmoText();

        // Check if the trigger was pressed this frame.
        if (shootAction == null || muzzlePoint == null)
        {
            return;
        }

        if (reloadAction.WasPressedThisFrame())
        {
            Reload();
        }

        if (shootAction.action.WasPressedThisFrame())
        {
            // The same trigger clicks VR menus; do not fire while
            // the controller ray is pointing at a UI panel.
            if (uiRayInteractor != null && uiRayInteractor.IsOverUIGameObject())
                return;

            // The player's head is inside a wall/cover block or outside the
            // arena (room-scale walking): the view is blacked out and shots
            // are held, so walking through geometry gives no advantage.
            if (HeadCollisionFade.IsViewBlocked)
            {
                if (GameAudio.Instance != null)
                    GameAudio.Play2D(GameAudio.Instance.denied, 0.5f);
                return;
            }

            Shoot();
        }
    }

    void Shoot()
    {
        PlayerCombat combat = PlayerCombat.Local;

        if (combat != null && combat.IsSpawned)
        {
            // Quick local check using the synchronized state.
            // The server checks everything again.
            if (!combat.CanFireLocally(out ShotRejectReason reason))
            {
                OnShotBlocked(combat, reason);
                return;
            }

            Vector3 endPoint = PredictEndPoint(combat);

            combat.RequestFire(muzzlePoint.position, muzzlePoint.forward);

            ShowLaser(endPoint);
            PlayShotFeedback();
            return;
        }

        // Offline practice mode (no network session): original behaviour.
        ShowLaser(PredictEndPoint(null));
        PlayShotFeedback();
    }

    void Reload()
    {
        PlayerCombat combat = PlayerCombat.Local;

        if (combat != null && combat.IsSpawned)
            combat.RequestReload();
    }

    // Local raycast used only to draw the predicted beam.
    // Skips the shooter's own (hidden) avatar hitboxes and the local rig.
    Vector3 PredictEndPoint(PlayerCombat self)
    {
        // By default,(if the laser doesn't hit anything) it extends forward to its maximum range.
        Vector3 endPoint = muzzlePoint.position +
                           muzzlePoint.forward * shootDistance;

        int mask = ~GameLayers.ShotIgnoreMask;

        RaycastHit[] hits = Physics.RaycastAll(
            muzzlePoint.position,
            muzzlePoint.forward,
            shootDistance,
            mask,
            QueryTriggerInteraction.Ignore);

        float closest = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (self != null && self.OwnsCollider(hit.collider))
                continue;

            if (hit.distance < closest)
            {
                closest = hit.distance;
                endPoint = hit.point;
            }
        }

        return endPoint;
    }

    void OnShotBlocked(PlayerCombat combat, ShotRejectReason reason)
    {
        if (reason == ShotRejectReason.TooFast)
            return;

        if (reason == ShotRejectReason.OutOfAmmo)
        {
            // Empty gun: click and start reloading automatically.
            if (GameAudio.Instance != null)
                GameAudio.Play2D(GameAudio.Instance.emptyClick);

            combat.RequestReload();
        }
        else if (GameAudio.Instance != null)
        {
            GameAudio.Play2D(GameAudio.Instance.denied, 0.5f);
        }

        XRHaptics.Right(0.2f, 0.05f);
    }

    void PlayShotFeedback()
    {
        if (GameAudio.Instance != null)
            GameAudio.Play2D(GameAudio.Instance.laserShot);

        XRHaptics.Right(0.4f, 0.06f);
    }

    void ShowLaser(Vector3 endPoint)
    {
        // Show the laser between the muzzle and the endpoint.
        if (laserLine != null)
        {
            laserLine.SetPosition(0, muzzlePoint.position);
            laserLine.SetPosition(1, endPoint);
            laserLine.enabled = true;

            // Restart the visibility timer for each shot.
            if (laserCoroutine != null)
            {
                StopCoroutine(laserCoroutine);
            }

            laserCoroutine = StartCoroutine(HideLaser());
        }
    }

    IEnumerator HideLaser()
    {
        yield return new WaitForSeconds(laserDuration);

        laserLine.enabled = false;
        laserCoroutine = null;
    }

    // Small display on the gun: "7 / 10", "RELOADING", "OUT".
    void UpdateAmmoText()
    {
        if (ammoText == null)
            return;

        PlayerCombat combat = PlayerCombat.Local;
        string label;

        if (combat == null || !combat.IsSpawned)
            label = "--";
        else if (combat.IsEliminated.Value)
            label = "OUT";
        else if (combat.IsReloading.Value)
            label = "RELOAD";
        else
            label = combat.Ammo.Value + " / " + combat.MagazineSize;

        if (label != lastAmmoLabel)
        {
            lastAmmoLabel = label;
            ammoText.text = label;
            ammoText.color = combat != null && combat.IsSpawned && combat.Ammo.Value == 0
                ? new Color(1f, 0.35f, 0.3f)
                : Color.white;
        }
    }
}
