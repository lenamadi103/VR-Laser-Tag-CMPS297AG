using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public enum MatchState : byte
{
    WaitingForPlayers,
    Countdown,
    Playing,
    Finished,
}

// Server-authoritative match flow.
//
// Rules:
// - Join: anyone can join at any time (up to 5 players). Players who join
//   during a match play immediately with 0 points.
// - Start: the host presses "Start Match" once at least MinPlayers are
//   connected (or autoStart is on). A countdown runs, then combat unlocks.
// - Scoring: +1 point for each elimination. Deaths are counted too.
// - Win: when the timer ends the highest score wins; equal top scores tie.
//   The match also ends early if too few players remain.
// - Restart: after the results, the host presses "Play Again".
//   Scores, health, ammo and positions are reset.
//
// Only the server changes the state; every client reads the
// NetworkVariables to show the timer, scores and results.
public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance { get; private set; }

    // Raised on every client when the match state changes (previous, current).
    public static event Action<MatchState, MatchState> StateChanged;

    public const ulong NoWinner = ulong.MaxValue;

    [Header("Rules")]
    [Tooltip("Players needed before a match can start. Use 1 for solo testing.")]
    [SerializeField] private int minPlayers = 2;
    [SerializeField] private float countdownDuration = 5f;
    [Tooltip("Match length in seconds (300 = 5 minutes).")]
    [SerializeField] private float matchDuration = 300f;
    [SerializeField] private int pointsPerElimination = 1;
    [Tooltip("Start the countdown automatically when enough players are connected.")]
    [SerializeField] private bool autoStart = false;

    public readonly NetworkVariable<MatchState> State =
        new NetworkVariable<MatchState>(MatchState.WaitingForPlayers);

    // Server time at which the countdown or match ends.
    public readonly NetworkVariable<double> StateEndTime = new NetworkVariable<double>(0);

    public readonly NetworkVariable<FixedString512Bytes> ResultText =
        new NetworkVariable<FixedString512Bytes>();

    // NoWinner when the match ended in a tie (or has no result yet).
    public readonly NetworkVariable<ulong> WinnerClientId =
        new NetworkVariable<ulong>(NoWinner);

    public int MinPlayers => minPlayers;
    public float MatchDuration => matchDuration;
    public int PlayerCount => PlayerInfo.All.Count;

    public bool IsCombatActive => IsSpawned && State.Value == MatchState.Playing;

    // Players do not respawn after the match has finished;
    // they are reset when the next match starts.
    public bool AllowsRespawn => !IsSpawned || State.Value != MatchState.Finished;

    public bool CanStart =>
        IsSpawned &&
        (State.Value == MatchState.WaitingForPlayers || State.Value == MatchState.Finished) &&
        PlayerCount >= minPlayers;

    public float RemainingSeconds =>
        IsSpawned ? Mathf.Max(0f, (float)(StateEndTime.Value - Now)) : 0f;

    private double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

    // Client-side, for countdown beeps.
    private int lastCountdownSecond = -1;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        State.OnValueChanged += OnStateChanged;

        if (IsServer)
        {
            State.Value = MatchState.WaitingForPlayers;
            StateEndTime.Value = 0;
            ResultText.Value = default;
            WinnerClientId.Value = NoWinner;
        }
    }

    public override void OnNetworkDespawn()
    {
        State.OnValueChanged -= OnStateChanged;
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        if (IsServer)
            ServerUpdate();

        PlayCountdownBeeps();
    }

    private void ServerUpdate()
    {
        switch (State.Value)
        {
            case MatchState.WaitingForPlayers:
                if (autoStart && CanStart)
                    ServerStartMatch();
                break;

            case MatchState.Countdown:
                if (PlayerCount < minPlayers)
                {
                    State.Value = MatchState.WaitingForPlayers;
                }
                else if (Now >= StateEndTime.Value)
                {
                    State.Value = MatchState.Playing;
                    StateEndTime.Value = Now + matchDuration;
                }
                break;

            case MatchState.Playing:
                if (Now >= StateEndTime.Value)
                    ServerFinishMatch("Time's up!");
                else if (PlayerCount < minPlayers)
                    ServerFinishMatch("Not enough players left.");
                break;
        }
    }

    // Server only. Called from the host's lobby UI.
    // Resets scores and players, then starts the countdown.
    public void ServerStartMatch()
    {
        if (!IsServer || !CanStart)
            return;

        foreach (PlayerInfo info in PlayerInfo.All)
        {
            info.Score.Value = 0;
            info.Deaths.Value = 0;
        }

        if (SpawnManager.Instance != null)
            SpawnManager.Instance.ServerRespawnAll();

        foreach (PlayerCombat combat in PlayerCombat.All)
            combat.ServerResetCombatState(false);

        ResultText.Value = default;
        WinnerClientId.Value = NoWinner;

        StateEndTime.Value = Now + countdownDuration;
        State.Value = MatchState.Countdown;
    }

    // Server only. Used by the automated test to run short matches.
    public void ConfigureForTesting(int testMinPlayers, float testMatchDuration, float testCountdown)
    {
        if (!IsServer)
            return;

        minPlayers = testMinPlayers;
        matchDuration = testMatchDuration;
        countdownDuration = testCountdown;
    }

    // Server only. Called by PlayerCombat when a player is eliminated.
    // Each elimination is reported once (PlayerCombat guards duplicates).
    public void ServerRecordElimination(ulong attackerClientId, ulong victimClientId)
    {
        if (!IsServer || State.Value != MatchState.Playing)
            return;

        PlayerInfo victim = PlayerInfo.FindByClientId(victimClientId);
        if (victim != null)
            victim.Deaths.Value += 1;

        if (attackerClientId == victimClientId)
            return;

        PlayerInfo attacker = PlayerInfo.FindByClientId(attackerClientId);
        if (attacker != null)
            attacker.Score.Value += pointsPerElimination;
    }

    private void ServerFinishMatch(string reason)
    {
        if (State.Value != MatchState.Playing)
            return;

        // Find the highest score and everyone who has it.
        int bestScore = int.MinValue;
        List<PlayerInfo> leaders = new List<PlayerInfo>();

        foreach (PlayerInfo info in PlayerInfo.All)
        {
            if (info.Score.Value > bestScore)
            {
                bestScore = info.Score.Value;
                leaders.Clear();
                leaders.Add(info);
            }
            else if (info.Score.Value == bestScore)
            {
                leaders.Add(info);
            }
        }

        string result;

        if (leaders.Count == 0)
        {
            WinnerClientId.Value = NoWinner;
            result = "No winner.";
        }
        else if (leaders.Count == 1)
        {
            WinnerClientId.Value = leaders[0].OwnerClientId;
            result = leaders[0].ColoredName + " wins with " + bestScore +
                     (bestScore == 1 ? " point!" : " points!");
        }
        else
        {
            WinnerClientId.Value = NoWinner;

            List<string> names = new List<string>();
            foreach (PlayerInfo info in leaders)
                names.Add(info.ColoredName);

            result = "Tie between " + string.Join(", ", names) +
                     " (" + bestScore + (bestScore == 1 ? " point)" : " points)");
        }

        ResultText.Value = new FixedString512Bytes(Truncate(reason + " " + result, 480));
        StateEndTime.Value = Now;
        State.Value = MatchState.Finished;
    }

    // Sorted standings for the scoreboard: score (high first), then deaths (low first).
    public static List<PlayerInfo> GetStandings()
    {
        List<PlayerInfo> standings = new List<PlayerInfo>(PlayerInfo.All);
        standings.RemoveAll(p => p == null);
        standings.Sort((a, b) =>
        {
            int byScore = b.Score.Value.CompareTo(a.Score.Value);
            if (byScore != 0) return byScore;

            int byDeaths = a.Deaths.Value.CompareTo(b.Deaths.Value);
            if (byDeaths != 0) return byDeaths;

            return a.PlayerNumber.Value.CompareTo(b.PlayerNumber.Value);
        });
        return standings;
    }

    public static string FormatTime(float seconds)
    {
        int total = Mathf.CeilToInt(seconds);
        return (total / 60) + ":" + (total % 60).ToString("00");
    }

    private void OnStateChanged(MatchState previous, MatchState current)
    {
        if (GameAudio.Instance != null)
        {
            if (current == MatchState.Playing)
                GameAudio.Play2D(GameAudio.Instance.matchStart);
            else if (current == MatchState.Finished)
                GameAudio.Play2D(GameAudio.Instance.matchEnd);
        }

        lastCountdownSecond = -1;
        StateChanged?.Invoke(previous, current);
    }

    private void PlayCountdownBeeps()
    {
        if (State.Value != MatchState.Countdown)
            return;

        int second = Mathf.CeilToInt(RemainingSeconds);

        if (second != lastCountdownSecond && second > 0 && second <= 5)
        {
            lastCountdownSecond = second;

            if (GameAudio.Instance != null)
                GameAudio.Play2D(GameAudio.Instance.countdownBeep);
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text.Substring(0, maxLength);
    }
}
