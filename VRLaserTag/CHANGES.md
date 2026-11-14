# VR Laser Tag – Changes

Unity **6000.6.2f1** · Netcode for GameObjects **2.13.3** · Unity Transport **2.6.0** · XR Interaction Toolkit **3.6.1** · OpenXR **1.19.0** (new) · XR Plug-in Management **4.6.1** (new) · Input System **1.20.0** · URP **17.6.0**

The existing foundation (arena, XR Origin, teleport, Host–Client networking, avatar sync, initial spawning) was kept and extended. No working system was replaced.

---

## 1. What was implemented

| Area | Result |
|---|---|
| Multiplayer combat | Owner sends a fire *request*; the server validates and raycasts; cover and walls block shots; no self-hits; hitboxes on body and head |
| Health / damage | 100 HP, 25 per hit, synced; damage, hit and elimination feedback (sound, vibration, HUD) |
| Ammo / reload | 10 shots, 2 s reload (A button / F key), automatic reload when empty; no firing while reloading, empty, eliminated or outside a match |
| Elimination / respawn | One elimination per death (guarded), 3 s respawn at the spawn furthest from enemies, both the NetworkPlayer **and** the local XR Origin are moved, 1.5 s spawn protection, no respawn after the match ends |
| Safe teleport | Floor only accepts destinations inside the arena, clear of cover and other players, not while eliminated and not during the countdown (the teleport line shows "blocked") |
| Match system | Waiting → Countdown (5 s) → Playing (5 min) → Finished; +1 point per elimination, deaths counted; winner or tie; host restarts with "Play Again" |
| VR UI | World-space lobby (IP keypad, Host/Join, players, Start/Play Again, Leave), head-locked HUD, two wall scoreboards, results screen |
| LAN | Host listens on all interfaces and shows its IP; manual IP join; max 5 players (6th is refused); clear disconnect messages |
| Meta Quest | OpenXR (Android only) with Meta Quest Support (Quest 2/3/3S/Pro targets) and Touch / Touch Plus controller profiles; IL2CPP, ARM64, Vulkan, API 32+, mobile URP tuned for VR |
| Visuals | Players get a colour (red/blue/green/yellow/purple), a visor showing head direction, a glowing gun tip; two cover blocks are now tall walls (full cover), the others stay crouch cover; spawns face the centre |
| Testing tools | Automated multi-instance match test (`-lasertag-autotest`) and a UI screenshot tour (`-lasertag-screenshots`), both inactive in normal play |

## 2. What files changed

**New scripts (`Assets/Scripts/`)**
- `PlayerCombat.cs` – server-authoritative shooting, health, ammo, reload, elimination, respawn timer, feedback RPCs
- `PlayerInfo.cs` – player number, colour, score, deaths
- `MatchManager.cs` – match states, timer, scoring, winner/tie, restart
- `LanConnectionManager.cs` – host/join over LAN, connection approval (max 5), status messages
- `ValidatedTeleportationArea.cs` – teleport destination rules (subclass of XRI `TeleportationArea`)
- `VRMenuUI.cs`, `PlayerHUD.cs`, `ScoreboardUI.cs` – VR UI
- `GameAudio.cs`, `XRHaptics.cs`, `GameLayers.cs`, `DisableWhenHeadsetActive.cs` – helpers
- `Testing/NetworkAutoTest.cs` – automated multiplayer test
- `Testing/ScreenshotTour.cs` – captures the lobby, HUD, scoreboard and results screens from a windowed build

**Modified scripts**
- `NetworkVRPlayer.cs` – spawn/respawn moves and turns the XR Origin to face the spawn direction; stale position updates after a respawn are ignored (spawn sequence number); pose RPCs are unreliable (loss-tolerant on Wi-Fi); large jumps sync as teleports (no sliding); the body capsule stretches with head height (accurate hitbox, crouching works)
- `SpawnManager.cs` – picks the free spawn furthest from living players; respawn and "everyone to separate spawns" at match start
- `LaserGunShooting.cs` – sends requests to `PlayerCombat`, keeps the instant local beam, reload input, ammo display on the gun, no firing while pointing at a menu; still works offline
- `NetworkTestUI.cs` – desktop test panel with IP field and Start Match (hidden on Quest)

**Editor / assets**
- `Assets/Editor/LaserTagSetup.cs` – menu **VR Laser Tag** (setup, Quest configuration, builds). Already applied to this project; safe to run again.
- `Assets/Editor/AndroidJavaSocketFix.cs` – fixes the Android build error *"Gradle build failed: Unable to establish loopback connection"* on Windows PCs whose TEMP folder cannot hold Java's local socket (found on the development PC). During Android builds (and when the Editor closes, when Unity stops Gradle) TEMP points to `Library/JavaTemp`; Editor-only, no effect on the game.
- `Assets/Audio/*.wav` – 11 synthesised sound effects (made for this project, no licence issues)
- `Assets/Materials/Cover.mat`, `AvatarVisor.mat`; `Assets/XR/…` (XR Plug-in Management + OpenXR settings)
- `Assets/ArenaScene.unity` – added MatchManager (NetworkObject), GameAudio, VR Menu, PlayerHUD (under Main Camera), 2 scoreboards, EventSystem with XR UI Input Module, ammo display on the gun; Floor uses `ValidatedTeleportationArea`; transport listens on `0.0.0.0`; connection approval on; XR Origin on layer `LocalRig`; rays ignore hitboxes; simulator removed on Quest; leftover falling test capsule removed
- `Assets/Prefabs/NetworkPlayer.prefab` – `PlayerCombat`, `PlayerInfo`, head sphere hitbox, `PlayerHitbox` layer, gun muzzle + remote beam, visor; NetworkTransform syncs position only
- `Packages/manifest.json` – added `com.unity.xr.management` 4.6.1 and `com.unity.xr.openxr` 1.19.0
- `ProjectSettings` – layers `PlayerHitbox` (6) and `LocalRig` (7), physics matrix (hitboxes never collide), Android player settings
- `Assets/Settings/Mobile_RPAsset.asset` – MSAA 4x, HDR off, render scale 1.0, shadow distance 15 m

Nothing was renamed or deleted except the stray `Capsule` test object in the scene.

## 3. How the systems work

**Authority model.** Host–Client. The server (host) decides everything that affects the game: hits, damage, health, ammo, reload, elimination, respawn, scores, timer, winner. Each client is trusted only for its own head and controller tracking (VR tracking cannot come from anywhere else). The server checks that a shot's origin is near where it sees the shooter; if not, it uses its own copy of the gun position.

**A shot, step by step.**
1. Trigger pressed → `LaserGunShooting` checks the synced state (alive, ammo, not reloading, match running) and draws the beam, plays the sound and vibrates immediately.
2. `PlayerCombat.FireRpc` reaches the server, which repeats all checks plus fire rate, spends 1 ammo and raycasts. Walls/cover stop the ray; the shooter's own hitboxes and eliminated players are skipped.
3. On a hit: health −25 (unless spawn-protected). At 0 HP the player is eliminated once, `MatchManager` adds the point and death.
4. `ShotFiredRpc` tells everyone: other players see the beam from the shooter's gun; the shooter gets a hit confirmation.

**Simultaneous fire.** The server handles shot requests one by one in arrival order. If two players would eliminate each other in the same moment, the first request eliminates the other player, whose shot is then refused (eliminated players cannot fire). No double damage, no double points.

**Disconnects.** A client leaving: its avatar despawns, it disappears from the scoreboard (its points leave with it), the match continues if at least `minPlayers` remain, otherwise it ends. The host leaving ends the session; clients see "Disconnected: the host closed the game." and the lobby again.

**Respawn.** `PlayerCombat` (server) waits 3 s → `SpawnManager` chooses a spawn → `NetworkVRPlayer.ServerMoveToSpawn` teleports the networked root and sends `InitializeSpawnRpc` to the owner, which moves and turns its XR Origin → health/ammo reset with 1.5 s protection.

**Match rules.** Anyone can join until 5 players (late joiners play with 0 points). The host presses **START MATCH** when at least `minPlayers` (default 2) are connected → 5 s countdown (weapons locked) → 5 min match → results (highest score wins; equal top scores tie) → host presses **PLAY AGAIN**. Combat only works while Playing.

**UI.** All UI only reads synced state. The lobby panel appears in front of the player (and re-centres when the player is moved to a spawn), hides when the countdown starts, and comes back for the results with a "YOU WIN / TIE" headline. While it is open the HUD hides its big messages so the two never overlap. Controls: **trigger** fire / click UI, **A** reload, **left menu button** show/hide menu, **thumbstick forward** (either hand) aim a teleport, release to go, **grip** while aiming cancels (see section 8).

## 4. What was tested

All of the following were actually run on the development PC (Windows 11):

| Check | Result |
|---|---|
| Package resolution + script compilation (Unity 6000.6.2f1, batch mode) | ✅ 0 errors |
| Setup script applied to scene/prefab, re-run without duplicates | ✅ |
| Windows build | ✅ succeeded |
| **Automated 3-player match** + one player leaves + a new player joins mid-match | ✅ PASS on all 4 instances |
| **Automated 5-player match** + one player leaves + a new player joins mid-match | ✅ PASS on all 6 instances |
| 6th player connecting to a full game | ✅ refused: "The game is full (5 players)" |
| Host closes the session | ✅ client detects it, returns to the lobby: "Disconnected: the host closed the game." |
| Same state on every client (scores, deaths, health, eliminated, winner) | ✅ identical "FINAL STATE" line on all instances (3- and 5-player runs; one run ended in a tie, reported as a tie everywhere) |
| UI screenshot tour (windowed build): lobby, session, countdown, HUD, damage, scoreboard, eliminated, results | ✅ all screens rendered and readable; found and fixed 5 UI issues (see below) |
| Meta Quest APK build (Android, IL2CPP, ARM64) | ✅ succeeded – `Builds/Quest/VRLaserTag.apk` (42 MB) |
| APK manifest | ✅ Quest VR intent, head tracking, INTERNET permission, supported devices `quest, quest2, cambria (Pro), eureka (Quest 3), quest3s`, minSdk 32 |

What the automated match checks (on the host, against the authoritative state): unique player numbers and separate spawns; countdown → playing; **cover test** (the host hides behind the centre cover while player 2 fires at it for 6 s → host stayed at 100 HP); client→host, host→client and client→client damage; health/ammo always in range; nobody at 0 HP without being eliminated; points never exceed eliminations; at least one elimination, respawn (with full health) and reload; **every respawn/join lands on a spawn point** (23 checks in the 5-player run – this also proves the owner's XR Origin moved, because the server ignores positions sent before the owner applied the spawn); a simultaneous-fire moment; one client disconnecting at 25 s (match continues, player removed); a new client joining mid-match (gets the free player number, spawns correctly); timer ends the match; correct winner/tie; **no respawn after the match ended**; **restart** resets scores, health, ammo and elimination – checked on the host *and* on every client.

Example 5-player result (identical on all 5 instances): Player 3 – 7 points, Player 1 – 6, Player 4 – 4, Players 2 and 5 – 2; winner Player 3.

UI issues found by the screenshots and fixed: the menu stayed behind when the player was moved to a spawn; the HUD overlapped the open menu; the menu covered the countdown; the scoreboard "DEATHS" header wrapped; the damage flash was a rectangle instead of a full-view tint.

The only exception in the headless logs comes from the XRI sample's *XR Device Simulator UI*, which has no keyboard/mouse in headless mode. It does not happen in a normal window (confirmed by the screenshot run) or on Quest.

**Not tested (no headset available to me):** anything on a real Quest, Quest 2 + Quest 3 in one session, real Wi-Fi, controller input/haptics, pressing the VR menu buttons with a real controller ray, teleporting with a real controller, and Quest 2 frame rate.

## 5. What remains / known limitations

- **Physical testing is still required** (see section 6). Quest compatibility is configured, not yet verified on a device.
- Hit detection uses the server's latest positions (no lag compensation). On a LAN this is ~50–100 ms; a target that moves fast may be missed by a shot that looked like a hit. Players mostly stand or teleport, so this is small in practice.
- Movement is trusted from the owning client (needed for VR tracking); only combat/scoring is cheat-resistant.
- The host is also the server: if the host quits, the match ends for everyone.
- A player who leaves takes their score with them; rejoining starts from 0.
- UI uses Unity's built-in `Text` (not TextMeshPro) to avoid extra imported assets; fine for readability, slightly softer than TMP.

## 6. What you need to do

**Nothing has to be wired by hand** – the scene and prefab are already set up. If you ever replace the scene or prefab, run **VR Laser Tag → 1. Setup Scene and Prefab** again.

The active build platform is now **Android** (needed for the APK). Editor Play mode still uses the XR Device Simulator. To make a Windows test build, use the menu item below (Unity switches platform automatically).

**Test in the Editor**
1. Open `ArenaScene`, press Play. Use the desktop panel (top-left) → *Start Host*. The XR Device Simulator still works.
2. For a second player run `Builds/Windows/VRLaserTag.exe` (or build it with **VR Laser Tag → 4. Build Windows Test Version**), type `127.0.0.1`, *Start Client*. On the host press *Start Match*.
3. For quick solo tests set **MatchManager → Min Players = 1** in the scene.

**Build and install on Quest**
1. Enable Developer Mode on each headset (Meta Horizon app) and connect it by USB.
2. **VR Laser Tag → 3. Build Quest APK** (or File → Build Profiles → Android) → `Builds/Quest/VRLaserTag.apk`.
3. Install: `adb install -r Builds/Quest/VRLaserTag.apk` (or drag the APK into Meta Quest Developer Hub). It appears under *Library → Unknown Sources*.

**Play on Quest**
1. Put all headsets on the **same Wi-Fi**. University networks often block device-to-device traffic – a dedicated router or phone hotspot is the safest choice.
2. One player presses **HOST GAME** and reads the IP shown on the panel.
3. Others type that IP on the keypad and press **JOIN**.
4. The host presses **START MATCH**.

**Physical test checklist (for the report):** two headsets first, then five; mix Quest 2 and Quest 3; check frame rate on Quest 2 (OVR Metrics Tool); shoot across cover; fire at the same moment; take a headset off Wi-Fi mid-match; quit the host.

**Automated test (optional, Windows):**
```
VRLaserTag.exe -batchmode -nographics -lasertag-host -lasertag-autotest -lasertag-autotest-players 3 -logFile host.log
VRLaserTag.exe -batchmode -nographics -lasertag-client 127.0.0.1 -lasertag-autotest -logFile a.log
VRLaserTag.exe -batchmode -nographics -lasertag-client 127.0.0.1 -lasertag-autotest -lasertag-autotest-leave 25 -logFile b.log
```
Each log ends with `AUTOTEST RESULT: PASS` or `FAIL (...)`. Extra options: `-lasertag-autotest-players 5`, `-lasertag-autotest-final N`, `-lasertag-autotest-hostquit S` (host) and `-lasertag-autotest-expect-hostloss` (client) – see the top of `NetworkAutoTest.cs`.

**UI screenshots (optional, Windows):**
```
VRLaserTag.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -lasertag-screenshots C:\shots
```
The latest screenshots are in `Builds/Screenshots/` (useful for the report).

---

## 7. Visual redesign (branch `feature/visual-redesign`)

**Goal:** make the game look like a polished futuristic free-for-all arena **without** changing gameplay and **without** lowering performance. The game was already free-for-all (individual scores, five player colours, no teams), so no gameplay logic changed. Networking, shooting, hit detection, damage, scoring, respawn, teleport and spawn points are untouched; arena objects (floor, walls, cover, spawn points) keep their positions, sizes and colliders.

### What changed

| Phase | Result |
|---|---|
| A. Lighting | Real-time sun and its shadows replaced by **baked** lighting: lightmaps, 9 baked lamps/corner accents (blue and orange on diagonals, so no "team side"), a light-probe grid for players and the gun, one baked reflection probe. Dark solid background instead of the sky. Quest renderer: real-time shadows, mixed lighting, probe blending and light layers off. |
| B. Arena look | Metal floor plates, panelled walls with vents, crate-style cover (small generated textures), ceiling with beams and light fixtures, upper wall band, neon trims on walls and cover, hazard bands, glowing centre core, numbered spawn pads 01–05, "VR LASER TAG / FREE FOR ALL ARENA" signs. Decoration has **no colliders**. |
| C. Hands and gun | Low-poly gloves on both controllers (right hand grips the gun), new low-poly laser pistol with glowing accents, ammo screen on the back of the gun. `LaserFX` (visual only): muzzle flash, impact spark, beam tinted with the shooter's player colour. Other players get player-coloured gloves and the same gun. The shot origin (`MuzzlePoint`) and the server's fallback muzzle are unchanged. |
| D. HUD and leaderboard | Head-locked HUD with small edge panels (FREE FOR ALL + timer, name/rank/score, health, ammo); red edge vignette when hit instead of a full-screen tint; wall leaderboards RANK / PLAYER / SCORE with the leader in gold and "(you)" on your row; `PlayerNameLabel` name tags above other players; menu in the arena colours. |

### How to re-apply

Menu **VR Laser Tag → 5. Visual Redesign**, in order: **A. Lighting (bake)**, **B. Arena Look (+ bake)**, **C. Hands and Gun**, **D. HUD, Leaderboard and Name Labels**. Every step is safe to run again. Running **1. Setup Scene and Prefab** afterwards keeps the redesign (it no longer resets cover materials, moves the server muzzle or drops the right glove's colour).

Command line (Unity closed): `Unity.exe -batchmode -projectPath VRLaserTag -executeMethod LaserTagVisuals.BatchApplyLighting` (likewise `BatchApplyArenaLook`, `BatchApplyHandsAndGun`, `BatchApplyHudAndLabels`). Do not use `-nographics` for the bake steps.

### Files

- New editor tools: `Editor/LaserTagVisuals.cs` (all phases), `Editor/LaserTagPreview.cs` (renders hand/gun/avatar previews: `-executeMethod LaserTagPreview.BatchRender -previewOut <folder>`; scene cost summary: `LaserTagPreview.BatchStats`), `Editor/StripSimulatorFromQuestBuild.cs`.
- New runtime scripts (visual only): `Scripts/LaserFX.cs`, `Scripts/PlayerNameLabel.cs`.
- Changed for formatting only: `Scripts/PlayerHUD.cs`, `Scripts/ScoreboardUI.cs`. Layout changes in `Editor/LaserTagSetup.cs` (HUD, leaderboards, menu colours).
- New assets: `Materials/Arena/`, `Textures/Arena/`, `Textures/HUD/`, `Meshes/`, `Settings/ArenaLighting.lighting`, `ArenaScene/` (lightmaps, reflection probe).

### Performance

Measured on **Quest 3**, one player in the lobby, about 40–100 s each, from the headset's own `VrApi` log (via `adb logcat`):

| Build | Frame rate | GPU time per frame | GPU load |
|---|---|---|---|
| Before the redesign | 72 (steady) | 2.68 ms | 30% |
| After A (baked lighting) | 72 | 2.11 ms | 23% |
| After B (arena look) | 72 | 2.45 ms | 28% |
| After C (hands and gun) | 72 | 2.37 ms | 26% |
| After D (HUD) – final | 72 | **2.12 ms** | **23%** |

Scene cost (editor count, `LaserTagPreview.BatchStats`): before – 15 renderers, about 2,600 triangles, 1 real-time light with soft shadows (most objects drawn twice). After – 116 renderers (113 static-batched), about 3,200 triangles, about 21 mesh draws after batching, 14 materials, no transparent materials in the arena, 4 small textures (≤ 512 px), 2 small lightmaps, **no real-time lights**.

### Verified / not verified

- ✅ Compiles; Windows and Quest builds succeed; automated 3-player and 5-player matches pass (identical final state on every client); UI screenshots and hand/gun/avatar previews reviewed; each phase viewed on a Quest 3.
- ❌ Not yet verified: **Quest 2 frame rate**, a real multiplayer match on headsets (name tags, in-match HUD, coloured beams and muzzle flash have only been seen in Editor previews and Windows screenshots), mixed Quest 2 + Quest 3 session.

### Quest fixes found during the first headset test (on `feature/complete-gameplay`)

- **Controllers and gun not showing:** the XR Device Simulator started on the headset before it was removed and deleted the real headset from the Input System. It now turns off before any other script runs (`DisableWhenHeadsetActive`), and Quest builds strip it from the scene (`StripSimulatorFromQuestBuild`).
- **Menu buttons could not be clicked:** the right controller ray had no "UI Press" input. It is now wired, and the setup script assigns it automatically.

---

## 8. Headset fixes (branch `feature/headset-fixes`)

Four problems reported from real Quest testing. Gameplay rules, networking, shooting, scoring and spawns are unchanged; the game stays free-for-all and teleport-only (course spec).

### Root causes

| Issue | Root cause found in the project |
|---|---|
| HUD too close, follows the head rigidly | `PlayerHUD` was a child of the headset camera, 0.6 m in front of the eyes: fully head-locked, and closer than is comfortable to focus on. |
| Walking through walls and blocks | Every wall and cover block already had a solid BoxCollider. Movement is teleport-only (and teleport destinations were already validated), so the way through was **physical room-scale walking**, which no collider can stop (the headset cannot be pushed back). Nothing reacted when the head entered geometry. |
| Controller movement unreliable / unexpected teleports | Three configuration faults in the XR rig: (1) the right **UI ray** was a projectile arc that could select the teleport floor, so **squeezing and releasing the grip on the gun hand teleported the player** wherever the arc pointed; (2) the right `ControllerInputActionManager` used the Right Hand itself as its "ray interactor": pushing the **thumbstick forward** (the standard Quest teleport gesture) **deactivated the whole Right Hand** (gun, ray, menu clicks), and because the manager sits on that same object it never re-activated it; (3) the Right Hand's existing Teleport Interactor was bound to the grip and offset **4.6 m forward and 2 m sideways** from the hand; the left ray had no select input at all. In addition the XR Origin requested no tracking origin, so the platform default was used. |
| Player models look poor | Avatars were a Unity capsule (body), a sphere (head) and a cube visor. |

### What changed

- **HUD (`ComfortHud`)**: the HUD now lives under the XR Origin's Camera Offset (it moves with teleports and spawns instantly) and floats **1.3 m** away. It follows the view lazily: it only starts turning after the view is **18° (horizontal) / 14° (vertical)** away, then eases back to the centre (0.22 s). Small head movements leave it still. It stays upright, 3° below the line of sight. In front of a wall or cover it comes closer (minimum 0.45 m) and shrinks proportionally, so it is never hidden inside geometry and keeps the same angular size (text keeps the size of the old HUD, about 42 × 26° in total). The damage vignette moved to a small head-locked overlay (a full-view effect). No per-frame allocations; one sphere cast per frame.
- **Walls (`HeadCollisionFade`)**: when the head comes within 12 cm of a wall/cover (or leaves the arena after spawning), the view fades to black and "STEP BACK INTO THE ARENA" is shown. The camera is never moved against the player's real motion (that causes motion sickness). While the view is blacked out the gun does not fire, so walking into cover gives no advantage. Teleporting out still works.
- **Locomotion (existing XRI teleport system repaired, nothing new added)**: the floor accepts only the **Teleport** interaction layer; each hand has the XRI Starter Assets Teleport Interactor, activated by **pushing the thumbstick forward** and teleporting on **release** (grip while aiming cancels). The controller managers no longer deactivate the hands. The hand rays are now straight menu pointers that only draw their line over menus. The XR Origin requests a **Floor** tracking origin (real player height; standard for standing room-scale). Destination rules (`ValidatedTeleportationArea`) are unchanged. The lobby help text lists the teleport control.
- **Avatars**: low-poly armoured suit (helmet with visor, torso with shoulder pads and power pack, legs) made of bevelled boxes in **one material** with an 8-pixel colour palette. Each player's own colour glows on the helmet crown, visor rim, chest core, shoulders, belt and legs (emission only; the suit itself is the same neutral white/grey for everyone, so nothing implies teams). `AvatarBodyVisual` places the torso under the synchronised head (it turns towards the view only after 40°) and stretches the legs to the floor (crouching works). No IK, no animation. Hitboxes (body capsule, head sphere) are unchanged, only their placeholder meshes are no longer drawn. The suit is hidden for its owner and while eliminated, like the rest of the avatar.

### Files

- New runtime scripts: `Scripts/ComfortHud.cs`, `Scripts/HeadCollisionFade.cs`, `Scripts/AvatarBodyVisual.cs`.
- Changed: `Scripts/PlayerInfo.cs` (accent colour on suit emission), `Scripts/LaserGunShooting.cs` (no firing while the view is blacked out), `Scripts/PlayerHUD.cs` (comment), `Editor/LaserTagSetup.cs` (HUD placement, overlay, lobby help text), `Editor/LaserTagPreview.cs` (avatar lineup and HUD previews, avatar cost).
- New editor tool: `Editor/LaserTagHeadsetFixes.cs` – menu **VR Laser Tag → 6. Headset Fixes → Apply All** (or `-executeMethod LaserTagHeadsetFixes.BatchApplyAll`). Safe to run again.
- Scene/prefab: `ArenaScene.unity` (HUD, overlay, teleport interactors, controller managers, rays, floor layer, tracking origin), `Prefabs/NetworkPlayer.prefab` (suit parts, `AvatarBodyVisual`, renderer lists).
- New assets: `Meshes/Avatar_Helmet|Torso|Legs.asset`, `Materials/Avatar/Avatar_Suit.mat`, `Textures/Avatar/` (two 8 × 1 px palettes).

### Performance (measured in the Editor; not yet on a headset)

| | Before | After |
|---|---|---|
| Arena scene | 116 renderers, ~21 mesh draws, 0 real-time lights | unchanged |
| One remote avatar – draws | 8 | **8** (helmet, torso and legs replace capsule, sphere and visor) |
| One remote avatar – triangles | 2,054 | 2,680 (+626; about +2,500 with four opponents) |
| Suit materials | per-part colour materials | 1 shared material (Simple Lit, no transparency, no shadows) |
| HUD | 1 canvas | 1 canvas + overlay (drawn only during damage flash / wall fade) |
| CPU per frame (new) | – | 1 sphere cast (HUD), 1 overlap sphere (wall fade), a few transform updates per visible avatar; no allocations |

### Verified / not verified

- ✅ Compiles; setup runs; Windows build succeeds; automated **3-player and 5-player matches PASS** on host and all clients (no disconnects). The variant where one client leaves mid-match reports "More points than eliminations" on the host: the check sums deaths of *connected* players only, so it fails whenever the leaving player had been hit before leaving (a limitation of the test, not of the game; no scoring code changed).
- ✅ Previews reviewed: avatar lineup in all five colours (different heights, crouching), HUD in the open and in front of cover (`Builds/Previews/c4`–`c8`).
- ❌ Not verified on a headset yet: HUD comfort while moving, thumbstick teleport on both hands, the wall fade while walking, Floor tracking origin on Quest, avatar look and tracking in a real match, Quest 2 / Quest 3 frame time.

### Fix: shots through thin walls

Found in a code review: the server's shot raycast starts at the gun tip, and the tip was only checked to be within 1.5 m of the head. A player standing behind one of the 0.5 m tall walls (`Cover2`, `Cover5`) could physically reach the gun through it (the head stays outside, so the wall fade does not trigger) or push the tip into a block, and the shot started behind the obstacle.

- `PlayerCombat`: new `ShotRejectReason.MuzzleBlocked`. The server refuses a shot when the gun tip is inside a wall/cover block or a wall/cover block lies between the shooter's head and the gun tip (`IsMuzzleBlocked`: one small overlap test and two linecasts, no allocations). The shooter sends its real head position with the request; the server replaces it with its own copy if it is more than 1.5 m away (same rule as the shot origin). No ammo is spent on a refused shot.
- `LaserGunShooting`: the same check runs before firing, so the shooter gets instant feedback (no predicted beam, 'denied' sound and haptic, HUD message "Gun blocked by a wall - pull it back").
- `NetworkAutoTest`: bots send their networked head as the eye position (their gun hangs 0.2 m below it).
- Holding the gun just over the near edge of crouch cover still works; reaching deep over or through a block does not.
- ❌ Not yet verified in Unity: compile, automated 3/5-player matches, and the behaviour on a headset.
