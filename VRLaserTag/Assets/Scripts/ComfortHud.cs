using UnityEngine;

// Keeps the player's HUD at a comfortable reading distance and lets it
// follow the player's view lazily instead of being glued to the headset.
//
// - The HUD lives under the XR Origin's "Camera Offset" (not under the
//   camera), so teleports and spawns carry it along instantly, while head
//   rotation is followed smoothly.
// - It only starts turning after the view has moved more than a threshold
//   angle away from it, then eases back to the centre of the view. Small
//   head movements (aiming, looking around a corner) leave it still.
// - Its distance shrinks in front of walls and cover so it is never drawn
//   "inside" geometry (which would hide it or give conflicting depth cues).
//   The scale changes with the distance, so it keeps the same angular size.
//
// Visual only: no gameplay state is read or written. No per-frame allocations.
[DefaultExecutionOrder(1000)] // after tracking and the player scripts
public class ComfortHud : MonoBehaviour
{
    [Tooltip("The headset camera.")]
    public Transform head;

    [Header("Placement")]
    [Tooltip("Preferred distance from the eyes, in metres.")]
    public float distance = 1.3f;

    [Tooltip("Closest the HUD may come when a wall or cover is in front of the player.")]
    public float minDistance = 0.45f;

    [Tooltip("How far below the line of sight the HUD centre sits, in degrees.")]
    public float pitchOffset = 3f;

    [Tooltip("Highest / lowest the HUD follows the view, in degrees.")]
    public float maxPitch = 35f;

    [Header("Lazy follow")]
    [Tooltip("The HUD starts turning once the view is this far away horizontally (degrees).")]
    public float yawThreshold = 18f;

    [Tooltip("The HUD starts turning once the view is this far away vertically (degrees).")]
    public float pitchThreshold = 14f;

    [Tooltip("It stops turning once it is this close to the centre of the view (degrees).")]
    public float settleAngle = 1.5f;

    [Tooltip("Smoothing time for re-centring, in seconds.")]
    public float rotationSmoothTime = 0.22f;

    [Tooltip("Smoothing time for following head position (walking around), in seconds.")]
    public float positionSmoothTime = 0.06f;

    [Header("Obstacles")]
    [Tooltip("Geometry the HUD should stay in front of (walls and cover).")]
    public LayerMask obstacleMask = 1; // Default

    [Tooltip("Gap kept between the HUD and a wall in front of it, in metres.")]
    public float wallGap = 0.08f;

    [Tooltip("Smoothing time when the distance changes because of a wall, in seconds.")]
    public float distanceSmoothTime = 0.08f;

    private Vector3 baseScale;
    private float yaw, pitch;
    private float yawVelocity, pitchVelocity;
    private bool turningYaw, turningPitch;
    private Vector3 smoothedHead;
    private Vector3 headVelocity;
    private float currentDistance;
    private float distanceVelocity;
    private bool initialised;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        NetworkVRPlayer.LocalRigPlaced += Snap;
        Snap();
    }

    private void OnDisable()
    {
        NetworkVRPlayer.LocalRigPlaced -= Snap;
    }

    // Puts the HUD straight in front of the view, without smoothing.
    public void Snap()
    {
        if (head == null)
            return;

        if (baseScale == Vector3.zero)
            baseScale = transform.localScale;

        ReadHead(out Vector3 headPosition, out float headYaw, out float headPitch, out _);

        yaw = headYaw;
        pitch = Mathf.Clamp(headPitch, -maxPitch, maxPitch);
        yawVelocity = pitchVelocity = 0f;
        turningYaw = turningPitch = false;
        smoothedHead = headPosition;
        headVelocity = Vector3.zero;
        currentDistance = FreeDistance();
        distanceVelocity = 0f;
        initialised = true;

        Apply();
    }

    private void LateUpdate()
    {
        if (head == null)
            return;

        if (!initialised)
        {
            Snap();
            return;
        }

        ReadHead(out Vector3 headPosition, out float headYaw, out float headPitch, out bool yawReliable);
        float targetPitch = Mathf.Clamp(headPitch, -maxPitch, maxPitch);

        // Horizontal: wait until the view is far enough away, then ease back.
        // (Looking almost straight up or down gives no reliable heading.)
        if (yawReliable)
        {
            float yawError = Mathf.Abs(Mathf.DeltaAngle(yaw, headYaw));

            if (!turningYaw && yawError > yawThreshold)
                turningYaw = true;

            if (turningYaw)
            {
                yaw = Mathf.SmoothDampAngle(yaw, headYaw, ref yawVelocity, rotationSmoothTime);

                if (Mathf.Abs(Mathf.DeltaAngle(yaw, headYaw)) < settleAngle)
                {
                    turningYaw = false;
                    yawVelocity = 0f;
                }
            }
        }

        // Vertical: same idea.
        float pitchError = Mathf.Abs(targetPitch - pitch);

        if (!turningPitch && pitchError > pitchThreshold)
            turningPitch = true;

        if (turningPitch)
        {
            pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVelocity, rotationSmoothTime);

            if (Mathf.Abs(targetPitch - pitch) < settleAngle)
            {
                turningPitch = false;
                pitchVelocity = 0f;
            }
        }

        // Walking around: follow the head position closely (a lagging
        // position would make the HUD drift nearer or further).
        smoothedHead = Vector3.SmoothDamp(smoothedHead, headPosition, ref headVelocity, positionSmoothTime);

        currentDistance = Mathf.SmoothDamp(currentDistance, FreeDistance(), ref distanceVelocity, distanceSmoothTime);

        Apply();
    }

    // Distance available in front of the eyes before a wall or cover.
    private float FreeDistance()
    {
        Transform space = transform.parent;
        Quaternion rotation = Quaternion.Euler(pitch + pitchOffset, yaw, 0f);
        Vector3 direction = space != null ? space.TransformDirection(rotation * Vector3.forward) : rotation * Vector3.forward;

        if (Physics.SphereCast(head.position, 0.05f, direction, out RaycastHit hit, distance + wallGap,
                obstacleMask, QueryTriggerInteraction.Ignore))
        {
            return Mathf.Clamp(hit.distance - wallGap, minDistance, distance);
        }

        return distance;
    }

    private void Apply()
    {
        Quaternion rotation = Quaternion.Euler(pitch + pitchOffset, yaw, 0f);

        // The canvas faces the same way as the view (its front is readable
        // when looked at along its forward axis), and it stays upright.
        transform.localPosition = smoothedHead + rotation * Vector3.forward * currentDistance;
        transform.localRotation = rotation;
        transform.localScale = baseScale * (currentDistance / distance);
    }

    // Head position and heading in the HUD's parent space (Camera Offset).
    private void ReadHead(out Vector3 position, out float headYaw, out float headPitch, out bool yawReliable)
    {
        Transform space = transform.parent;
        position = space != null ? space.InverseTransformPoint(head.position) : head.position;
        Vector3 forward = space != null ? space.InverseTransformDirection(head.forward) : head.forward;

        headYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        headPitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg; // positive = looking down
        yawReliable = Mathf.Abs(forward.y) < 0.95f;
    }
}
