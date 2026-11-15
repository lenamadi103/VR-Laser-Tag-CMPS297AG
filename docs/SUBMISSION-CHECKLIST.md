# Requirements and submission checklist

Status as of 15 November 2026. The course brief is authoritative; grading weights and logistics are announced separately.

## Required behavior

| Requirement | Current coverage |
| --- | --- |
| Fully virtual shared arena, cover, predefined spawns and visible avatars | Described in source/report |
| Independent physical areas and consistent virtual coordinates | Architecture documented; add measured coordinate example to demonstration |
| Tracked looking/aiming and valid, unoccupied teleport destinations | Implemented; mixed-headset teleport testing reported successful |
| Controller weapon with ammo/energy, cover blocks shots | Ammo and authoritative ray/obstruction checks documented; retain explicit cover tests |
| Synced shots, hits, health, ammo and scores; clear feedback | Architecture and historical tests documented; latest general hardware success reported |
| Join/start, scoring, win/tie, restart, death/respawn | Implemented and described; preserve scenario evidence |
| Chosen authority explained | Host authority, client tracking and limits described |
| Quest 2 and Quest 3 together | Three-player latest-APK session reported successful 15 November |
| Five simultaneous headset players | Outstanding; five desktop processes do not satisfy this |
| Responsive behavior on Quest 2 | General success reported; quantitative performance record outstanding |
| Boundaries enabled; separate clear physical areas | Safety instructions included; enforce during live demonstration |
| Source plus installable Quest build and exact versions | Prepared as separate source access and APK delivery; confirm reviewer access |
| Technical report, contributions, sources/licenses and AI disclosure | Included; foundation/source provenance still needs team completion |
| Simultaneous firing and disconnect explained in demo | Behavior described; rehearse explicit final-build scenarios |
| Every member explains architecture and performs a live modification/debug task | [Rehearsal plan](DEMO.md) prepared; successful execution not yet recorded |

## Before sending

- [ ] Complete the five-headset mixed-model demonstration/test and record results.
- [ ] Record Quest 2 responsiveness and explicit edge-case tests; do not invent measurements.
- [ ] Rehearse all six members' explanations and small live tasks.
- [ ] Identify the original foundation's creator/source/baseline and any additional tutorials/assets or AI use absent from the credits.
- [ ] Reconcile unrelated local rendering/project-setting differences deliberately and compare a clean-source build. Do not bulk-commit local changes.
- [ ] Publish the reviewed documentation only to `feature/headset-fixes`; keep `main` and `dev` untouched unless separately requested.
- [ ] Verify the professor can open the private repository branch and download the installable APK.
- [ ] Provide the combined report, APK, checksum and installation instructions together. A documentation ZIP alone is not the Unity source project.
- [ ] Confirm the professor's submission destination/deadline and use email subject `CMPS297AG_final_project`.

## Repository publication scope

The submission documentation consists of the root README, this docs folder and a dated status note in `VRLaserTag/CHANGES.md`. Its source review is based on commit `0104d280a7061ee4d521df5025a6f44631f2b3d7`; the documentation commit does not change Unity code, scenes, prefabs, materials or project settings.

The historical `VRLaserTag/CHANGES.md` retains earlier implementation/test observations. Its new dated note distinguishes those records from the team's successful latest-APK mixed Quest 2/3 retest on 15 November. The README, setup guide and testing record describe current validation scope and the resolved package versions.
