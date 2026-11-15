using UnityEngine;

// Places the player's HUD at a comfortable reading distance (1.3 m).
//
// Two modes:
// - Lock To View (default): the HUD is fixed in the view like a flat-screen
//   game HUD. While the game runs it is a child of the headset camera, so it
//   is drawn with exactly the pose the headset renders with: no lag, no
//   jitter, no drift when walking, turning or teleporting.
// - Lazy follow (Lock To View off): the HUD lives under the XR Origin's
//   "Camera Offset" and only turns once the view has moved more than a
//   threshold angle away from it, then eases back to the centre.
//
// In both modes the distance shrinks in front of walls and cover, and the
// scale shrinks with it (same angular size), so the HUD never sits "inside"
// geometry. The HUD itself is drawn on top of the arena (HudOverlay shader).
//
// Visual only: no gameplay state is read or written. No per-frame allocations.
[DefaultExecutionOrder(1000)] // after tracking and the player scripts
public class ComfortHud : MonoBehaviour
{
    [Tooltip("The headset camera.")]
    public Transform head;

    [Header("Mode")]
    [Tooltip("On: the HUD is fixed in the view like a flat-screen game HUD (it moves rigidly with the head). Off: it follows the view lazily.")]
    public bool lockToView = true;

    [Tooltip("Lock To View: how far below the centre of the view the HUD centre sits, in degrees.")]
    public float lockedPitchOffset = 0f;

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
    private Transform followParent; // Camera Offset (lazy mode)

    private void Awake()
    {
        baseScale = Vector3.one * PlayerHUD.MetresPerCanvasUnit;
        followParent = transform.parent;
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
            baseScale = Vector3.one * PlayerHUD.MetresPerCanvasUnit;

        if (Locked)
        {
            AttachToHead();
            currentDistance = LockedFreeDistance();
            distanceVelocity = 0f;
            initialised = true;
            ApplyLocked();
            return;
        }

        DetachFromHead();

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

        // (Re)place the HUD at start and whenever the mode is switched.
        if (!initialised || Locked != (transform.parent == head))
        {
            Snap();
            return;
        }

        if (Locked)
        {
            currentDistance = Mathf.SmoothDamp(currentDistance, LockedFreeDistance(), ref distanceVelocity, distanceSmoothTime);
            ApplyLocked();
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

    // Locked only while the game runs: in edit mode (editor previews) the
    // HUD keeps its scene parent and is placed like the lazy mode.
    private bool Locked => lockToView && Application.isPlaying;

    // Lock To View: the HUD becomes a child of the headset camera.
    private void AttachToHead()
    {
        if (transform.parent != head)
            transform.SetParent(head, false);
    }

    private void DetachFromHead()
    {
        if (transform.parent == head && followParent != null)
            transform.SetParent(followParent, false);
    }

    private Quaternion LockedRotation => Quaternion.Euler(lockedPitchOffset, 0f, 0f);

    // Lock To View: free distance straight ahead of the view.
    private float LockedFreeDistance()
    {
        Vector3 direction = head.rotation * (LockedRotation * Vector3.forward);

        if (Physics.SphereCast(head.position, 0.05f, direction, out RaycastHit hit, distance + wallGap,
            obstacleMask, QueryTriggerInteraction.Ignore))
        {
            return Mathf.Clamp(hit.distance - wallGap, minDistance, distance);
        }

        return distance;
    }

    private void ApplyLocked()
    {
        Quaternion rotation = LockedRotation;
        transform.localPosition = rotation * Vector3.forward * currentDistance;
        transform.localRotation = rotation;
        // Parent/XR rig scale must not be multiplied into the HUD's physical
        // pixels. Distance compensation preserves the same apparent size near cover.
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        Vector3 physicalScale = baseScale * (currentDistance / distance);
        transform.localScale = new Vector3(
            physicalScale.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            physicalScale.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            physicalScale.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
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
        // Parent/XR rig scale must not be multiplied into the HUD's physical
        // pixels. Distance compensation preserves the same apparent size near cover.
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        Vector3 physicalScale = baseScale * (currentDistance / distance);
        transform.localScale = new Vector3(
            physicalScale.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            physicalScale.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            physicalScale.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
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
