using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

// Automated multiplayer test (does nothing in normal play).
//
// Activated only by the command-line flag "-lasertag-autotest".
// Several Windows builds (1 host + 2 clients) play a short scripted match;
// the host checks the authoritative results and prints
// "AUTOTEST RESULT: PASS" or "AUTOTEST RESULT: FAIL" to the player log.
//
// Example (see CHANGES.md):
//   VRLaserTag.exe -batchmode -nographics -lasertag-host   -lasertag-autotest -logFile host.log
//   VRLaserTag.exe -batchmode -nographics -lasertag-client 127.0.0.1 -lasertag-autotest -logFile a.log
//   VRLaserTag.exe -batchmode -nographics -lasertag-client 127.0.0.1 -lasertag-autotest -lasertag-autotest-leave 25 -logFile b.log
// For 5 players add "-lasertag-autotest-players 5" to the host and start 4 clients.
// Other options:
//   -lasertag-autotest-final N        players expected at the end (default: players - 1;
//                                     use "players" when a new client rejoins mid-match)
//   -lasertag-autotest-hostquit S     host closes the session S seconds after a client joined
//   -lasertag-autotest-expect-hostloss  client passes if it detects the host leaving
// Every instance logs "FINAL STATE: ..." after the match; the lines must be identical.
//
// Timeline (seconds since the match started; all instances share server time):
//   1-8   Cover test: the host hides behind the centre cover; player 2 shoots at it.
//   9-20  Free fire: everyone shoots the next player (by number) when in sight.
//   21    Simultaneous fire: everyone fires in the same frame.
//   25    One client disconnects (the one started with -lasertag-autotest-leave).
//   22-38 Free fire continues. The 40 s timer ends the match.
//   After the end: winner check, then the host restarts the match and checks the reset.
public class NetworkAutoTest : MonoBehaviour
{
    private const float MatchSeconds = 40f;
    private const float CountdownSeconds = 3f;

    private readonly List<string> failures = new List<string>();
    private readonly StringBuilder log = new StringBuilder();

    private float leaveAt = -1f;
    private int expectedPlayers = 3;
    private int expectedFinalPlayers = -1;
    private float hostQuitAfter = -1f;
    private bool expectHostLoss;
    private bool wasConnected;
    private bool finished;
    private bool leftOnPurpose;
    private bool forcedLateElimination;
    private float nextShotTime;
    private bool simultaneousShotDone;
    private bool coverPositionSet;
    private bool openPositionSet;
    private int hostHealthBeforeCover = -1;
    private int coverShotsFired;

    // Host-side observations.
    private int maxEliminationsSeen;
    private bool sawDamage;
    private bool sawReload;
    private bool sawRespawn;
    private readonly Dictionary<ulong, bool> wasEliminated = new Dictionary<ulong, bool>();
    private readonly HashSet<ulong> knownPlayers = new HashSet<ulong>();
    // Players whose position must be on a spawn point at the given time.
    private readonly List<KeyValuePair<PlayerCombat, float>> spawnChecks = new List<KeyValuePair<PlayerCombat, float>>();
    private int spawnChecksPassed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-lasertag-autotest") < 0)
            return;

        GameObject go = new GameObject("NetworkAutoTest");
        DontDestroyOnLoad(go);
        NetworkAutoTest test = go.AddComponent<NetworkAutoTest>();

        int leaveIndex = Array.IndexOf(args, "-lasertag-autotest-leave");
        if (leaveIndex >= 0 && leaveIndex + 1 < args.Length)
            float.TryParse(args[leaveIndex + 1], out test.leaveAt);

        // Number of players the host waits for (default 3). Exactly one client leaves.
        int playersIndex = Array.IndexOf(args, "-lasertag-autotest-players");
        if (playersIndex >= 0 && playersIndex + 1 < args.Length)
            int.TryParse(args[playersIndex + 1], out test.expectedPlayers);

        int finalIndex = Array.IndexOf(args, "-lasertag-autotest-final");
        if (finalIndex >= 0 && finalIndex + 1 < args.Length)
            int.TryParse(args[finalIndex + 1], out test.expectedFinalPlayers);

        int quitIndex = Array.IndexOf(args, "-lasertag-autotest-hostquit");
        if (quitIndex >= 0 && quitIndex + 1 < args.Length)
            float.TryParse(args[quitIndex + 1], out test.hostQuitAfter);

        test.expectHostLoss = Array.IndexOf(args, "-lasertag-autotest-expect-hostloss") >= 0;
    }

    private void Start()
    {
        Application.targetFrameRate = 60;
        Application.runInBackground = true;
        StartCoroutine(Run());
    }

    private bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    // Clients: detect losing the host (host quit or crashed).
    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (finished || nm == null)
            return;

        if (nm.IsConnectedClient)
            wasConnected = true;

        if (!wasConnected || leftOnPurpose || nm.IsServer || nm.IsClient)
            return;

        LanConnectionManager lan = LanConnectionManager.Instance;
        string status = lan != null ? lan.StatusMessage : "";
        Log("Lost the host. Lobby status: \"" + status + "\"");

        if (expectHostLoss)
        {
            Check(status.StartsWith("Disconnected"), "Lobby did not report the disconnect.");
            Check(lan != null && !lan.IsBusy, "Client still thinks it is in a session.");
            Check(PlayerCombat.Local == null, "Local player object still exists after the host left.");
            Finish(null);
        }
        else
        {
            Finish("Lost the connection to the host unexpectedly.");
        }
    }

    private IEnumerator Run()
    {
        Log("Autotest started. Waiting for the network session...");

        float timeout = Time.realtimeSinceStartup + 60f;
        while (NetworkManager.Singleton == null || !(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsConnectedClient))
        {
            if (Time.realtimeSinceStartup > timeout) { Finish("Never connected."); yield break; }
            yield return null;
        }

        Log("Connected as " + (IsHost ? "HOST" : "CLIENT " + NetworkManager.Singleton.LocalClientId));

        while (MatchManager.Instance == null || !MatchManager.Instance.IsSpawned || PlayerCombat.Local == null)
            yield return null;

        if (IsHost && hostQuitAfter > 0f)
            yield return HostQuitRun();
        else if (IsHost)
            yield return HostRun();
        else
            yield return ClientRun();
    }

    // ------------------------------------------------------------------
    // Host
    // ------------------------------------------------------------------

    // Host-termination test: wait for a client, then close the session.
    private IEnumerator HostQuitRun()
    {
        yield return new WaitUntil(() => PlayerInfo.All.Count >= 2);
        yield return new WaitForSeconds(hostQuitAfter);
        Log("Closing the host session on purpose (host termination test).");
        leftOnPurpose = true;
        LanConnectionManager.Instance.Leave();
        Finish(null);
    }

    private IEnumerator HostRun()
    {
        MatchManager match = MatchManager.Instance;
        match.ConfigureForTesting(2, MatchSeconds, CountdownSeconds);

        // Wait for both clients.
        float timeout = Time.realtimeSinceStartup + 60f;
        while (PlayerInfo.All.Count < expectedPlayers)
        {
            if (Time.realtimeSinceStartup > timeout) { Finish("Clients did not join (" + PlayerInfo.All.Count + " players)."); yield break; }
            yield return null;
        }

        yield return new WaitForSeconds(2f);
        CheckJoin();

        foreach (PlayerInfo info in PlayerInfo.All)
            knownPlayers.Add(info.OwnerClientId);

        match.ServerStartMatch();
        Check(match.State.Value == MatchState.Countdown, "Match did not enter Countdown after Start.");

        yield return new WaitUntil(() => match.State.Value == MatchState.Playing);
        Log("Match is playing.");
        CheckAllReset("match start");

        while (match.State.Value == MatchState.Playing)
        {
            float elapsed = MatchSeconds - match.RemainingSeconds;
            HostObserve();
            HostAct(elapsed);
            yield return null;
        }

        yield return null;
        CheckResult(match);

        // Nobody may respawn after the match has finished.
        List<PlayerCombat> eliminatedAtEnd = PlayerCombat.All.FindAll(c => c.IsEliminated.Value);
        Log("Eliminated at match end: " + eliminatedAtEnd.Count);
        Check(eliminatedAtEnd.Count > 0, "No player was eliminated at the end (no-respawn check could not run).");

        yield return new WaitForSeconds(1.5f);
        LogFinalState();

        yield return new WaitForSeconds(3.5f);
        foreach (PlayerCombat c in eliminatedAtEnd)
        {
            // Skip players who already left the session.
            if (c != null && c.IsSpawned)
                Check(c.IsEliminated.Value, "A player respawned after the match had finished.");
        }
        Log("No-respawn-after-end check done (" + eliminatedAtEnd.Count + " players stayed eliminated).");
        Check(spawnChecksPassed > 0, "No respawn/join position was verified.");
        Log("Spawn position checks passed: " + spawnChecksPassed);

        // Restart and check the reset.
        match.ServerStartMatch();
        yield return new WaitForSeconds(0.5f);
        Check(match.State.Value == MatchState.Countdown, "Restart did not enter Countdown.");
        CheckAllReset("restart");

        // Give the clients time to check the reset before the session closes.
        yield return new WaitForSeconds(3f);
        Finish(null);
    }

    private void CheckJoin()
    {
        HashSet<int> numbers = new HashSet<int>();
        HashSet<Vector3> positions = new HashSet<Vector3>();

        foreach (PlayerInfo info in PlayerInfo.All)
        {
            Check(info.PlayerNumber.Value > 0, "A player has no number.");
            Check(numbers.Add(info.PlayerNumber.Value), "Duplicate player number " + info.PlayerNumber.Value);
            Vector3 p = info.transform.position;
            positions.Add(new Vector3(Mathf.Round(p.x), 0f, Mathf.Round(p.z)));
        }

        Check(positions.Count == PlayerInfo.All.Count, "Players share a spawn position.");
        Log("Join check: " + PlayerInfo.All.Count + " players, numbers " + string.Join(",", numbers));
    }

    private void CheckAllReset(string when)
    {
        foreach (PlayerCombat combat in PlayerCombat.All)
        {
            Check(combat.Health.Value == combat.MaxHealth, when + ": health not reset.");
            Check(combat.Ammo.Value == combat.MagazineSize, when + ": ammo not reset.");
            Check(!combat.IsEliminated.Value, when + ": player still eliminated.");
        }

        foreach (PlayerInfo info in PlayerInfo.All)
            Check(info.Score.Value == 0 && info.Deaths.Value == 0, when + ": score not reset.");
    }

    private void HostObserve()
    {
        int totalDeaths = 0;
        int totalScore = 0;

        foreach (PlayerCombat combat in PlayerCombat.All)
        {
            int h = combat.Health.Value;
            Check(h >= 0 && h <= combat.MaxHealth, "Health out of range: " + h);
            Check(combat.Ammo.Value >= 0 && combat.Ammo.Value <= combat.MagazineSize, "Ammo out of range.");
            Check(!(h == 0 && !combat.IsEliminated.Value), "Player at 0 HP is not eliminated.");

            if (h < combat.MaxHealth) sawDamage = true;
            if (combat.IsReloading.Value) sawReload = true;

            wasEliminated.TryGetValue(combat.OwnerClientId, out bool before);
            if (before && !combat.IsEliminated.Value)
            {
                sawRespawn = true;
                Check(combat.Health.Value == combat.MaxHealth, "Respawned player does not have full health.");
                spawnChecks.Add(new KeyValuePair<PlayerCombat, float>(combat, Time.time + 1f));
            }
            wasEliminated[combat.OwnerClientId] = combat.IsEliminated.Value;
        }

        // A player joining during the match.
        foreach (PlayerInfo info in PlayerInfo.All)
        {
            if (knownPlayers.Add(info.OwnerClientId))
            {
                Log("Late join during the match: " + info.DisplayName);
                PlayerCombat joined = FindByClientId(info.OwnerClientId);
                if (joined != null)
                    spawnChecks.Add(new KeyValuePair<PlayerCombat, float>(joined, Time.time + 1.5f));
            }
        }

        // One second after a respawn/join the player must stand on a spawn point.
        // The server only accepts positions sent after the owner moved its
        // XR Origin, so this also proves the local rig was repositioned.
        for (int i = spawnChecks.Count - 1; i >= 0; i--)
        {
            if (Time.time < spawnChecks[i].Value)
                continue;

            PlayerCombat checkedPlayer = spawnChecks[i].Key;
            spawnChecks.RemoveAt(i);

            if (checkedPlayer == null || !checkedPlayer.IsSpawned)
                continue;

            float distance = DistanceToNearestSpawn(checkedPlayer.transform.position);
            Check(distance < 0.5f, "Player not on a spawn point after respawn/join (" + distance.ToString("0.00") + " m away).");
            if (distance < 0.5f)
                spawnChecksPassed++;
        }

        foreach (PlayerInfo info in PlayerInfo.All)
        {
            totalDeaths += info.Deaths.Value;
            totalScore += info.Score.Value;
        }

        // Every point comes from one elimination of another player.
        Check(totalScore <= totalDeaths, "More points (" + totalScore + ") than eliminations (" + totalDeaths + ").");
        maxEliminationsSeen = Mathf.Max(maxEliminationsSeen, totalDeaths);
    }

    private void HostAct(float elapsed)
    {
        PlayerCombat self = PlayerCombat.Local;
        PlayerCombat shooter = FindByNumber(2);

        if (elapsed < 8.5f)
        {
            // Cover test: stand behind the centre cover, opposite player 2.
            if (!coverPositionSet && shooter != null)
            {
                Vector3 away = -shooter.NetworkHead.position;
                away.y = 0f;
                MoveLocalRig(away.normalized * 2.6f);
                coverPositionSet = true;
            }

            if (elapsed > 1.5f && hostHealthBeforeCover < 0)
                hostHealthBeforeCover = self.Health.Value;

            return;
        }

        if (!openPositionSet)
        {
            Check(self.Health.Value == hostHealthBeforeCover && hostHealthBeforeCover == self.MaxHealth,
                "Cover test: the host took damage through cover (HP " + self.Health.Value + ").");
            Log("Cover test: host HP after cover phase = " + self.Health.Value);

            MoveLocalRig(FindOpenPosition());
            openPositionSet = true;
        }

        // Just before the end, eliminate one player so the
        // "no respawn after the match" rule can be checked.
        if (!forcedLateElimination && elapsed >= MatchSeconds - 1.5f)
        {
            forcedLateElimination = true;
            PlayerCombat victim = PlayerCombat.All.Find(c => c != self && !c.IsEliminated.Value);
            if (victim != null)
            {
                victim.ServerApplyDamage(victim.MaxHealth, null);
                Log("Eliminated a player just before the end (no-respawn-after-end test).");
            }
            return;
        }

        BotFire(elapsed);
    }

    private static float DistanceToNearestSpawn(Vector3 position)
    {
        float best = float.MaxValue;
        SpawnManager spawns = SpawnManager.Instance;

        for (int i = 0; spawns != null && i < spawns.SpawnPointCount; i++)
        {
            Vector3 offset = spawns.GetSpawnPoint(i).position - position;
            offset.y = 0f;
            best = Mathf.Min(best, offset.magnitude);
        }

        return best;
    }

    // Same text on every instance when the state is consistent.
    private void LogFinalState()
    {
        List<string> parts = new List<string>();
        List<PlayerInfo> players = new List<PlayerInfo>(PlayerInfo.All);
        players.Sort((a, b) => a.PlayerNumber.Value.CompareTo(b.PlayerNumber.Value));

        foreach (PlayerInfo info in players)
        {
            PlayerCombat combat = FindByClientId(info.OwnerClientId);
            parts.Add("P" + info.PlayerNumber.Value + " score=" + info.Score.Value + " deaths=" + info.Deaths.Value +
                      " hp=" + (combat != null ? combat.Health.Value : -1) +
                      " out=" + (combat != null && combat.IsEliminated.Value));
        }

        MatchManager match = MatchManager.Instance;
        PlayerInfo winner = PlayerInfo.FindByClientId(match.WinnerClientId.Value);
        Log("FINAL STATE: " + string.Join(" | ", parts) + " | winner=" +
            (match.WinnerClientId.Value == MatchManager.NoWinner ? "tie" : winner != null ? winner.DisplayName : "?"));
    }

    private void CheckResult(MatchManager match)
    {
        Check(match.State.Value == MatchState.Finished, "Match did not finish when the timer ended.");

        var standings = MatchManager.GetStandings();
        int best = standings.Count > 0 ? standings[0].Score.Value : 0;
        int leaders = standings.FindAll(p => p.Score.Value == best).Count;

        if (leaders == 1)
            Check(match.WinnerClientId.Value == standings[0].OwnerClientId, "Wrong winner.");
        else
            Check(match.WinnerClientId.Value == MatchManager.NoWinner, "Tie not reported as a tie.");

        Check(sawDamage, "No damage happened during the match.");
        Check(maxEliminationsSeen > 0, "No elimination happened during the match.");
        Check(sawRespawn, "No respawn was observed.");
        Check(sawReload, "No reload was observed.");
        int finalPlayers = expectedFinalPlayers > 0 ? expectedFinalPlayers : expectedPlayers - 1;
        Check(PlayerInfo.All.Count == finalPlayers, "Expected " + finalPlayers + " players at the end, found " + PlayerInfo.All.Count + ".");

        Log("Result: " + match.ResultText.Value);
        foreach (PlayerInfo info in standings)
            Log("  " + info.DisplayName + " score " + info.Score.Value + " deaths " + info.Deaths.Value);
    }

    // ------------------------------------------------------------------
    // Clients
    // ------------------------------------------------------------------

    private IEnumerator ClientRun()
    {
        MatchManager match = MatchManager.Instance;

        yield return new WaitUntil(() => match.State.Value == MatchState.Playing);
        Log("Match is playing.");

        while (match.State.Value == MatchState.Playing)
        {
            // The host shortened the match to MatchSeconds (not synced, but
            // every test instance uses the same constant).
            float elapsed = MatchSeconds - match.RemainingSeconds;

            if (leaveAt > 0f && elapsed >= leaveAt)
            {
                Log("Leaving the session on purpose (disconnect test).");
                leftOnPurpose = true;
                LanConnectionManager.Instance.Leave();
                Finish(null);
                yield break;
            }

            if (elapsed < 8.5f)
                ClientCoverPhase(elapsed);
            else
                BotFire(elapsed);

            yield return null;
        }

        yield return new WaitForSeconds(1.5f);
        Log("Match ended: " + match.ResultText.Value);
        LogFinalState();

        // Stay until the host restarts, then check the reset arrived here too.
        float restartTimeout = Time.realtimeSinceStartup + 12f;
        while (match.State.Value != MatchState.Countdown && Time.realtimeSinceStartup < restartTimeout)
            yield return null;

        leftOnPurpose = true; // the host closes the session after the restart
        yield return new WaitForSeconds(0.5f);

        PlayerCombat self = PlayerCombat.Local;
        Check(match.State.Value == MatchState.Countdown, "This client did not see the restart.");
        Check(self != null && self.Health.Value == self.MaxHealth && self.Ammo.Value == self.MagazineSize &&
              !self.IsEliminated.Value, "Restart: own health/ammo/elimination not reset on this client.");
        Check(PlayerInfo.All.TrueForAll(p => p.Score.Value == 0 && p.Deaths.Value == 0),
              "Restart: scores not reset on this client.");
        Log("Restart seen on this client; state reset.");

        Finish(null);
    }

    private void ClientCoverPhase(float elapsed)
    {
        PlayerCombat self = PlayerCombat.Local;
        PlayerInfo me = PlayerInfo.FindByClientId(self.OwnerClientId);

        // Only player 2 shoots, at the host hiding behind cover.
        if (me == null || me.PlayerNumber.Value != 2 || elapsed < 2f || Time.time < nextShotTime)
            return;

        PlayerCombat host = FindByClientId(NetworkManager.ServerClientId);
        if (host == null)
            return;

        nextShotTime = Time.time + 0.4f;
        if (TryFireAt(self, host, false))
            coverShotsFired++;

        if (coverShotsFired == 1)
            Log("Cover phase: firing at the host through cover.");
    }

    // ------------------------------------------------------------------
    // Shared bot behaviour
    // ------------------------------------------------------------------

    private void BotFire(float elapsed)
    {
        PlayerCombat self = PlayerCombat.Local;
        if (self == null || self.IsEliminated.Value)
            return;

        // Everyone fires in the same frame once.
        bool simultaneous = !simultaneousShotDone && elapsed >= 21f;
        if (!simultaneous && Time.time < nextShotTime)
            return;

        PlayerCombat target = NextTarget(self);
        if (target == null)
            return;

        if (simultaneous)
        {
            simultaneousShotDone = true;
            Log("Simultaneous fire at elapsed " + elapsed.ToString("0.00"));
        }

        nextShotTime = Time.time + Mathf.Max(0.3f, self.FireInterval);

        if (self.Ammo.Value == 0)
            self.RequestReload();
        else
            TryFireAt(self, target, true);
    }

    // Aims from the networked gun position at the target's head and requests a shot.
    private static bool TryFireAt(PlayerCombat self, PlayerCombat target, bool requireLineOfSight)
    {
        Vector3 origin = self.NetworkHead.position + Vector3.down * 0.2f;
        Vector3 aim = target.NetworkHead.position;

        if (requireLineOfSight && Physics.Linecast(origin, aim, 1, QueryTriggerInteraction.Ignore))
            return false; // Default layer (cover/walls) in the way.

        if (!self.CanFireLocally(out _))
            return false;

        self.RequestFire(origin, (aim - origin).normalized);
        return true;
    }

    private static PlayerCombat NextTarget(PlayerCombat self)
    {
        PlayerInfo me = PlayerInfo.FindByClientId(self.OwnerClientId);
        if (me == null)
            return null;

        PlayerCombat best = null;
        int bestDistance = int.MaxValue;

        foreach (PlayerCombat other in PlayerCombat.All)
        {
            if (other == self || other.IsEliminated.Value)
                continue;

            PlayerInfo info = PlayerInfo.FindByClientId(other.OwnerClientId);
            if (info == null)
                continue;

            // Next player number after mine, wrapping around.
            int distance = (info.PlayerNumber.Value - me.PlayerNumber.Value + 10) % 10;
            if (distance > 0 && distance < bestDistance)
            {
                bestDistance = distance;
                best = other;
            }
        }

        return best;
    }

    private static Vector3 FindOpenPosition()
    {
        // A spot the host can see every other player from.
        Vector3[] candidates =
        {
            new Vector3(0f, 0f, -5.5f), new Vector3(0f, 0f, 5.5f), new Vector3(-5.5f, 0f, 0f),
            new Vector3(5.5f, 0f, 0f), new Vector3(-5f, 0f, -5f), new Vector3(5f, 0f, 5f),
        };

        foreach (Vector3 candidate in candidates)
        {
            bool clear = true;

            foreach (PlayerCombat other in PlayerCombat.All)
            {
                if (other == PlayerCombat.Local)
                    continue;

                Vector3 eye = candidate + Vector3.up * 1.1f;
                if (Physics.Linecast(eye, other.NetworkHead.position, 1, QueryTriggerInteraction.Ignore))
                    clear = false;
            }

            if (clear)
                return candidate;
        }

        return candidates[0];
    }

    private void MoveLocalRig(Vector3 position)
    {
        XROrigin origin = NetworkVRPlayer.Local != null ? NetworkVRPlayer.Local.LocalXROrigin : null;
        if (origin == null)
            return;

        CharacterController cc = origin.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Vector3 headOffset = origin.Camera.transform.position - origin.transform.position;
        origin.transform.position = new Vector3(position.x - headOffset.x, origin.transform.position.y, position.z - headOffset.z);
        Physics.SyncTransforms();

        if (cc != null) cc.enabled = true;
        Log("Moved local rig to " + position);
    }

    private static PlayerCombat FindByNumber(int number)
    {
        foreach (PlayerCombat combat in PlayerCombat.All)
        {
            PlayerInfo info = PlayerInfo.FindByClientId(combat.OwnerClientId);
            if (info != null && info.PlayerNumber.Value == number)
                return combat;
        }

        return null;
    }

    private static PlayerCombat FindByClientId(ulong clientId)
    {
        foreach (PlayerCombat combat in PlayerCombat.All)
        {
            if (combat.OwnerClientId == clientId)
                return combat;
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Reporting
    // ------------------------------------------------------------------

    private void Check(bool condition, string message)
    {
        if (!condition && !failures.Contains(message))
        {
            failures.Add(message);
            Log("CHECK FAILED: " + message);
        }
    }

    private void Log(string message)
    {
        string line = "[AUTOTEST " + Time.realtimeSinceStartup.ToString("0.0") + "s] " + message;
        log.AppendLine(line);
        Debug.Log(line);
    }

    private void Finish(string fatal)
    {
        if (finished)
            return;
        finished = true;

        if (fatal != null)
            failures.Add(fatal);

        Debug.Log("AUTOTEST RESULT: " + (failures.Count == 0 ? "PASS" : "FAIL (" + string.Join(" | ", failures) + ")"));

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        Application.Quit(failures.Count == 0 ? 0 : 1);
    }
}
