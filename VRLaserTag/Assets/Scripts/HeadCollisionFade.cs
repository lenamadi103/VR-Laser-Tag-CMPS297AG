using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

// Room-scale safety for walls and cover.
//
// Movement in the game is teleport-only, but players still physically walk
// and lean in their real room, and no collider can stop a real body. Two
// layers make the arena's walls and cover behave as solid:
//
// 1. Push-back (solid cover): the local player's head and body are kept out
//    of walls and cover. When they touch a block, the XR Origin is moved
//    back by the same amount, so walking into a block feels like walking
//    into a wall: the player stays at its surface and cannot come out on
//    the other side. Horizontal only, only while touching something, and
//    never a big jump. The body has some slack for leaning (the feet stay
//    put while the head leans, for example to peek around a corner).
// 2. Fade (fallback): if the head or body still ends up inside geometry
//    (for example a fast lunge) or outside the arena, the view fades to
//    black, a hint asks the player to step back and the gun is held
//    (IsViewBlocked), so being inside a block never gives an advantage.
//
// Local only: nothing is synchronised directly (NetworkVRPlayer sends the
// moved XR Origin to the server as usual). No per-frame allocations.
[DefaultExecutionOrder(1000)] // after tracking has updated the camera
public class HeadCollisionFade : MonoBehaviour
{
    // True while the local player's head is inside geometry or outside the arena.
    public static bool IsViewBlocked { get; private set; }

    [Tooltip("The headset camera.")]
    public Transform head;

    [Tooltip("Full-view black image (alpha is animated).")]
    public Image fadeImage;

    [Tooltip("Hint shown while the view is blocked.")]
    public Text hintText;

    [Header("Detection")]
    [Tooltip("Walls and cover (the arena's solid geometry).")]
    public LayerMask obstacleMask = 1; // Default

    [Tooltip("Radius of the head, in metres.")]
    public float headRadius = 0.1f;

    [Tooltip("The fade starts this far before the head reaches a surface, in metres.")]
    public float fadeStartDistance = 0.12f;

    [Tooltip("Centre of the playable area.")]
    public Vector3 arenaCenter = Vector3.zero;

    [Tooltip("Half of the playable width (x) and depth (z) up to the inner wall faces, in metres.")]
    public Vector2 arenaHalfSize = new Vector2(7.25f, 7.25f);

    [Header("Solid cover (push-back)")]
    [Tooltip("Keep the player's head and body out of walls and cover by moving the XR Origin back.")]
    public bool pushBack = true;

    [Tooltip("Closest the head centre may come to a wall or block, in metres. Keep it above Head Radius + Fade Start Distance so touching a wall does not dim the view.")]
    public float headClearance = 0.24f;

    [Tooltip("Radius of the body (legs and torso), in metres.")]
    public float bodyRadius = 0.18f;

    [Tooltip("How far the head may lean away from the body before the body follows, in metres.")]
    public float leanSlack = 0.25f;

    [Tooltip("Largest correction applied in one frame, in metres. Deeper overlaps are left to the fade.")]
    public float maxCorrection = 0.3f;

    [Header("Fade")]
    [Tooltip("Alpha change per second when fading out (to black).")]
    public float fadeInSpeed = 6f;

    [Tooltip("Alpha change per second when fading back in.")]
    public float fadeOutSpeed = 3f;

    // Body column used for push-back: from just above the floor (the floor
    // itself is ignored) up to below the head.
    private const float BodyBottom = 0.3f;
    private const float NeckDrop = 0.35f;

    private static readonly Collider[] Overlaps = new Collider[8];

    // The teleport floor is on the same layer as walls and cover, but it is
    // never pushed against, and being close to it (kneeling, reaching down)
    // must not black out the view.
    private readonly List<Collider> ignoredColliders = new List<Collider>();

    private float alpha;
    private XROrigin origin;
    private Vector3 bodyAnchor; // on the floor, under the player's body
    private bool anchorValid;

    private void OnEnable()
    {
        NetworkVRPlayer.LocalRigPlaced += ResetBody;
    }

    private void OnDisable()
    {
        NetworkVRPlayer.LocalRigPlaced -= ResetBody;
        IsViewBlocked = false;
        alpha = 0f;
        Apply();
    }

    private void Start()
    {
        // Interactables collect their colliders in Awake, which has run by now.
        foreach (ValidatedTeleportationArea area in FindObjectsByType<ValidatedTeleportationArea>(FindObjectsSortMode.None))
            ignoredColliders.AddRange(area.colliders);

        if (head != null)
            origin = head.GetComponentInParent<XROrigin>();

        Apply();
    }

    private void LateUpdate()
    {
        if (head == null)
            return;

        UpdateBodyAnchor();

        if (pushBack && origin != null)
            KeepOutOfGeometry();

        float target = Coverage(head.position);
        float speed = target > alpha ? fadeInSpeed : fadeOutSpeed;
        float next = Mathf.MoveTowards(alpha, target, speed * Time.deltaTime);

        bool blocked = target >= 0.99f || next >= 0.9f;
        if (blocked != IsViewBlocked || (blocked && Time.unscaledTime >= nextBlockedLog))
            LogState(blocked);
        IsViewBlocked = blocked;

        if (!Mathf.Approximately(next, alpha))
        {
            alpha = next;
            Apply();
        }
    }

    // ------------------------------------------------------------------
    // Push-back
    // ------------------------------------------------------------------

    // Spawns and respawns move the rig: the body starts under the head again.
    private void ResetBody()
    {
        anchorValid = false;
    }

    private float FloorHeight => origin != null ? origin.transform.position.y : 0f;

    // The body stands under the head, but the head may lean away from it by
    // leanSlack: leaning moves the head, not the feet.
    private void UpdateBodyAnchor()
    {
        Vector3 headPosition = head.position;
        Vector3 headOnFloor = new Vector3(headPosition.x, FloorHeight, headPosition.z);

        // First frame, teleport or respawn: more than 1 m in one frame.
        if (!anchorValid || (headOnFloor - bodyAnchor).sqrMagnitude > 1f)
        {
            bodyAnchor = headOnFloor;
            anchorValid = true;
            return;
        }

        Vector3 lean = headOnFloor - bodyAnchor;
        lean.y = 0f;
        float leanDistance = lean.magnitude;

        if (leanDistance > leanSlack)
            bodyAnchor += lean * ((leanDistance - leanSlack) / leanDistance);

        bodyAnchor.y = headOnFloor.y;
    }

    private void KeepOutOfGeometry()
    {
        Vector3 headPosition = head.position;

        Vector3 push = HeadPush(headPosition);
        push += BodyPush(bodyAnchor + push, headPosition.y);
        push.y = 0f;

        float amount = push.magnitude;

        // Not touching anything, or a deep overlap that a small correction
        // cannot fix (the fade handles that case).
        if (amount < 0.0005f || amount > maxCorrection)
            return;

        origin.transform.position += push;
        bodyAnchor += push;

        // The XR Origin has a CharacterController: keep physics in sync with
        // the moved transform (Unity's documented way, no re-enable needed).
        Physics.SyncTransforms();
    }

    private Vector3 HeadPush(Vector3 position)
    {
        Vector3 push = Vector3.zero;
        int count = Physics.OverlapSphereNonAlloc(position, headClearance, Overlaps, obstacleMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider c = Overlaps[i];
            if (c == null || ignoredColliders.Contains(c))
                continue;

            push += HorizontalPush(c, position, headClearance);
        }

        return push;
    }

    private Vector3 BodyPush(Vector3 anchor, float headHeight)
    {
        float floor = FloorHeight;
        float bottom = floor + BodyBottom;
        float top = Mathf.Max(bottom + 0.05f, headHeight - NeckDrop);

        Vector3 p0 = new Vector3(anchor.x, bottom, anchor.z);
        Vector3 p1 = new Vector3(anchor.x, top, anchor.z);

        Vector3 push = Vector3.zero;
        int count = Physics.OverlapCapsuleNonAlloc(p0, p1, bodyRadius, Overlaps, obstacleMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider c = Overlaps[i];
            if (c == null || ignoredColliders.Contains(c))
                continue;

            // Test at a height where the block and the body column overlap,
            // so the contact is beside the body, not above or below it.
            Bounds b = c.bounds;
            float y = Mathf.Clamp((Mathf.Max(bottom, b.min.y) + Mathf.Min(top, b.max.y)) * 0.5f, bottom, top);
            push += HorizontalPush(c, new Vector3(anchor.x, y, anchor.z), bodyRadius);
        }

        return push;
    }

    // Horizontal move that takes a sphere of the given radius at 'point' out
    // of the collider. Zero when the point is already inside (deep overlap:
    // left to the fade) or when the contact is straight above or below.
    private static Vector3 HorizontalPush(Collider c, Vector3 point, float radius)
    {
        Vector3 closest = c.ClosestPoint(point);
        Vector3 delta = point - closest;

        Vector3 side = new Vector3(delta.x, 0f, delta.z);
        float distance = side.magnitude;
        if (distance < 0.0001f)
            return Vector3.zero;

        // Horizontal distance needed so the sphere just touches the surface.
        float needed2 = radius * radius - delta.y * delta.y;
        if (needed2 <= 0f)
            return Vector3.zero;

        float needed = Mathf.Sqrt(needed2);
        if (distance >= needed)
            return Vector3.zero;

        return side * ((needed - distance) / distance);
    }

    // ------------------------------------------------------------------
    // Fade
    // ------------------------------------------------------------------

    private float nextBlockedLog;
    private Collider lastCollider;
    private bool lastOutOfArena;

    // Only on a change (and every 2 s while blocked): says what blocks the view.
    private void LogState(bool blocked)
    {
        nextBlockedLog = Time.unscaledTime + 2f;
        string cause = lastOutOfArena ? "outside the arena bounds " + arenaHalfSize
            : lastCollider != null ? "collider '" + lastCollider.name + "' (" + lastCollider.GetType().Name +
                                     ", parent '" + (lastCollider.transform.parent != null ? lastCollider.transform.parent.name : "-") +
                                     "', bounds " + lastCollider.bounds + ")"
            : "fading";
        Debug.Log("[Fade] " + (blocked ? "VIEW BLOCKED: " + cause : "view clear") +
                  " head=" + head.position.ToString("F2") + " headLocal=" + head.localPosition.ToString("F2") +
                  " origin=" + (origin != null ? origin.transform.position.ToString("F2") : "-") +
                  " body=" + bodyAnchor.ToString("F2") +
                  " trackingReady=" + (NetworkVRPlayer.Local != null && NetworkVRPlayer.Local.IsTrackingReady));
    }

    // 0 = clear view, 1 = head or body inside geometry, or out of the arena.
    private float Coverage(Vector3 position)
    {
        lastOutOfArena = false;
        lastCollider = null;

        // Arena and body checks only count once the player has been placed on
        // a spawn point: before joining, the rig's start position plus the
        // player's real position in their room may lie outside the walls,
        // and the lobby menu must stay visible.
        NetworkVRPlayer player = NetworkVRPlayer.Local;
        if (player != null && player.IsTrackingReady)
        {
            Vector3 local = position - arenaCenter;
            if (Mathf.Abs(local.x) > arenaHalfSize.x || Mathf.Abs(local.z) > arenaHalfSize.y)
            {
                lastOutOfArena = true;
                return 1f;
            }

            if (BodyInsideGeometry())
                return 1f;
        }

        float reach = headRadius + fadeStartDistance;
        int count = Physics.OverlapSphereNonAlloc(position, reach, Overlaps, obstacleMask, QueryTriggerInteraction.Ignore);

        float coverage = 0f;
        for (int i = 0; i < count; i++)
        {
            Collider c = Overlaps[i];
            if (c == null || ignoredColliders.Contains(c))
                continue;

            // ClosestPoint returns the position itself when it is inside.
            Vector3 closest = c.ClosestPoint(position);
            float gap = Vector3.Distance(closest, position) - headRadius;

            float value = gap <= 0f ? 1f : 1f - gap / fadeStartDistance;
            if (value > coverage)
            {
                coverage = value;
                lastCollider = c;
            }
        }

        return Mathf.Clamp01(coverage);
    }

    // The centre line of the body (legs and hips) is inside a wall or block:
    // the player walked deep into low cover, where the head stays above it.
    private bool BodyInsideGeometry()
    {
        if (!anchorValid)
            return false;

        float floor = FloorHeight;
        Vector3 p0 = new Vector3(bodyAnchor.x, floor + BodyBottom, bodyAnchor.z);
        Vector3 p1 = new Vector3(bodyAnchor.x, floor + 1f, bodyAnchor.z);

        int count = Physics.OverlapCapsuleNonAlloc(p0, p1, 0.02f, Overlaps, obstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = Overlaps[i];
            if (c != null && !ignoredColliders.Contains(c))
            {
                lastCollider = c;
                return true;
            }
        }

        return false;
    }

    private void Apply()
    {
        if (fadeImage != null)
        {
            Color color = fadeImage.color;
            color.a = alpha;
            fadeImage.color = color;
            fadeImage.enabled = alpha > 0.001f;
        }

        if (hintText != null)
            hintText.enabled = alpha > 0.6f;
    }
}
