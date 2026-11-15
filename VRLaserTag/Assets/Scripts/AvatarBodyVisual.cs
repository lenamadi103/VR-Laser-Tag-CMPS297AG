using UnityEngine;

// Places the armoured torso and legs of a player avatar under its
// synchronised head. Visual only: the hitboxes (PlayerBody capsule and
// NetworkHead sphere) are unchanged and still positioned by NetworkVRPlayer.
//
// No IK: the torso hangs below the neck and stays upright, turns towards
// where the player looks only after a threshold (so it does not spin with
// every head turn), and the legs stretch between the hips and the floor
// (so crouching and different player heights still look right).
//
// Runs only while the avatar is visible (it is hidden for its owner and
// while eliminated). No per-frame allocations.
[DefaultExecutionOrder(100)] // after NetworkVRPlayer has moved the head
public class AvatarBodyVisual : MonoBehaviour
{
    [Tooltip("Synchronised head (NetworkHead).")]
    public Transform head;

    [Tooltip("Torso pivot: the top of the torso, at the base of the neck.")]
    public Transform torso;

    [Tooltip("Legs pivot: the hips. The legs mesh hangs down from it.")]
    public Transform legs;

    [Tooltip("Renderer used to tell whether the avatar is currently drawn.")]
    public Renderer visibilityRenderer;

    [Header("Proportions (metres)")]
    [Tooltip("Base of the neck below the head centre.")]
    public float neckDrop = 0.2f;

    [Tooltip("Base of the neck behind the head centre.")]
    public float neckBack = 0.07f;

    [Tooltip("Neck base to hips.")]
    public float torsoLength = 0.58f;

    [Tooltip("Hips to floor for the modelled legs (legs scale = actual / this).")]
    public float modelledLegLength = 0.86f;

    [Header("Turning")]
    [Tooltip("The torso starts turning once the head is this far to the side (degrees).")]
    public float turnThreshold = 40f;

    [Tooltip("Turning speed towards the view direction, in degrees per second.")]
    public float turnSpeed = 240f;

    private float bodyYaw;
    private bool turning;
    private bool initialised;

    private void LateUpdate()
    {
        Refresh();
    }

    // Also used by the Editor preview renderer.
    public void Refresh()
    {
        if (head == null || torso == null)
            return;

        if (visibilityRenderer != null && !visibilityRenderer.enabled)
        {
            initialised = false;
            return;
        }

        // Head heading in the avatar root's space (the root never rotates).
        Vector3 forward = head.localRotation * Vector3.forward;
        bool reliable = Mathf.Abs(forward.y) < 0.95f;
        float headYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

        if (!initialised)
        {
            bodyYaw = headYaw;
            initialised = true;
        }
        else if (reliable)
        {
            float error = Mathf.DeltaAngle(bodyYaw, headYaw);

            if (!turning && Mathf.Abs(error) > turnThreshold)
                turning = true;

            if (turning)
            {
                bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, headYaw, turnSpeed * Time.deltaTime);
                if (Mathf.Abs(Mathf.DeltaAngle(bodyYaw, headYaw)) < 2f)
                    turning = false;
            }
        }

        Quaternion bodyRotation = Quaternion.Euler(0f, bodyYaw, 0f);

        Vector3 neck = head.localPosition + bodyRotation * new Vector3(0f, 0f, -neckBack);
        neck.y -= neckDrop;
        torso.SetLocalPositionAndRotation(neck, bodyRotation);

        if (legs != null)
        {
            Vector3 hips = neck;
            hips.y = Mathf.Max(0.2f, neck.y - torsoLength);
            legs.SetLocalPositionAndRotation(hips, bodyRotation);

            float stretch = Mathf.Clamp(hips.y / modelledLegLength, 0.35f, 1.3f);
            legs.localScale = new Vector3(1f, stretch, 1f);
        }
    }
}
