
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.XR.CoreUtils;

public class NetworkVRPlayer : NetworkBehaviour
{
    // The locally owned player on this device (null until spawned).
    public static NetworkVRPlayer Local { get; private set; }

    // Raised on the owner after its XR Origin was placed at a spawn point
    // (first spawn, respawn, match start). The VR menu uses it to re-center.
    public static event System.Action LocalRigPlaced;

    // References to the visual parts of the networked player.
    // These objects are children of the NetworkPlayer prefab.
    [Header("Network Avatar")]
    [SerializeField] private Transform playerBody;
    [SerializeField] private Transform networkHead;
    [SerializeField] private Transform networkLeftHand;
    [SerializeField] private Transform networkRightHand;

    // Renderers hidden only for the player who owns this avatar.
    // Other players can still see the complete avatar.
    [Header("Local Visibility")]
    [SerializeField] private Renderer[] localHiddenRenderers;

    [Header("Network Settings")]

    // Number of tracking updates sent to the server per second.
    // The local avatar still updates every frame.
    [SerializeField] private float sendRate = 20f;

    // Controls how quickly remote avatars approach received poses.
    // Higher values produce faster but less noticeable smoothing.
    [SerializeField] private float smoothingSpeed = 15f;

    // A root movement larger than this between two updates is treated
    // as a teleport and is not interpolated on other clients.
    [SerializeField] private float teleportSnapDistance = 1.5f;

    [Header("Body")]

    // Distance between the top of the body capsule and the headset.
    // The body capsule stretches with the player's real head height,
    // so crouching behind cover also makes the body hitbox smaller.
    [SerializeField] private float neckGap = 0.25f;

    // Spawn initialization state.
    // The server assigns the spawn position and sends it to the owner.
    private bool spawnPositionReceived;
    private Vector3 pendingSpawnPosition;
    private Quaternion pendingSpawnRotation = Quaternion.identity;
    private int pendingSpawnSequence;
    private bool trackingReady;

    // Every server-side spawn/respawn increases this number.
    // Tracking updates sent before the owner applied the newest spawn
    // carry an older number and are ignored by the server, so the avatar
    // does not jump back to where the player was eliminated.
    private int ownerSpawnSequence;
    private int serverSpawnSequence;

    // The local XR rig used by the player controlling this avatar.
    private XROrigin xrOrigin;

    // Real headset and controller transforms from the local XR rig.
    private Transform localHead;
    private Transform localLeftHand;
    private Transform localRightHand;

    private NetworkTransform networkTransform;

    // Determines when the next network update can be sent.
    private float nextSendTime;

    // Latest head and hand positions received over the network.
    // Remote avatars interpolate toward these target positions.
    private Vector3 targetHeadPos;
    private Vector3 targetLeftPos;
    private Vector3 targetRightPos;

    // Latest head and hand rotations received over the network.
    private Quaternion targetHeadRot;
    private Quaternion targetLeftRot;
    private Quaternion targetRightRot;

    // Indicates whether a remote avatar has received its first pose.
    private bool hasReceivedPose;

    public XROrigin LocalXROrigin => xrOrigin;
    public Transform NetworkHead => networkHead;
    public bool IsTrackingReady => trackingReady;


    private void Awake()
    {
        networkTransform = GetComponent<NetworkTransform>();
    }


    // Called when this player object is spawned on the network.
    // Only the owner connects this networked avatar to a local XR rig.
    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;

        Local = this;

        // Hide the owner's duplicate networked avatar.
        // The player's local XR rig already provides their own visuals.
        foreach (Renderer avatarRenderer in localHiddenRenderers)
        {
            if (avatarRenderer != null)
                avatarRenderer.enabled = false;
        }

        // Find the XR Origin in the local scene.
        xrOrigin = FindAnyObjectByType<XROrigin>();

        if (xrOrigin == null || xrOrigin.Camera == null)
        {
            Debug.LogError("XR Origin or VR Camera not found!");
            return;
        }

        // Get the real headset transform.
        localHead = xrOrigin.Camera.transform;

        // Get the real controller transforms.
        // These paths must match the XR Origin hierarchy.
        localLeftHand =
            xrOrigin.transform.Find("Camera Offset/Left Hand");

        localRightHand =
            xrOrigin.transform.Find("Camera Offset/Right Hand");

        if (localLeftHand == null || localRightHand == null)
        {
            Debug.LogError("XR hands not found!");
            return;
        }

        // Initialize tracking if the server's spawn position
        // has already been received.
        TryInitializeSpawn();
    }


    public override void OnNetworkDespawn()
    {
        if (Local == this)
            Local = null;
    }


    // Server only. Moves this player to a spawn point:
    // the networked root moves immediately (without interpolation),
    // and the owner is told to move their local XR Origin.
    //
    // Used for the first spawn, for respawns, and at match start.
    public void ServerMoveToSpawn(Vector3 position, Quaternion rotation)
    {
        if (!IsServer)
            return;

        serverSpawnSequence++;

        // The root keeps an identity rotation. Head and hand poses are
        // sent relative to the root, so a fixed root rotation keeps the
        // math identical on every device.
        if (networkTransform != null && networkTransform.CanCommitToTransform && IsSpawned)
            networkTransform.Teleport(position, Quaternion.identity, transform.localScale);
        else
            transform.SetPositionAndRotation(position, Quaternion.identity);

        InitializeSpawnRpc(
            position,
            rotation,
            serverSpawnSequence,
            RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp)
        );
    }


    // Called by the server for the player who owns this avatar.
    // Receives and stores the assigned spawn position.
    //
    // Storing the position allows initialization to happen even
    // if the XR references are not ready when this RPC executes.
    [Rpc(SendTo.SpecifiedInParams)]
    public void InitializeSpawnRpc(
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        int spawnSequence,
        RpcParams rpcParams = default)
    {
        if (!IsOwner)
            return;

        pendingSpawnPosition = spawnPosition;
        pendingSpawnRotation = spawnRotation;
        pendingSpawnSequence = spawnSequence;
        spawnPositionReceived = true;

        TryInitializeSpawn();
    }


    // Aligns the local XR Origin with the server-assigned spawn.
    // Tracking is enabled only after the spawn position and
    // required XR references are available.
    //
    // This runs for the first spawn and for every respawn.
    private void TryInitializeSpawn()
    {
        if (!IsOwner ||
            !spawnPositionReceived ||
            xrOrigin == null ||
            localHead == null ||
            localLeftHand == null ||
            localRightHand == null)
            return;

        PlaceLocalRig(pendingSpawnPosition, pendingSpawnRotation);

        spawnPositionReceived = false;
        ownerSpawnSequence = pendingSpawnSequence;

        // The owner can now start sending tracking information.
        // Send the new position immediately.
        trackingReady = true;
        nextSendTime = 0f;

        Debug.Log(
            "XR Origin placed at spawn: " +
            pendingSpawnPosition
        );

        LocalRigPlaced?.Invoke();
    }


    // Moves the local XR Origin so that the headset stands
    // horizontally on the spawn point and looks along the spawn's
    // forward direction. The real room-scale offset is preserved.
    private void PlaceLocalRig(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        CharacterController characterController =
            xrOrigin.GetComponent<CharacterController>();

        bool controllerWasEnabled =
            characterController != null && characterController.enabled;

        // A CharacterController can override a direct position change,
        // so it is disabled while the rig is moved.
        if (characterController != null)
            characterController.enabled = false;

        // Turn the rig around the headset so the player faces the
        // spawn's forward direction (usually the arena centre).
        Vector3 cameraForward =
            Vector3.ProjectOnPlane(localHead.forward, Vector3.up);

        Vector3 spawnForward =
            Vector3.ProjectOnPlane(spawnRotation * Vector3.forward, Vector3.up);

        if (cameraForward.sqrMagnitude > 0.001f &&
            spawnForward.sqrMagnitude > 0.001f)
        {
            float angle =
                Vector3.SignedAngle(cameraForward, spawnForward, Vector3.up);

            xrOrigin.RotateAroundCameraUsingOriginUp(angle);
        }

        // Calculate the headset's offset from the XR Origin.
        // In VR, the headset may not be directly above the origin.
        Vector3 headOffset =
            localHead.position - xrOrigin.transform.position;

        // Move the XR Origin so the headset starts horizontally
        // at the assigned spawn point.
        Vector3 originPosition = spawnPosition;

        originPosition.x -= headOffset.x;
        originPosition.z -= headOffset.z;

        xrOrigin.transform.position = originPosition;

        Physics.SyncTransforms();

        if (characterController != null)
            characterController.enabled = controllerWasEnabled;
    }


    // Stores the most recently received tracking pose.
    // Remote avatars gradually move toward these targets.
    private void SetTargetPose(
        Vector3 headPos, Quaternion headRot,
        Vector3 leftPos, Quaternion leftRot,
        Vector3 rightPos, Quaternion rightRot)
    {
        targetHeadPos = headPos;
        targetHeadRot = headRot;

        targetLeftPos = leftPos;
        targetLeftRot = leftRot;

        targetRightPos = rightPos;
        targetRightRot = rightRot;

        // Apply the first received pose immediately so the
        // remote avatar does not interpolate from its default pose.
        if (!hasReceivedPose)
        {
            ApplyPose(
                headPos, headRot,
                leftPos, leftRot,
                rightPos, rightRot
            );

            hasReceivedPose = true;
        }
    }


    // Runs every frame for the locally owned player.
    //
    // Reads the headset and controller tracking data,
    // updates the local avatar immediately, and sends
    // network updates at the configured send rate.
    private void Update()
    {
        if (!IsSpawned || !IsOwner || !trackingReady)
            return;

        // Use the headset's horizontal position as the
        // player's position in the multiplayer world.
        // Keep the XR Origin's Y coordinate as ground height.
        Vector3 playerPosition = localHead.position;
        playerPosition.y = xrOrigin.transform.position.y;

        // The networked head and hands are children of the
        // NetworkPlayer root, so their poses must be expressed
        // relative to that root rather than in world coordinates.
        Quaternion rootRotation = transform.rotation;

        Vector3 headPos =
            Quaternion.Inverse(rootRotation) *
            (localHead.position - playerPosition);

        Vector3 leftPos =
            Quaternion.Inverse(rootRotation) *
            (localLeftHand.position - playerPosition);

        Vector3 rightPos =
            Quaternion.Inverse(rootRotation) *
            (localRightHand.position - playerPosition);

        Quaternion headRot =
            Quaternion.Inverse(rootRotation) * localHead.rotation;

        Quaternion leftRot =
            Quaternion.Inverse(rootRotation) * localLeftHand.rotation;

        Quaternion rightRot =
            Quaternion.Inverse(rootRotation) * localRightHand.rotation;

        // Update the local networked avatar immediately.
        // Local tracking should not wait for network messages.
        ApplyPose(
            headPos, headRot,
            leftPos, leftRot,
            rightPos, rightRot
        );

        // Limit how frequently tracking data is sent.
        // At sendRate = 20, updates are sent about every 0.05 s.
        if (Time.time >= nextSendTime)
        {
            nextSendTime =
                Time.time + 1f / Mathf.Max(1f, sendRate);

            // Send the player's world position and relative
            // headset/controller poses to the server.
            UpdatePlayerRpc(
                ownerSpawnSequence,
                playerPosition,
                headPos, headRot,
                leftPos, leftRot,
                rightPos, rightRot
            );
        }
    }


    // Runs after Update to smooth remote avatar movements.
    //
    // The owner uses live tracking data, so smoothing is
    // applied only to avatars belonging to other players.
    private void LateUpdate()
    {
        if (!IsSpawned || IsOwner || !hasReceivedPose)
            return;

        // Frame-rate-independent smoothing factor.
        // The avatar approaches its target gradually.
        float t =
            1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);

        // Smooth headset position and rotation.
        networkHead.localPosition = Vector3.Lerp(
            networkHead.localPosition,
            targetHeadPos,
            t
        );

        networkHead.localRotation = Quaternion.Slerp(
            networkHead.localRotation,
            targetHeadRot,
            t
        );

        // Smooth left controller position and rotation.
        networkLeftHand.localPosition = Vector3.Lerp(
            networkLeftHand.localPosition,
            targetLeftPos,
            t
        );

        networkLeftHand.localRotation = Quaternion.Slerp(
            networkLeftHand.localRotation,
            targetLeftRot,
            t
        );

        // Smooth right controller position and rotation.
        // The visual gun follows because it is a child
        // of NetworkRightHand.
        networkRightHand.localPosition = Vector3.Lerp(
            networkRightHand.localPosition,
            targetRightPos,
            t
        );

        networkRightHand.localRotation = Quaternion.Slerp(
            networkRightHand.localRotation,
            targetRightRot,
            t
        );

        // Keep the capsule underneath the smoothed headset.
        UpdateBody(networkHead.localPosition);
    }


    // Immediately applies the headset and controller poses
    // to the visual networked avatar.
    //
    // The positions and rotations are relative to the
    // NetworkPlayer root.
    private void ApplyPose(
        Vector3 headPos, Quaternion headRot,
        Vector3 leftPos, Quaternion leftRot,
        Vector3 rightPos, Quaternion rightRot)
    {
        networkHead.SetLocalPositionAndRotation(
            headPos,
            headRot
        );

        networkLeftHand.SetLocalPositionAndRotation(
            leftPos,
            leftRot
        );

        networkRightHand.SetLocalPositionAndRotation(
            rightPos,
            rightRot
        );

        UpdateBody(headPos);
    }


    // Positions the body capsule underneath the headset and stretches
    // it from the floor to just below the head. The capsule is also
    // the body hitbox, so this keeps shots at chest height accurate.
    private void UpdateBody(Vector3 headLocalPosition)
    {
        if (playerBody == null)
            return;

        // A Unity capsule is 2 units tall at scale 1.
        float bodyTop = Mathf.Max(0.5f, headLocalPosition.y - neckGap);
        float halfHeight = bodyTop * 0.5f;

        Vector3 bodyScale = playerBody.localScale;
        bodyScale.y = halfHeight;
        playerBody.localScale = bodyScale;

        playerBody.localPosition = new Vector3(
            headLocalPosition.x,
            halfHeight,
            headLocalPosition.z
        );
    }


    // Runs on the server when an owner sends tracking data.
    //
    // The server updates the NetworkPlayer root position.
    // The NetworkTransform component then synchronizes
    // that root position with the other clients.
    //
    // Head and hand poses are forwarded separately by RPC.
    //
    // Sent unreliably: a lost tracking packet is replaced 50 ms
    // later by a newer one, and resending old poses would only
    // add delay on busy Wi-Fi.
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void UpdatePlayerRpc(
        int spawnSequence,
        Vector3 playerPosition,
        Vector3 headPos, Quaternion headRot,
        Vector3 leftPos, Quaternion leftRot,
        Vector3 rightPos, Quaternion rightRot,
        RpcParams rpcParams = default)
    {
        // Only the owner may move this avatar.
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        // Ignore updates sent before the owner applied the latest
        // spawn/respawn position.
        if (spawnSequence != serverSpawnSequence)
            return;

        // Update the server's authoritative root transform.
        // This currently trusts the position reported by
        // the owning client.
        //
        // Large jumps (teleports) are sent without interpolation
        // so other players see a clean teleport, not a slide.
        bool isTeleport =
            (playerPosition - transform.position).sqrMagnitude >
            teleportSnapDistance * teleportSnapDistance;

        if (isTeleport && networkTransform != null && networkTransform.CanCommitToTransform)
            networkTransform.Teleport(playerPosition, Quaternion.identity, transform.localScale);
        else
            transform.position = playerPosition;

        // For a remote player on the server, store the
        // received tracking pose for visual smoothing.
        // The Host's own player already updates locally.
        if (!IsOwner)
        {
            SetTargetPose(
                headPos, headRot,
                leftPos, leftRot,
                rightPos, rightRot
            );
        }

        // Forward tracking data to non-server clients.
        ApplyPoseClientRpc(
            headPos, headRot,
            leftPos, leftRot,
            rightPos, rightRot
        );
    }


    // Runs on non-server clients when the server forwards
    // a player's headset and controller tracking data.
    //
    // Ignore the locally owned player because its avatar
    // already follows the real headset and controllers.
    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
    private void ApplyPoseClientRpc(
        Vector3 headPos, Quaternion headRot,
        Vector3 leftPos, Quaternion leftRot,
        Vector3 rightPos, Quaternion rightRot)
    {
        if (IsOwner)
            return;

        // Store the received pose for interpolation
        // in LateUpdate().
        SetTargetPose(
            headPos, headRot,
            leftPos, leftRot,
            rightPos, rightRot
        );
    }
}
