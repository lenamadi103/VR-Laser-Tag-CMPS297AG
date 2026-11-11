using UnityEngine;

// Physics layers used by the game. They are created by
// "VR Laser Tag > Setup Project" (Assets/Editor/LaserTagSetup.cs).
//
// PlayerHitbox: colliders on networked avatars. They are used only by
//               shot raycasts and never physically collide with anything.
// LocalRig:     the local XR Origin's CharacterController. Shots ignore it,
//               otherwise the host's own rig would block or absorb shots.
public static class GameLayers
{
    public const string PlayerHitboxName = "PlayerHitbox";
    public const string LocalRigName = "LocalRig";

    public static int PlayerHitbox => LayerMask.NameToLayer(PlayerHitboxName);
    public static int LocalRig => LayerMask.NameToLayer(LocalRigName);

    // Layers that shots must never hit.
    public static int ShotIgnoreMask
    {
        get
        {
            int mask = 1 << 2; // Built-in "Ignore Raycast" layer.

            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) mask |= 1 << ui;

            int rig = LocalRig;
            if (rig >= 0) mask |= 1 << rig;

            return mask;
        }
    }
}
