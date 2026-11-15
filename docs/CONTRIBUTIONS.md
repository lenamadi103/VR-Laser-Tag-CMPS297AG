# Multiplayer VR Laser Tag Contribution Statement

CMPS297AG Introduction to XR | 15 November 2026

Team members: Mostafa Bdeir, Ali Jomaa, Rami Bawadi, Lena Madi, Alaa Mansour and Sally Sinno. The professor has approved this six-member team. The team representative confirmed the responsibility allocation below on 15 November 2026.

## Team work and individual attribution

We worked collaboratively on the existing Unity project. The allocation records each member responsibility for implementation, integration, review and explanation, including AI-assisted work. The file references and shared test evidence identify the resulting work; they do not assert exclusive authorship or individual Git commits.

| Member | Confirmed responsibility | Completed work and supporting evidence |
| --- | --- | --- |
| Mostafa Bdeir | HUD and VR interface | Worked on platform HUD profiles, connected Rank/Time/Score panels, Health/Ammo readability and VR interface review. Files: PlayerHUD.cs, ComfortHud.cs, HudArt.cs and VRMenuUI.cs. Evidence: Windows HUD captures, live-value checks and platform layout previews. |
| Ali Jomaa | Combat and weapons | Worked on server-side shooting and hit validation, cover blocking, health, ammunition, reload and elimination integration. Files: PlayerCombat.cs and LaserGunShooting.cs. Evidence: desktop combat/cover test logs and synchronized health/ammo checks. |
| Rami Bawadi | Movement collision and respawning | Worked on valid teleport destinations, block/wall obstruction, respawn placement and XR Origin/network-avatar alignment. Files: ValidatedTeleportationArea.cs, HeadCollisionFade.cs, SpawnManager.cs and NetworkVRPlayer.cs. Evidence: spawn checks and team-reported collision/teleport testing. |
| Lena Madi | Networking and avatar synchronization | Worked on host/client LAN connections, five-player approval, shared coordinates, tracked avatar poses and disconnect handling. Files: LanConnectionManager.cs and NetworkVRPlayer.cs. Evidence: earlier multi-client logs and the team-reported three-player mixed-headset sessions. |
| Alaa Mansour | Match rules and arena integration | Worked on countdown, timer, scores, winner/tie, restart and arena/spawn/avatar integration. Files: MatchManager.cs, PlayerInfo.cs, AvatarBodyVisual.cs and ArenaScene.unity. Evidence: five assigned spawns and desktop match-state, results and reset checks. |
| Sally Sinno | Quest deployment and verification | Worked on Quest configuration, package/version checks, APK delivery, verification records and submission documentation. Evidence: signed ARM64 APK with Quest 2/3 targets, installation instructions and compiled test records. Quest 2 performance measurement remains outstanding. |

## Starting material and project changes

We retained the existing arena, XR Origin, teleport setup, initial host-client networking, avatar tracking and spawn foundation described in CHANGES.md. Work on the final project extended combat, respawning, match management, VR menus, collision handling, avatar visuals and platform-specific HUD layouts. This statement distinguishes that foundation from the subsequent implementation and integration work.

## Sources assets and licenses

| Source or material | Use and attribution |
| --- | --- |
| Unity packages and samples | XR Interaction Toolkit, Netcode for GameObjects, Unity Transport, OpenXR, XR Plug-in Management, Input System and URP provide framework functionality. XRI Starter Assets and Device Simulator samples are included. Versions are recorded in Packages/manifest.json and packages-lock.json; the respective package/sample license terms apply. |
| Text and font resources | TextMesh Pro resources and the Liberation Sans font are included. The repository contains the Liberation Sans OFL notice. Existing third-party attribution and license notices must accompany redistribution. |
| Project visuals and audio | CHANGES.md documents generated low-poly models, textures, HUD graphics and synthesized sound effects. Their project use is separate from the Unity framework and sample resources. |
| Project and course records | The supplied project foundation, CHANGES.md, course assignment brief, committed source, build checks and test records informed implementation review and these documents. The source repository is linked below. |

## AI assistance and verification

OpenAI Codex/ChatGPT assistance was used for HUD layout/scaling implementation support, platform previews, build and verification support, requirement review and drafting the submission documents. The team report also records Claude (Anthropic) assistance reviewing the report against the assignment brief. GitHub Copilot was used to refine this final submission report and map it to the supplied brief. AI-assisted code and prose remain part of the team responsibility. Shared evidence includes runtime/source inspection, desktop multiplayer logs, live HUD-value checks, simulated layout bounds and APK signature/manifest verification. A fresh automated desktop rerun was blocked by Windows preference-access errors and is not counted as a pass.

## Headset testing and remaining checks

Our team reports three players using Quest 2 and Quest 3 together on 13 and 14 November 2026, with earlier collision, HUD and teleport issues resolved on 14 November. We retested the latest delivered APK, including the updated HUD, with three players on Quest 2 and Quest 3 on 15 November and reported that everything worked. This additional hardware result is team-reported; no per-feature logs or captures were supplied. Five simultaneous headsets and measured Quest 2 performance remain unverified. Each member is responsible for explaining their allocated system, its integration and supporting evidence.

Source branch: https://github.com/lenamadi103/VR-Laser-Tag-CMPS297AG/tree/feature/headset-fixes
