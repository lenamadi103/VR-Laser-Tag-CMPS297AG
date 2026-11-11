using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Identity and score of one networked player.
// The server assigns a player number (1-5), which also selects the
// player's color. Score and deaths are written only by the server
// (MatchManager) and read by every client for the HUD and scoreboard.
public class PlayerInfo : NetworkBehaviour
{
    public static readonly List<PlayerInfo> All = new List<PlayerInfo>();

    public static readonly Color[] PlayerColors =
    {
        new Color(0.95f, 0.25f, 0.25f), // 1 red
        new Color(0.25f, 0.55f, 1.00f), // 2 blue
        new Color(0.30f, 0.90f, 0.35f), // 3 green
        new Color(1.00f, 0.85f, 0.20f), // 4 yellow
        new Color(0.75f, 0.35f, 1.00f), // 5 purple
    };

    // Renderers tinted with the player's color (body, head, hands).
    [SerializeField] private Renderer[] colorRenderers;

    public readonly NetworkVariable<int> PlayerNumber = new NetworkVariable<int>(0);
    public readonly NetworkVariable<int> Score = new NetworkVariable<int>(0);
    public readonly NetworkVariable<int> Deaths = new NetworkVariable<int>(0);

    public string DisplayName =>
        PlayerNumber.Value > 0 ? "Player " + PlayerNumber.Value : "Player ?";

    public Color PlayerColor =>
        PlayerNumber.Value > 0
            ? PlayerColors[(PlayerNumber.Value - 1) % PlayerColors.Length]
            : Color.white;

    // Name wrapped in a rich-text color tag, used by the UI.
    public string ColoredName =>
        "<color=#" + ColorUtility.ToHtmlStringRGB(PlayerColor) + ">" + DisplayName + "</color>";

    public override void OnNetworkSpawn()
    {
        All.Add(this);

        if (IsServer)
            PlayerNumber.Value = LowestFreeNumber();

        PlayerNumber.OnValueChanged += OnPlayerNumberChanged;
        ApplyColor();
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        PlayerNumber.OnValueChanged -= OnPlayerNumberChanged;
    }

    private void OnPlayerNumberChanged(int previous, int current)
    {
        ApplyColor();
    }

    private void ApplyColor()
    {
        if (colorRenderers == null)
            return;

        foreach (Renderer r in colorRenderers)
        {
            if (r != null)
                r.material.color = PlayerColor;
        }
    }

    // Server only: reuse numbers freed by players who left.
    private int LowestFreeNumber()
    {
        for (int n = 1; n < 100; n++)
        {
            bool used = false;

            foreach (PlayerInfo other in All)
            {
                if (other != this && other.PlayerNumber.Value == n)
                {
                    used = true;
                    break;
                }
            }

            if (!used)
                return n;
        }

        return 0;
    }

    public static PlayerInfo FindByClientId(ulong clientId)
    {
        foreach (PlayerInfo info in All)
        {
            if (info != null && info.OwnerClientId == clientId)
                return info;
        }

        return null;
    }

    public static string NameOf(ulong clientId)
    {
        PlayerInfo info = FindByClientId(clientId);
        return info != null ? info.ColoredName : "A player";
    }
}
