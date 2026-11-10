using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

// Teleportation area (the arena floor) that only accepts valid destinations:
// - inside the arena,
// - not inside or touching cover/walls,
// - not on top of another living player,
// - not while the local player is eliminated.
//
// Invalid spots show the teleport line in its "blocked" style, and a
// teleport request to an invalid spot is refused.
// Teleporting itself is unchanged: XRI moves the local XR Origin and
// NetworkVRPlayer sends the new position to the server as usual.
public class ValidatedTeleportationArea : TeleportationArea
{
    [Header("Destination Rules")]
    [Tooltip("Centre of the playable area (the floor's centre).")]
    public Vector3 arenaCenter = Vector3.zero;

    [Tooltip("Half of the playable width (x) and depth (z), in metres.")]
    public Vector2 arenaHalfSize = new Vector2(7f, 7f);

    [Tooltip("Minimum horizontal distance from another living player's head.")]
    public float playerClearance = 0.9f;

    [Tooltip("Radius around the destination that must be free of obstacles.")]
    public float obstacleClearance = 0.35f;

    [Tooltip("Layers that count as obstacles (walls and cover).")]
    public LayerMask obstacleMask = 1; // Default layer

    private static readonly Collider[] overlapBuffer = new Collider[16];

    public override bool IsSelectableBy(IXRSelectInteractor interactor)
    {
        if (!base.IsSelectableBy(interactor))
            return false;

        // Lets the teleport line show the blocked style for bad spots.
        if (interactor is XRRayInteractor ray &&
            ray.TryGetCurrent3DRaycastHit(out RaycastHit hit) &&
            hit.collider != null)
        {
            return IsDestinationValid(hit.point);
        }

        return true;
    }

    protected override bool GenerateTeleportRequest(
        IXRInteractor interactor,
        RaycastHit raycastHit,
        ref TeleportRequest teleportRequest)
    {
        if (!base.GenerateTeleportRequest(interactor, raycastHit, ref teleportRequest))
            return false;

        if (!IsDestinationValid(teleportRequest.destinationPosition))
        {
            if (GameAudio.Instance != null)
                GameAudio.Play2D(GameAudio.Instance.denied, 0.6f);

            XRHaptics.Left(0.3f, 0.1f);
            return false;
        }

        return true;
    }

    public bool IsDestinationValid(Vector3 destination)
    {
        PlayerCombat self = PlayerCombat.Local;

        // Eliminated players wait for the respawn instead of moving.
        if (self != null && self.IsSpawned && self.IsEliminated.Value)
            return false;

        // Stay inside the arena.
        Vector3 local = destination - arenaCenter;
        if (Mathf.Abs(local.x) > arenaHalfSize.x || Mathf.Abs(local.z) > arenaHalfSize.y)
            return false;

        // Do not land on another living player.
        foreach (PlayerCombat other in PlayerCombat.All)
        {
            if (other == null || other == self || other.IsEliminated.Value)
                continue;

            Transform head = other.NetworkHead != null ? other.NetworkHead : other.transform;
            Vector3 offset = head.position - destination;
            offset.y = 0f;

            if (offset.magnitude < playerClearance)
                return false;
        }

        // Do not land inside or touching cover/walls.
        Vector3 bottom = destination + Vector3.up * (obstacleClearance + 0.05f);
        Vector3 top = destination + Vector3.up * 1.6f;

        int count = Physics.OverlapCapsuleNonAlloc(
            bottom, top, obstacleClearance,
            overlapBuffer, obstacleMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            // Ignore the floor this area belongs to.
            if (colliders.Contains(overlapBuffer[i]))
                continue;

            return false;
        }

        return true;
    }
}
