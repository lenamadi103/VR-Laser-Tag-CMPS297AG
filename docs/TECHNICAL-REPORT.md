# Multiplayer VR Laser Tag Technical Report

CMPS297AG Introduction to XR | 15 November 2026
Team: Mostafa Bdeir, Ali Jomaa, Rami Bawadi, Lena Madi, Alaa Mansour and Sally Sinno

## Project scope

Our project is a fully virtual, free-for-all laser-tag game for Meta Quest 2 and Quest 3. Players share one arena with fixed spawn points and cover while remaining in separate physical play areas. They look and aim using tracked headsets and controllers and move through the virtual arena by teleportation. The implementation supports up to five connected players; our team reports a mixed-device test with three players, not a five-headset demonstration.

The reviewed source is feature/headset-fixes at commit 0104d280a7061ee4d521df5025a6f44631f2b3d7. An installable Quest APK accompanies the source. This report separates implementation, recorded desktop checks and team-reported headset observations.

## Architecture and shared coordinates

Unity runs the same ArenaScene on each device. SpawnManager assigns positions from five predefined spawn points. NetworkVRPlayer connects each locally owned avatar to the device XR Origin and sends head and hand poses relative to its networked root. Other clients interpolate received poses; large position changes are treated as teleports. This establishes consistent virtual coordinates without physical colocation or shared spatial anchors. Avatar head, hand and body representations are visible to other players, while the owner hides the duplicate network avatar.

## Networking and authority

The game uses a host-client model with Netcode for GameObjects and Unity Transport. One player hosts on the local network; others enter the host IP through the VR menu. Connection approval limits the session to five players. The host acts as the authoritative server for hit detection, damage, health, ammunition, reloads, elimination, respawn, scoring, match timing and the winner. Clients supply their own tracking poses and request actions. NetworkVariables distribute persistent state, and RPCs distribute action requests and feedback.

## Combat and cover

A trigger press requests a shot after local state checks and gives immediate visual/audio feedback. The server validates ownership, match state, fire interval, elimination, reload state, ammunition and the shot origin. It sorts raycast hits by distance, skips the shooter and eliminated players, and stops at the first valid obstacle or target. A separate overlap and head-to-muzzle obstruction check rejects a gun tip pushed into or through cover. Damage is 25 HP per accepted hit against a 100 HP player. The magazine holds 10 shots and reloading takes two seconds; an empty magazine reloads automatically. Firing ends spawn protection.

## Match flow and recovery

The host starts a match after at least two players join. A five-second countdown leads into a five-minute match. Each elimination awards one point, deaths are tracked, and the highest score wins; equal leading scores produce a tie. Eliminated players respawn after three seconds at a spawn selected away from living opponents, with health and ammo restored and brief spawn protection. The server moves both the network root and the owning XR Origin. Play Again resets the match. Client loss removes that player; the match continues if enough players remain. Host loss ends the session.

## Locomotion and physical movement

The existing XR Interaction Toolkit teleport system remains the locomotion mechanism. ValidatedTeleportationArea rejects destinations outside the arena, overlapping obstacles or too close to living players. Teleport is restricted during the countdown and while eliminated. Tracking origin is configured for floor-relative play. HeadCollisionFade provides local push-back near virtual obstacles and a fade fallback when the player enters blocked geometry; blocked views prevent local firing. This controls virtual behavior, but cannot physically stop a person. Quest boundaries must remain enabled and each player needs a clear physical area.

## HUD and feedback

PlayerHUD builds the runtime world-space interface and reads synchronized health, ammunition, rank, score and time. Rank, Time and Score form a connected top group with FREE FOR ALL underneath. Health and Ammo occupy lower side panels. Independent Windows, Quest 2 and Quest 3 profiles set size and placement, with existing automatic device detection retained. Placement considers panel bounds, camera projection and physical Canvas units. ComfortHud compensates for parent scale and distance changes so moving the HUD near cover preserves its apparent size. Compact Rank and Score tabs retain balanced text padding.

Laser effects, sound, haptics, HUD messages, elimination visuals and respawn feedback communicate combat events. Local predicted effects improve responsiveness; health changes and scored hits depend on server confirmation. Haptic code uses OpenXR-compatible device capabilities.

## Build configuration and versions

| Component | Committed version |
| --- | --- |
| Unity Editor | 6000.6.2f1 |
| Netcode for GameObjects | 2.13.3 |
| Unity Transport | 6.6.0 in the committed package lockfile |
| XR Interaction Toolkit | 3.6.1 |
| OpenXR and XR Plug in Management | 1.19.0 and 4.6.1 |
| Input System and Universal Render Pipeline | 1.20.0 and 17.6.0 |

The Android build uses IL2CPP and ARM64. Meta Quest support and Oculus Touch, Touch Plus and Touch Pro controller profiles are enabled. Quest 2 and Quest 3 are included in device targets. The APK package is com.aub.cmps297ag.vrlasertag, version 0.1.0 with build code 1, minimum Android API 32 and target API 36. Its signature, archive integrity, ARM64 native libraries and Quest device declarations were verified. The APK is 43,969,875 bytes. Package files are the authoritative version record; the older CHANGES.md Transport 2.6.0 statement requires correction.

## Testing results and evidence

| Check | Result and evidence limit |
| --- | --- |
| Earlier automated desktop matches | Local summaries report PASS for 3 and 5 players, both with all players staying and with one leaving. Remaining clients had identical final states. These are historical desktop results. |
| Cover phase in earlier desktop tests | Host health remained 100 during the scripted cover phase. This does not exhaust every gun-through-wall angle or room-scale case. |
| Current Windows HUD | A Windows build and Editor client ran together. Live health, ammo, rank, score and timer matched synchronized values; ammo changed after firing. |
| HUD layout previews | Windows and simulated Quest 2/3 projection-bound checks passed, including left/right asymmetric projections and near-cover sizing. Previews are not headset captures. |
| Team reported headset tests | 13 and 14 November 2026: three-player mixed Quest 2/3 tests, with earlier collision, HUD and teleport issues reported resolved on 14 November. On 15 November, the team retested the latest APK with three players on Quest 2 and Quest 3 and reported that everything worked. |
| Fresh automated review attempt | Low-integrity Windows execution caused PlayerPrefsException before clients connected. This rerun was inconclusive and is not counted as a gameplay pass. |
| Five headsets and performance | No five-headset result or measured Quest 2 CPU/GPU frame-time result is recorded in this report. |

The team confirmed that the successful 15 November retest used the latest delivered APK, including the updated HUD. This is a team-reported hardware result, not an independently observed test. It closes the earlier final-APK testing gap for three-player mixed-device use. No per-feature logs, headset software versions or captures were supplied for this retest; general success does not establish measured performance, five-player behavior or an exhaustive regression.

## Limitations and remaining verification

The host is a single point of failure and no host migration is implemented. A leaving player loses their session score. Tracking positions are supplied by their owning clients; combat decisions are validated by the server. Hit detection uses the server latest poses without lag compensation, so latency can affect moving targets. Simultaneous requests are resolved in server arrival order. The final demonstration still needs five headsets including both Quest models, measured Quest 2 responsiveness, simultaneous-fire and disconnect checks on the presentation network, and repeated tests of cover, occupied teleport destinations and respawn.

The delivered APK was built from a checkout that also contained unrelated local material/rendering differences. Runtime/editor source matches the reviewed commit, but a clean-clone build has not been compared. These differences should be reconciled deliberately before asserting complete source-to-build reproducibility. No unrelated changes were pushed with the HUD commit.

## Setup and delivery

Open the VRLaserTag folder with the specified Unity version and load ArenaScene. For headset installation, extract the supplied ZIP, enable Developer Mode, connect a USB data cable, approve debugging and install the APK using Meta Developer Hub. Launch VR Laser Tag from Unknown Sources. Put players on a network that allows device-to-device traffic. One player selects HOST GAME; others join that host IP, and the host selects START MATCH. Submit source access, the APK, this technical report and the confirmed contribution statement. The private repository requires explicit reviewer access.

Source branch: https://github.com/lenamadi103/VR-Laser-Tag-CMPS297AG/tree/feature/headset-fixes
