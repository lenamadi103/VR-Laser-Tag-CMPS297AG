using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// What the local player should be told about (used by the HUD).
public enum CombatFeedback
{
    HitConfirmed,     // My shot hit someone.
    Damaged,          // I was hit.
    Eliminated,       // I was eliminated.
    EliminatedOther,  // I eliminated someone.
    Respawned,        // I am back in the game.
    ShotRejected,     // The server refused my shot (no ammo, reloading...).
}

// Why the server refused a shot.
public enum ShotRejectReason : byte
{
    MatchNotRunning,
    Eliminated,
    Reloading,
    OutOfAmmo,
    TooFast,
    MuzzleBlocked, // The gun tip is inside or behind a wall/cover block.
}

// Server-authoritative combat state of one player.
//
// Authority model:
// - The owning client only sends a *request* to fire (origin + direction).
// - The server checks match state, elimination, reload, ammo and fire rate,
//   then performs its own raycast against cover and player hitboxes.
// - The server alone changes health, ammo, elimination and (through
//   MatchManager) scores. Clients only display these NetworkVariables.
// - The shooter shows the laser immediately (prediction) so firing feels
//   responsive, but hits and damage are only shown when the server confirms.
[RequireComponent(typeof(NetworkVRPlayer))]
public class PlayerCombat : NetworkBehaviour
{
    public static PlayerCombat Local { get; private set; }
    public static readonly List<PlayerCombat> All = new List<PlayerCombat>();

    // Raised on the local client only, for HUD messages and effects.
    public static event Action<CombatFeedback, string> LocalFeedback;

    private const ulong NoTarget = ulong.MaxValue;

    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int damagePerHit = 25;

    [Header("Ammunition")]
    [SerializeField] private int magazineSize = 10;
    [SerializeField] private float reloadDuration = 2f;
    [Tooltip("Minimum seconds between two shots.")]
    [SerializeField] private float fireInterval = 0.25f;

    [Header("Respawn")]
    [SerializeField] private float respawnDelay = 3f;
    [Tooltip("Seconds after respawning during which the player cannot be damaged.")]
    [SerializeField] private float spawnProtection = 1.5f;

    [Header("Server Shot Validation")]
    [SerializeField] private float maxShotDistance = 60f;
    [Tooltip("A shot origin further than this from the shooter's head (as seen by the server) is replaced by the server's own gun position.")]
    [SerializeField] private float maxOriginDistanceFromHead = 1.5f;
    [SerializeField] private LayerMask shotMask = ~0;

    [Header("References")]
    [SerializeField] private Transform networkHead;
    [SerializeField] private Transform gunMuzzle;
    [SerializeField] private LineRenderer remoteBeam;
    [SerializeField] private float beamDuration = 0.1f;
    [Tooltip("Avatar renderers hidden on other clients while this player is eliminated.")]
    [SerializeField] private Renderer[] avatarRenderers;

    // Synchronized state. Only the server writes these.
    public readonly NetworkVariable<int> Health = new NetworkVariable<int>(100);
    public readonly NetworkVariable<int> Ammo = new NetworkVariable<int>(10);
    public readonly NetworkVariable<bool> IsReloading = new NetworkVariable<bool>(false);
    public readonly NetworkVariable<bool> IsEliminated = new NetworkVariable<bool>(false);
    // Server time at which an eliminated player respawns.
    public readonly NetworkVariable<double> RespawnTime = new NetworkVariable<double>(0);

    public int MaxHealth => maxHealth;
    public int MagazineSize => magazineSize;
    public float FireInterval => fireInterval;
    public Transform NetworkHead => networkHead;

    // Server-only bookkeeping.
    private double nextAllowedFireTime;
    private double reloadEndTime;
    private double invulnerableUntil;

    // Client-only.
    private float localNextFireTime;
    private float beamHideTime;
    private bool[] rendererDefaultEnabled;

    private static readonly RaycastHit[] hitBuffer = new RaycastHit[32];

    private double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

    private void Awake()
    {
        // Shots must never hit the local XR rig, UI, or "Ignore Raycast".
        shotMask &= ~GameLayers.ShotIgnoreMask;

        if (avatarRenderers != null)
        {
            rendererDefaultEnabled = new bool[avatarRenderers.Length];
            for (int i = 0; i < avatarRenderers.Length; i++)
                rendererDefaultEnabled[i] = avatarRenderers[i] != null && avatarRenderers[i].enabled;
        }

        if (remoteBeam != null)
            remoteBeam.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        All.Add(this);

        if (IsOwner)
            Local = this;

        if (IsServer)
            ServerResetCombatState(false);

        IsEliminated.OnValueChanged += OnEliminatedChanged;
        IsReloading.OnValueChanged += OnReloadingChanged;
        ApplyEliminatedVisuals(IsEliminated.Value);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);

        if (Local == this)
            Local = null;

        IsEliminated.OnValueChanged -= OnEliminatedChanged;
        IsReloading.OnValueChanged -= OnReloadingChanged;
    }

    private void Update()
    {
        if (remoteBeam != null && remoteBeam.enabled && Time.time >= beamHideTime)
            remoteBeam.enabled = false;

        if (!IsSpawned || !IsServer)
            return;

        // Finish reloading.
        if (IsReloading.Value && Now >= reloadEndTime)
        {
            Ammo.Value = magazineSize;
            IsReloading.Value = false;
        }

        // Respawn after the delay (not after the match has finished;
        // MatchManager resets everyone when the next match starts).
        if (IsEliminated.Value && Now >= RespawnTime.Value)
        {
            bool allowed = MatchManager.Instance == null || MatchManager.Instance.AllowsRespawn;
            if (allowed)
                ServerRespawn();
        }
    }

    // ------------------------------------------------------------------
    // Owner-side API (called by LaserGunShooting on the local XR rig)
    // ------------------------------------------------------------------

    // Local pre-check so the shooter gets instant feedback.
    // The server repeats every check; this is only for responsiveness.
    public bool CanFireLocally(out ShotRejectReason reason)
    {
        reason = ShotRejectReason.TooFast;

        if (!IsSpawned)
            return false;

        if (MatchManager.Instance != null && !MatchManager.Instance.IsCombatActive)
        {
            reason = ShotRejectReason.MatchNotRunning;
            return false;
        }

        if (IsEliminated.Value) { reason = ShotRejectReason.Eliminated; return false; }
        if (IsReloading.Value) { reason = ShotRejectReason.Reloading; return false; }
        if (Ammo.Value <= 0) { reason = ShotRejectReason.OutOfAmmo; return false; }
        if (Time.time < localNextFireTime) { reason = ShotRejectReason.TooFast; return false; }

        return true;
    }

    public void RequestFire(Vector3 origin, Vector3 direction)
    {
        RequestFire(origin, direction, LocalEyePosition());
    }

    // eye: where the shooter's head is at the moment of firing. The server
    // uses it (after a distance check) to make sure the gun is on the
    // shooter's side of every wall and cover block.
    public void RequestFire(Vector3 origin, Vector3 direction, Vector3 eye)
    {
        if (!IsOwner || !IsSpawned)
            return;

        localNextFireTime = Time.time + fireInterval;
        FireRpc(origin, direction, eye);
    }

    // The real headset position on the owner's device. The networked head
    // follows it too, but its root position arrives from the server and
    // lags behind right after a teleport or respawn.
    public Vector3 LocalEyePosition()
    {
        NetworkVRPlayer vrPlayer = GetComponent<NetworkVRPlayer>();
        if (vrPlayer != null && vrPlayer.LocalXROrigin != null && vrPlayer.LocalXROrigin.Camera != null)
            return vrPlayer.LocalXROrigin.Camera.transform.position;

        return networkHead != null ? networkHead.position : transform.position;
    }

    public void RequestReload()
    {
        if (!IsOwner || !IsSpawned)
            return;

        if (IsReloading.Value || IsEliminated.Value || Ammo.Value >= magazineSize)
            return;

        ReloadRpc();
    }

    // Returns true when the collider belongs to this player's own avatar.
    public bool OwnsCollider(Collider other)
    {
        return other != null && other.GetComponentInParent<PlayerCombat>() == this;
    }

    // ------------------------------------------------------------------
    // Server logic
    // ------------------------------------------------------------------

    [Rpc(SendTo.Server)]
    private void FireRpc(Vector3 origin, Vector3 direction, Vector3 eye, RpcParams rpcParams = default)
    {
        // Only the owner may fire this player's gun.
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!ServerCanFire(out ShotRejectReason reason))
        {
            ShotRejectedRpc(reason, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
            return;
        }

        if (float.IsNaN(direction.x) || direction.sqrMagnitude < 0.0001f)
            return;

        direction.Normalize();

        // Do not trust an origin far away from where the server sees
        // this player. Latency and smoothing make small differences normal.
        Vector3 head = networkHead != null ? networkHead.position : transform.position;
        float maxOffset = maxOriginDistanceFromHead * maxOriginDistanceFromHead;

        if (float.IsNaN(origin.x) || (origin - head).sqrMagnitude > maxOffset)
        {
            origin = gunMuzzle != null ? gunMuzzle.position : head;
            eye = head;
        }

        // The reported head position gets the same tolerance.
        if (float.IsNaN(eye.x) || (eye - head).sqrMagnitude > maxOffset)
            eye = head;

        // Cover must also block the shot on the shooter's side: reaching
        // through a thin wall (real room-scale movement) or pushing the gun
        // tip into a block must not let a shot start behind it.
        if (IsMuzzleBlocked(eye, origin))
        {
            ShotRejectedRpc(ShotRejectReason.MuzzleBlocked, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
            return;
        }

        Ammo.Value -= 1;

        // Small tolerance because RPCs can arrive slightly bunched together.
        nextAllowedFireTime = Now + fireInterval * 0.75f;

        PlayerCombat target = ServerRaycast(origin, direction, out Vector3 end);

        bool damaged = target != null && target.ServerApplyDamage(damagePerHit, this);

        ShotFiredRpc(origin, end, damaged ? target.OwnerClientId : NoTarget);

        if (Ammo.Value <= 0)
            ServerStartReload();
    }

    private bool ServerCanFire(out ShotRejectReason reason)
    {
        reason = ShotRejectReason.TooFast;

        if (MatchManager.Instance != null && !MatchManager.Instance.IsCombatActive)
        {
            reason = ShotRejectReason.MatchNotRunning;
            return false;
        }

        if (IsEliminated.Value) { reason = ShotRejectReason.Eliminated; return false; }
        if (IsReloading.Value) { reason = ShotRejectReason.Reloading; return false; }
        if (Ammo.Value <= 0) { reason = ShotRejectReason.OutOfAmmo; return false; }
        if (Now < nextAllowedFireTime) { reason = ShotRejectReason.TooFast; return false; }

        return true;
    }

    // Finds the first thing the shot hits. Cover and walls stop the shot.
    // The shooter's own hitboxes and eliminated players are skipped.
    private PlayerCombat ServerRaycast(Vector3 origin, Vector3 direction, out Vector3 end)
    {
        end = origin + direction * maxShotDistance;

        int count = Physics.RaycastNonAlloc(
            origin, direction, hitBuffer, maxShotDistance,
            shotMask, QueryTriggerInteraction.Ignore);

        Array.Sort(hitBuffer, 0, count, RaycastHitDistanceComparer.Instance);

        for (int i = 0; i < count; i++)
        {
            Collider hitCollider = hitBuffer[i].collider;
            PlayerCombat player = hitCollider.GetComponentInParent<PlayerCombat>();

            if (player == this)
                continue;

            if (player != null && player.IsEliminated.Value)
                continue;

            end = hitBuffer[i].point;
            return player; // null when an obstacle blocked the shot
        }

        return null;
    }

    // Returns true if damage was applied.
    public bool ServerApplyDamage(int amount, PlayerCombat attacker)
    {
        if (!IsServer || IsEliminated.Value || Now < invulnerableUntil)
            return false;

        Health.Value = Mathf.Max(0, Health.Value - amount);

        ulong attackerId = attacker != null ? attacker.OwnerClientId : NoTarget;
        DamagedRpc(attackerId, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));

        if (Health.Value == 0)
            ServerEliminate(attacker);

        return true;
    }

    private void ServerEliminate(PlayerCombat attacker)
    {
        // Guard against duplicate eliminations.
        if (IsEliminated.Value)
            return;

        IsEliminated.Value = true;
        IsReloading.Value = false;
        RespawnTime.Value = Now + respawnDelay;

        ulong attackerId = attacker != null ? attacker.OwnerClientId : NoTarget;

        if (MatchManager.Instance != null)
            MatchManager.Instance.ServerRecordElimination(attackerId, OwnerClientId);

        Vector3 position = networkHead != null ? networkHead.position : transform.position;
        EliminatedRpc(attackerId, position);
    }

    private void ServerRespawn()
    {
        if (SpawnManager.Instance != null)
            SpawnManager.Instance.ServerRespawnPlayer(NetworkObject);

        ServerResetCombatState(true);
    }

    private void ServerStartReload()
    {
        if (IsReloading.Value || IsEliminated.Value || Ammo.Value >= magazineSize)
            return;

        IsReloading.Value = true;
        reloadEndTime = Now + reloadDuration;
    }

    // Full reset: used on spawn, respawn and at the start of each match.
    public void ServerResetCombatState(bool withSpawnProtection)
    {
        if (!IsServer)
            return;

        Health.Value = maxHealth;
        Ammo.Value = magazineSize;
        IsReloading.Value = false;
        IsEliminated.Value = false;
        RespawnTime.Value = 0;
        nextAllowedFireTime = 0;
        invulnerableUntil = withSpawnProtection ? Now + spawnProtection : 0;
    }

    [Rpc(SendTo.Server)]
    private void ReloadRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        ServerStartReload();
    }

    // ------------------------------------------------------------------
    // Client feedback
    // ------------------------------------------------------------------

    [Rpc(SendTo.Everyone)]
    private void ShotFiredRpc(Vector3 origin, Vector3 end, ulong targetClientId)
    {
        if (IsOwner)
        {
            // The shooter already drew the predicted laser.
            // Only confirm a hit here.
            if (targetClientId != NoTarget)
            {
                if (GameAudio.Instance != null)
                    GameAudio.Play2D(GameAudio.Instance.hitConfirm);

                XRHaptics.Right(0.7f, 0.08f);
                RaiseLocalFeedback(CombatFeedback.HitConfirmed, "Hit " + PlayerInfo.NameOf(targetClientId));
            }

            return;
        }

        // Other players: draw the beam from where we see the shooter's gun.
        Vector3 start = gunMuzzle != null ? gunMuzzle.position : origin;
        ShowRemoteBeam(start, end);

        if (GameAudio.Instance != null)
            GameAudio.PlayAt(GameAudio.Instance.laserShot, start, 0.8f);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void DamagedRpc(ulong attackerClientId, RpcParams rpcParams = default)
    {
        if (GameAudio.Instance != null)
            GameAudio.Play2D(GameAudio.Instance.damageTaken);

        XRHaptics.Both(0.8f, 0.15f);
        RaiseLocalFeedback(CombatFeedback.Damaged, "Hit by " + PlayerInfo.NameOf(attackerClientId));
    }

    [Rpc(SendTo.Everyone)]
    private void EliminatedRpc(ulong attackerClientId, Vector3 position)
    {
        if (GameAudio.Instance != null)
            GameAudio.PlayAt(GameAudio.Instance.eliminated, position);

        if (IsOwner)
        {
            XRHaptics.Both(1f, 0.4f);
            RaiseLocalFeedback(CombatFeedback.Eliminated, "Eliminated by " + PlayerInfo.NameOf(attackerClientId));
        }
        else if (NetworkManager.LocalClientId == attackerClientId)
        {
            RaiseLocalFeedback(CombatFeedback.EliminatedOther, "You eliminated " + PlayerInfo.NameOf(OwnerClientId) + "!");
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ShotRejectedRpc(ShotRejectReason reason, RpcParams rpcParams = default)
    {
        if (reason == ShotRejectReason.OutOfAmmo && GameAudio.Instance != null)
            GameAudio.Play2D(GameAudio.Instance.emptyClick);

        RaiseLocalFeedback(CombatFeedback.ShotRejected, DescribeReason(reason));
    }

    public static string DescribeReason(ShotRejectReason reason)
    {
        switch (reason)
        {
            case ShotRejectReason.MatchNotRunning: return "Weapons are locked until the match starts";
            case ShotRejectReason.Eliminated: return "You are eliminated";
            case ShotRejectReason.Reloading: return "Reloading...";
            case ShotRejectReason.OutOfAmmo: return "Out of ammo - press A to reload";
            case ShotRejectReason.MuzzleBlocked: return "Gun blocked by a wall - pull it back";
            default: return "";
        }
    }

    private void OnEliminatedChanged(bool previous, bool current)
    {
        ApplyEliminatedVisuals(current);

        if (IsOwner && previous && !current)
        {
            if (GameAudio.Instance != null)
                GameAudio.Play2D(GameAudio.Instance.respawn);

            RaiseLocalFeedback(CombatFeedback.Respawned, "Back in the game!");
        }
    }

    private void OnReloadingChanged(bool previous, bool current)
    {
        if (!current || GameAudio.Instance == null)
            return;

        if (IsOwner)
            GameAudio.Play2D(GameAudio.Instance.reload);
        else if (networkHead != null)
            GameAudio.PlayAt(GameAudio.Instance.reload, networkHead.position, 0.6f);
    }

    // Eliminated players disappear for everyone else until they respawn.
    // The owner's own avatar is already hidden by NetworkVRPlayer.
    private void ApplyEliminatedVisuals(bool eliminated)
    {
        if (IsOwner || avatarRenderers == null || rendererDefaultEnabled == null)
            return;

        for (int i = 0; i < avatarRenderers.Length; i++)
        {
            if (avatarRenderers[i] != null)
                avatarRenderers[i].enabled = !eliminated && rendererDefaultEnabled[i];
        }

        if (eliminated && remoteBeam != null)
            remoteBeam.enabled = false;
    }

    private void ShowRemoteBeam(Vector3 start, Vector3 end)
    {
        if (remoteBeam == null)
            return;

        remoteBeam.SetPosition(0, start);
        remoteBeam.SetPosition(1, end);
        remoteBeam.enabled = true;
        beamHideTime = Time.time + beamDuration;
    }

    // Walls, cover and the floor are on the Default layer.
    public const int ObstacleMask = 1 << 0;

    // Radius around the gun tip that must be free of walls and cover.
    private const float MuzzleClearance = 0.02f;

    // True when the gun tip is inside a wall/cover block, or a wall/cover
    // block lies between the shooter's head and the gun tip. Used by the
    // server (authoritative) and by the shooter's client (instant feedback).
    public static bool IsMuzzleBlocked(Vector3 eye, Vector3 muzzle)
    {
        if (Physics.CheckSphere(muzzle, MuzzleClearance, ObstacleMask, QueryTriggerInteraction.Ignore))
            return true;

        // Both directions: a linecast does not detect a collider it starts
        // inside (for example a head that is inside a wall).
        return Physics.Linecast(eye, muzzle, ObstacleMask, QueryTriggerInteraction.Ignore) ||
               Physics.Linecast(muzzle, eye, ObstacleMask, QueryTriggerInteraction.Ignore);
    }

    // Shows a shot refusal decided on this device (same text as a server refusal).
    public static void NotifyLocalShotRejected(ShotRejectReason reason)
    {
        RaiseLocalFeedback(CombatFeedback.ShotRejected, DescribeReason(reason));
    }

    private static void RaiseLocalFeedback(CombatFeedback type, string message)
    {
        LocalFeedback?.Invoke(type, message);
    }

    private class RaycastHitDistanceComparer : IComparer<RaycastHit>
    {
        public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();

        public int Compare(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }
    }
}
