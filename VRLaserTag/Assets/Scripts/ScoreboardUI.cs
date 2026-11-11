using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Large free-for-all leaderboard mounted on an arena wall.
// Shows the match state and timer, the standings (rank, player, score;
// deaths only if a deaths column is assigned), and the result when the
// match ends. Read-only.
public class ScoreboardUI : MonoBehaviour
{
    [SerializeField] private Text headerText;
    [SerializeField] private Text rankText;
    [SerializeField] private Text namesText;
    [SerializeField] private Text scoresText;
    [SerializeField] private Text deathsText;
    [SerializeField] private Text footerText;

    private float nextRefreshTime;

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + 0.25f;
        Refresh();
    }

    private void Refresh()
    {
        MatchManager match = MatchManager.Instance;
        bool inSession = match != null && match.IsSpawned;

        if (!inSession)
        {
            SetText(headerText, "VR LASER TAG");
            SetText(rankText, "");
            SetText(namesText, "");
            SetText(scoresText, "");
            SetText(deathsText, "");
            SetText(footerText, "Press the menu button on the left controller to host or join a game.");
            return;
        }

        SetText(headerText, DescribeState(match));

        // Free-for-all leaderboard: the leader is shown in gold and the
        // viewer's own row is marked "(you)".
        var standings = MatchManager.GetStandings();
        string ranks = Column("RANK"), names = Column("PLAYER"), scores = Column("SCORE"), deaths = Column("DEATHS");
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;

        for (int i = 0; i < standings.Count; i++)
        {
            PlayerInfo info = standings[i];
            bool leader = i == 0 && info.Score.Value > 0;
            string you = info.OwnerClientId == localId ? "  <color=#9FB3C8>(you)</color>" : "";

            ranks += (leader ? Gold(i + 1) : (i + 1).ToString()) + "\n";
            names += info.ColoredName + you + "\n";
            scores += (leader ? Gold(info.Score.Value) : info.Score.Value.ToString()) + "\n";
            deaths += info.Deaths.Value + "\n";
        }

        SetText(rankText, ranks);
        SetText(namesText, names);
        SetText(scoresText, scores);
        SetText(deathsText, deaths);

        SetText(footerText, match.State.Value == MatchState.Finished
            ? match.ResultText.Value.ToString()
            : "+1 point per elimination");
    }

    private static string DescribeState(MatchManager match)
    {
        switch (match.State.Value)
        {
            case MatchState.WaitingForPlayers:
                return "WAITING FOR PLAYERS (" + match.PlayerCount + "/" + match.MinPlayers + ")";
            case MatchState.Countdown:
                return "STARTING IN " + Mathf.CeilToInt(match.RemainingSeconds);
            case MatchState.Playing:
                return "TIME LEFT  " + MatchManager.FormatTime(match.RemainingSeconds);
            case MatchState.Finished:
                return "MATCH OVER";
        }

        return "";
    }

    private static string Column(string title)
    {
        return "<size=30><color=#7F93A8>" + title + "</color></size>\n";
    }

    private static string Gold(int value)
    {
        return "<color=#FFD54A><b>" + value + "</b></color>";
    }

    private static void SetText(Text text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
    }
}
