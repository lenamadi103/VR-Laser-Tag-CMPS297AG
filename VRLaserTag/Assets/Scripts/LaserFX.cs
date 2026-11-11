using Unity.Netcode;
using UnityEngine;

// Visual-only laser effects. Does not change shooting, damage or hit
// detection; it only reacts when a laser beam becomes visible.
//
// - Muzzle flash at the gun tip for a few frames.
// - Small spark where the beam ends.
// - Beam tinted with the shooter's player colour, so in free-for-all
//   everyone can see who is shooting.
// - Remote beams start at the visible gun tip (beamStart) instead of the
//   avatar's muzzle point, which is kept unchanged for the server.
//
// Used on the local XR rig gun (beam = LaserGunShooting.laserLine) and on
// the NetworkPlayer prefab (beam = PlayerCombat's remote beam).
public class LaserFX : MonoBehaviour
{
    [SerializeField] private LineRenderer beam;
    [Tooltip("Optional: moves the start of the beam to this point when it appears.")]
    [SerializeField] private Transform beamStart;
    [SerializeField] private GameObject muzzleFlash;
    [SerializeField] private GameObject impactSpark;
    [SerializeField] private float flashDuration = 0.06f;
    [Tooltip("True for the gun on the local XR rig (uses the local player's colour).")]
    [SerializeField] private bool localPlayerGun;

    private static readonly Color DefaultColor = new Color(0.3f, 0.85f, 1f);

    private PlayerInfo owner;
    private bool wasVisible;
    private float hideTime;

    private void Awake()
    {
        if (!localPlayerGun)
            owner = GetComponentInParent<PlayerInfo>();

        if (impactSpark != null)
            impactSpark.transform.SetParent(null, true);

        HideEffects();
    }

    private void OnDestroy()
    {
        if (impactSpark != null)
            Destroy(impactSpark);
    }

    private void LateUpdate()
    {
        bool visible = beam != null && beam.enabled;

        if (visible && !wasVisible)
            OnBeamShown();

        wasVisible = visible;

        if (Time.time >= hideTime)
            HideEffects();
    }

    private void OnBeamShown()
    {
        if (beamStart != null)
            beam.SetPosition(0, beamStart.position);

        Color color = ShooterColor();
        beam.startColor = Color.Lerp(color, Color.white, 0.55f);
        beam.endColor = color;

        if (muzzleFlash != null)
        {
            muzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 90f));
            muzzleFlash.SetActive(true);
        }

        if (impactSpark != null)
        {
            impactSpark.transform.position = beam.GetPosition(1);
            impactSpark.transform.rotation = Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), 0f);
            impactSpark.SetActive(true);
        }

        hideTime = Time.time + flashDuration;
    }

    private void HideEffects()
    {
        if (muzzleFlash != null && muzzleFlash.activeSelf)
            muzzleFlash.SetActive(false);

        if (impactSpark != null && impactSpark.activeSelf)
            impactSpark.SetActive(false);
    }

    private Color ShooterColor()
    {
        PlayerInfo info = owner;

        if (localPlayerGun)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening)
                info = PlayerInfo.FindByClientId(network.LocalClientId);
        }

        return info != null && info.PlayerNumber.Value > 0 ? info.PlayerColor : DefaultColor;
    }
}
