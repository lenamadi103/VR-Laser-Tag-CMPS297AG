# Live demonstration and individual rehearsal

This is a preparation plan, not a record of completed tests. The professor approved six team members; the gameplay demonstration still calls for five simultaneous headset players.

## Shared demonstration

1. Show the APK identity, source branch, supported models and controls.
2. Join five headsets including both Quest models. Show separate spawns, tracked avatars and a common arena.
3. Demonstrate valid teleport, blocked/occupied destination rejection and separate physical play areas with boundaries enabled.
4. Show cover stopping shots, accepted damage, ammo/reload, elimination, respawn and synchronized score/health values.
5. Explain simultaneous-fire server ordering and demonstrate the outcome. Disconnect a client, then explain and demonstrate host-loss behavior in a separate session.
6. Show the result and restart/reset. Record Quest 2 responsiveness under representative load.

## Every member

Each member should explain the host/client model, shared coordinates, local XR tracking versus remote avatars and the build/test limitations. Then explain their confirmed contribution and perform a small live modification or debugging exercise.

| Member | Proposed rehearsal task |
| --- | --- |
| Mostafa Bdeir | Explain Canvas physical sizing; adjust one HUD profile value in a disposable demonstration copy and show the result |
| Ali Jomaa | Trace a shot request through server validation and diagnose why a shot is rejected behind cover |
| Rami Bawadi | Trace an invalid teleport or respawn placement and show the relevant guard/state |
| Lena Madi | Trace joining and pose replication; diagnose a wrong host IP or session disconnect |
| Alaa Mansour | Trace match-state transitions and demonstrate where a countdown/timer setting is applied in a disposable copy |
| Sally Sinno | Identify the APK/package/device configuration, explain installation and verify the supplied checksum |

These tasks are proposed exercises, not claims of individually completed work or a guarantee that the professor will accept each exercise as the live task. Agree on the expected scope in advance. Use an isolated copy for demonstration edits so the submitted build remains identifiable. Do not change the submission project merely to rehearse.
