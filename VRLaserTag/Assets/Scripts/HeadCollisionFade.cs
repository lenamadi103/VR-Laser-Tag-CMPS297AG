using UnityEngine;
using UnityEngine.UI;

// Room-scale safety for walls and cover.
//
// Movement in the game is teleport-only, but players can still physically
// walk (or lean) in their real room. A headset cannot be pushed back, and
// moving the camera against the player's real motion causes motion
// sickness, so the standard VR solution is used instead: when the head
// enters a wall or cover block (or leaves the arena), the view fades to
// black and a hint asks the player to step back. Nothing behind the wall
// can be seen, so walking through geometry gives no advantage, and the
// player's shots are held while the view is blocked (see IsViewBlocked).
//
// Visual/local only: nothing is synchronised. No per-frame allocations.
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

    [Header("Fade")]
    [Tooltip("Alpha change per second when fading out (to black).")]
    public float fadeInSpeed = 6f;

    [Tooltip("Alpha change per second when fading back in.")]
    public float fadeOutSpeed = 3f;

    private static readonly Collider[] Overlaps = new Collider[8];
    private float alpha;

    private void OnDisable()
    {
        IsViewBlocked = false;
        alpha = 0f;
        Apply();
    }

    private void Start()
    {
        Apply();
    }

    private void LateUpdate()
    {
        if (head == null)
            return;

        float target = Coverage(head.position);
        float speed = target > alpha ? fadeInSpeed : fadeOutSpeed;
        float next = Mathf.MoveTowards(alpha, target, speed * Time.deltaTime);

        IsViewBlocked = target >= 0.99f || next >= 0.9f;

        if (!Mathf.Approximately(next, alpha))
        {
            alpha = next;
            Apply();
        }
    }

    // 0 = clear view, 1 = head inside geometry or out of the arena.
    private float Coverage(Vector3 position)
    {
        // Outside the arena only counts once the player has been placed on a
        // spawn point: before joining, the rig's start position plus the
        // player's real position in their room may lie outside the walls,
        // and the lobby menu must stay visible.
        NetworkVRPlayer player = NetworkVRPlayer.Local;
        if (player != null && player.IsTrackingReady)
        {
            Vector3 local = position - arenaCenter;
            if (Mathf.Abs(local.x) > arenaHalfSize.x || Mathf.Abs(local.z) > arenaHalfSize.y)
                return 1f;
        }

        float reach = headRadius + fadeStartDistance;
        int count = Physics.OverlapSphereNonAlloc(position, reach, Overlaps, obstacleMask, QueryTriggerInteraction.Ignore);

        float coverage = 0f;
        for (int i = 0; i < count; i++)
        {
            Collider c = Overlaps[i];
            if (c == null)
                continue;

            // ClosestPoint returns the position itself when it is inside.
            Vector3 closest = c.ClosestPoint(position);
            float gap = Vector3.Distance(closest, position) - headRadius;

            float value = gap <= 0f ? 1f : 1f - gap / fadeStartDistance;
            if (value > coverage)
                coverage = value;
        }

        return Mathf.Clamp01(coverage);
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
