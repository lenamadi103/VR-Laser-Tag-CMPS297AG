# Setup, installation and controls

## Install the supplied game

Unity is not required to run the supplied APK.

1. Extract the submission ZIP on a computer. The installable file is `VRLaserTag-Quest-HUD.apk`.
2. Enable Developer Mode for each Quest, connect a USB data cable and approve USB debugging inside the headset.
3. Install the APK through Meta Quest Developer Hub, or use `adb install -r VRLaserTag-Quest-HUD.apk` with Android platform tools.
4. Open the headset's Unknown Sources application list and launch **VR Laser Tag**.
5. Keep Quest boundaries enabled. Give every player a separate clear physical area.

If an older install has a different signing key, an update may be rejected. Back up any needed app data before deliberately removing an older install. The delivered APK is signed, ARM64 and declares Quest 2/3 device support.

## Host and join

1. Connect headsets to the same local network with device-to-device traffic permitted. Check campus/guest Wi-Fi isolation before the presentation.
2. One player selects **HOST GAME** and reads the displayed local IP address.
3. Other players enter that IP and select **JOIN**.
4. Once at least two players are connected, the host selects **START MATCH**.
5. After the countdown, play the timed free-for-all. The host can select **PLAY AGAIN** after the result.

The game uses LAN IP joining. Internet matchmaking and Relay setup are not required for this implementation. Host exit ends the session; there is no host migration.

## Controls

| Input | Action |
| --- | --- |
| Controller trigger | Fire or select a menu control |
| Right controller A | Reload; an empty magazine also reloads automatically |
| Thumbstick forward, either hand | Aim teleport; release to teleport to a valid destination |
| Grip while aiming teleport | Cancel teleport |
| Left menu button | Show/hide the in-game menu |

Bindings above follow the project implementation record. The latest mixed-headset session was reported successful, but a separate per-binding test record was not supplied. The desktop simulator displays its active keyboard bindings; F is the weapon reload binding.

## Open and build the source

Use branch `feature/headset-fixes`; reviewed source commit: `0104d280a7061ee4d521df5025a6f44631f2b3d7`.

1. Install Unity **6000.6.2f1** through Unity Hub with **Android Build Support**, SDK/NDK tools and OpenJDK. For the Windows test build, also install Windows build support.
2. Add the repository's **VRLaserTag** subfolder to Unity Hub. Do not open the repository root as the Unity project.
3. Let package resolution and asset import finish, then open `Assets/ArenaScene.unity`.
4. Use **VR Laser Tag > 3. Build Quest APK**. The project tool writes `VRLaserTag/Builds/Quest/VRLaserTag.apk`.
5. For desktop verification, use **VR Laser Tag > 4. Build Windows Test Version**; output is `Builds/Windows/VRLaserTag.exe` under the project.

The scene is already configured. Setup/configuration menu items modify project assets and are not necessary just to inspect the source. Build outputs belong outside committed source files.

| Package | Resolved version |
| --- | --- |
| Netcode for GameObjects | 2.13.3 |
| Unity Transport | 6.6.0 |
| XR Interaction Toolkit | 3.6.1 |
| OpenXR | 1.19.0 |
| XR Plug-in Management | 4.6.1 |
| Input System | 1.20.0 |
| Universal Render Pipeline | 17.6.0 |

`Packages/manifest.json`, `Packages/packages-lock.json` and `ProjectSettings/ProjectVersion.txt` are the authoritative version records. The Transport 2.6.0 figure in the historical CHANGES introduction is stale.

## Delivered artifact

- Filename: `VRLaserTag-Quest-HUD.apk`
- Application: `com.aub.cmps297ag.vrlasertag`, version 0.1.0 / build code 1.
- Size: 43,969,875 bytes.
- SHA-256: `72c6c41d48030c1920e917b7661d17e7ec46e8c95f9b8ded5d2e77250e470ebc`
- Built 15 November 2026; the team confirmed a successful same-day three-player Quest 2/3 retest of the latest APK.

The build checkout contained unrelated local rendering/material/project-setting differences. Runtime/editor source matches the reviewed commit, but a clean-clone build has not been compared. The commit and hash identify the reviewed source and delivered binary separately; they do not prove a byte-identical clean rebuild.
