# Final Project Submission and Assessment Report

**Course:** CMPS297AG - Introduction to XR  
**Project:** Multiplayer VR Laser Tag  
**Team:** Mostafa Bdeir, Ali Jomaa, Rami Bawadi, Lena Madi, Alaa Mansour and Sally Sinno  
**Report date:** 15 November 2026  
**Submission branch:** `feature/headset-fixes`  
**Reviewed source revision:** `0104d280a7061ee4d521df5025a6f44631f2b3d7`

## Executive summary

The project is a fully virtual, networked laser-tag game for Meta Quest 2 and Quest 3. Players use separate physical play areas while sharing a virtual arena, tracked avatars, teleport movement, cover, combat, and a timed free-for-all match. The host is authoritative for gameplay decisions and synchronized match state.

The team reports that the delivered APK worked in a three-player mixed Quest 2/Quest 3 session on 15 November, including the updated HUD. This is a team-reported result, not an independently witnessed test. It does **not** satisfy the assignment's five-simultaneous-headset demonstration requirement. Historical desktop tests, simulated headset layout checks, and static APK checks provide useful supporting evidence but are not substitutes for that demonstration.

**A final numeric mark cannot be calculated from the supplied brief.** It says assessment weights and logistics will be announced separately, and the final demonstration and individual explanations are part of assessment. This report therefore provides an evidence-based readiness assessment rather than predicting or claiming a grade.

## Requirements and evidence assessment

| Assignment criterion | Implementation and evidence available | Status for assessment |
|---|---|---|
| Shared virtual arena, fixed spawns, cover and visible avatars | The scene and architecture describe one shared virtual coordinate system, five predefined spawns, cover and networked head/hand/body representations. See [architecture](ARCHITECTURE.md) and [technical report](TECHNICAL-REPORT.md). | Implemented and documented. Include a live demonstration. |
| Independent physical areas and consistent virtual coordinates | Each headset tracks its local player while the networked avatar is represented in shared virtual coordinates; physical colocation and shared anchors are not required. Boundary and clear-area safety instructions are documented. | Design documented. The assignment's illustrative relative-distance scenario has not been recorded as a measured demonstration. |
| Tracked aiming and valid, unoccupied teleport destinations | Teleport destination checks and local tracking are documented. Earlier Quest 2/3 testing reportedly resolved collision and teleport issues; the latest three-player APK retest was generally reported successful. | Implemented; final-build, per-case test evidence is limited. Demonstrate valid, blocked and occupied destinations. |
| Controller weapon, ammunition/energy and usable cover | The report describes a 10-shot magazine, reload behavior, server-side shot validation and obstruction checks. Historical desktop test summaries include a cover phase; they do not prove every angle or final-build headset scenario. | Implemented and partly tested. Demonstrate cover blocking, ammo/reload and accepted hits on the submitted build. |
| Synchronized shots, hits, health, ammunition and scores with feedback | Host-authoritative combat and synchronized state are documented. Desktop live-value checks compared health, ammo, rank, score and timer; the team reports general success on the latest mixed-headset APK. | Architecture documented and supporting checks exist. Capture or demonstrate consistent values across clients. |
| Joining, starting, scoring, winning/tie, restarting, death and respawn | Match rules and state flow are described in the technical report. Historical desktop scenarios covered multi-process matches; latest headset confirmation did not include per-feature logs. | Implemented and partly tested. Show the complete lifecycle in the final demonstration. |
| Consistent authority model | The host validates gameplay actions and owns combat, damage, ammunition, respawn, scoring and match state; clients provide local tracking and request actions. Host loss ends the session; host migration is not implemented. | Explained in the [technical report](TECHNICAL-REPORT.md); team members must be able to explain it live. |
| Five simultaneous headsets with both Quest 2 and Quest 3 | The latest APK was reportedly tested with three simultaneous Quest 2/Quest 3 players. Historical five-process tests were desktop tests, not five headsets. | **Not yet demonstrated; highest-priority grading gap.** |
| Responsive rendering and interaction on Quest 2 | General Quest 2/3 success is team-reported. No measured Quest 2 frame times or dropped-frame record is available; the brief specifies responsiveness but no numeric FPS threshold. | Qualitative evidence only; record responsiveness during representative multiplayer load. |
| Simultaneous firing and player disconnect | Behavior is described in the architecture/technical report, including server-arrival ordering and host-loss behavior. No explicit final-APK demonstration record is supplied. | **Demonstration and evidence outstanding.** Explain and test client disconnect and host loss separately. |
| Every member explains, modifies or debugs their contribution | The contribution statement assigns confirmed responsibilities, and [the rehearsal plan](DEMO.md) proposes a task for each member. Rehearsal completion is not recorded. | **Live individual demonstration outstanding.** Each member should rehearse a small, isolated modification/debug task. |
| Source, installable build, exact versions and setup/join instructions | Source branch and Unity/package versions, APK identity, installation and LAN join steps are documented in [setup](SETUP.md). The APK is distributed separately from source. | Ready when repository reviewer access and APK delivery are confirmed. |
| Technical report, contributions, sources/licenses, AI use and verification | Covered in [technical report](TECHNICAL-REPORT.md), [contributions](CONTRIBUTIONS.md), [credits](CREDITS.md), and [testing evidence](TESTING.md). | Documentation prepared. Original foundation creator/source and any unlisted assets/tutorials/AI use still need confirmation. |

## Test evidence and limitations

- **Earlier team-reported two-headset test:** The team says both players joined one environment and exercised shooting, movement/cover, score display, respawn and reload. The team also reports finding wall/obstacle clipping and a health/ammo HUD that was too close to the face, then correcting these issues. The date, device models, APK, network, logs and captures were not supplied, so this is historical team testimony—not a verified final-build pass. Its "game ID" and Developer Mode-disabled details conflict with the current documented host-IP and sideload setup and need confirmation.
- **Latest headset build:** The team reports a successful three-player Quest 2/Quest 3 session on 15 November 2026 using `VRLaserTag-Quest-HUD.apk`. No per-feature logs, headset software versions, or physical-headset captures were supplied.
- **Earlier headset sessions:** Three-player mixed-device tests were reported on 13-14 November, with earlier collision, HUD and teleport issues reported resolved on 14 November.
- **Desktop multiplayer:** Historical three- and five-process scenarios are recorded. These establish neither five-headset support nor five-player physical demonstration.
- **HUD and packaging:** Windows live-value checks, simulated Quest projection/layout checks, and static APK signature/manifest/archive checks are recorded. Simulated projections are not headset captures.
- **Inconclusive rerun:** A fresh automated desktop run stopped on a Windows `PlayerPrefs` access error before clients connected. It is not counted as a gameplay pass.
- **Source/build reproducibility:** The delivered APK identity and SHA-256 are recorded in [setup](SETUP.md). The build checkout had unrelated local rendering/material/project-setting differences, and a clean-clone rebuild has not been compared. The checksum identifies the delivered artifact; it does not prove a byte-identical rebuild from the reviewed commit.

Do not present any of these evidence types as stronger than they are. In particular, do not describe the three-headset test as a five-headset pass, simulated projections as physical headset captures, or a team report as an independently witnessed test.

The earlier test account says reload was triggered by holding a designated controller button. The current setup guide specifies Right-controller A and a two-second reload. Verify the actual delivered-build binding during the demonstration; use the documented build behavior, not an unverified recollection.

## Highest-priority actions before assessment

1. Run and record the required five-headset session, with both Quest 2 and Quest 3 represented. Record exact APK, headset counts/models, date, and result.
2. During that session, demonstrate all players joining, spawning, seeing one another and teleporting in the same arena. Keep boundaries enabled and physical areas separate.
3. Demonstrate cover blocking shots; firing, ammunition/reload, hit/damage feedback, death/respawn and consistent scores across clients.
4. Test simultaneous firing and a client disconnect; explain the observed behavior. Separately explain the host-loss limitation.
5. Check Quest 2 responsiveness during representative multiplayer load. Record observations or available performance measurements without inventing a threshold.
6. Have all six members rehearse an architecture/contribution explanation and a small live modification or debugging task, using a disposable copy.
7. Confirm the original foundation's creator/source/baseline and reconcile any additional assets, tutorials or AI assistance that need attribution.
8. Confirm the professor can access the private repository and retrieve the APK. If time permits, compare a clean-source build before making reproducibility claims.

## Submission packet

The assignment asks for the following. Submit them together or clearly cross-reference them:

1. **Unity source and playable build:** provide reviewer access to the repository's `feature/headset-fixes` branch and attach or link the installable Quest APK. Include the exact Unity/package versions, APK checksum, and [installation/join instructions](SETUP.md).
2. **Technical report:** submit [the technical report](TECHNICAL-REPORT.md), covering architecture, authority and synchronization, test results, and known limitations.
3. **Contribution statement:** submit [the contribution statement](CONTRIBUTIONS.md), including each member's work, sources/licenses, AI assistance and verification.
4. **Supporting evidence:** include [testing and evidence](TESTING.md) and the existing referenced evidence files. Clearly label simulated projections, desktop results and team-reported headset tests.
5. **Final demonstration:** arrange the five-headset demonstration and each member's explanation/live task. A written report does not replace these observed assessment items.

The edited [Word report](Multiplayer-VR-Laser-Tag-Final-Report.docx) and [submission-ready PDF](Multiplayer-VR-Laser-Tag-Final-Report.pdf) are based on the team draft and include the technical report and contribution statement. Use these revised copies rather than the older combined PDF if their contents differ. Do not send credentials; grant repository access through the appropriate account/invitation mechanism.

**Email subject required by the brief:** `CMPS297AG_final_project`

The brief does not state the submission deadline, grading weights, or a numeric scoring formula. Confirm those separately with the professor; no grade estimate is claimed here.
