using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Small name tag ("Player 3", in the player's colour) floating above other
// players' heads, so opponents can be told apart in free-for-all.
// Visual only: hidden on your own avatar and while the player is eliminated.
public class PlayerNameLabel : MonoBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField] private GameObject label;
    [SerializeField] private Text labelText;
    [SerializeField] private float heightAboveHead = 0.32f;

    private PlayerInfo info;
    private PlayerCombat combat;
    private NetworkObject networkObject;
    private int shownNumber = -1;
    private Camera viewer;

    private void Awake()
    {
        info = GetComponent<PlayerInfo>();
        combat = GetComponent<PlayerCombat>();
        networkObject = GetComponent<NetworkObject>();
    }

    private void LateUpdate()
    {
        if (label == null || head == null || info == null)
            return;

        bool visible = networkObject != null && networkObject.IsSpawned && !networkObject.IsOwner &&
                       info.PlayerNumber.Value > 0 &&
                       (combat == null || !combat.IsEliminated.Value);

        if (label.activeSelf != visible)
            label.SetActive(visible);

        if (!visible)
            return;

        // Only rebuild the text when the player number changes.
        if (shownNumber != info.PlayerNumber.Value && labelText != null)
        {
            shownNumber = info.PlayerNumber.Value;
            labelText.text = info.DisplayName.ToUpperInvariant();
            labelText.color = info.PlayerColor;
        }

        if (viewer == null || !viewer.isActiveAndEnabled)
            viewer = Camera.main;

        Transform t = label.transform;
        t.position = head.position + Vector3.up * heightAboveHead;

        // Face the viewer but stay upright.
        if (viewer != null)
        {
            Vector3 away = t.position - viewer.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
                t.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
