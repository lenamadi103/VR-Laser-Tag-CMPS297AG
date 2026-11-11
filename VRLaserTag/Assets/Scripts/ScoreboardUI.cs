using UnityEngine;
using UnityEngine.UI;

// Large scoreboard mounted on an arena wall.
// Shows the match state and timer, the standings (score and deaths),
// and the result when the match ends. Read-only.
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

        var standings = MatchManager.GetStandings();
        string ranks = "#\n", names = "PLAYER\n", scores = "SCORE\n", deaths = "DEATHS\n";

        for (int i = 0; i < standings.Count; i++)
        {
            PlayerInfo info = standings[i];
            ranks += (i + 1) + "\n";
            names += info.ColoredName + "\n";
            scores += info.Score.Value + "\n";
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

    private static void SetText(Text text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
    }
}
