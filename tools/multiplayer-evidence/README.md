# Rocksmith multiplayer evidence collector

Help test regular **Rocksmith 2014 Remastered** multiplayer. Run the collector,
follow the numbered prompts, and send one results ZIP to the RockList owner.
No AI agent, Cheat Engine, RockList account, or development tools are needed.

This is a diagnostic candidate, not a multiplayer fix. It also recognizes the
known Learn & Play build for comparison. Other executable builds produce a
fingerprint report without game-memory reads.

## Before starting

- Use 64-bit Windows 10 or 11 with Windows PowerShell 5.1 available.
- Make sure normal multiplayer works and each instrument controls its own player.
  Keep your usual audio setup. This tool does not install or configure RS_ASIO.
- Close Cheat Engine, other game readers and RockList Desktop for this capture.
  No debugger or additional game mod is needed.
- Pick easy songs and comfortable difficulty. Playing perfectly is unnecessary;
  some recognized hits on each instrument are what distinguish the players.
- Select **Two people** to play together, or **One person** to test both instruments
  in turn. Either player can use guitar or bass.
- Allow roughly 30–45 minutes for all 39 checkpoints, including two full game
  restarts. You can skip steps or finish early; partial evidence is useful.

## Run and follow the prompts

1. Extract **all** files from the collector ZIP into one folder.
2. Double-click **Start.cmd**. Use your normal Windows account; administrator
   access is not normally needed. Keep the collector open throughout the test.
3. For each numbered step, click **Start step**, perform the action in Rocksmith,
   then click **Done - next step**. Use **Repeat step** for a mistake during the
   current step, or **Skip step** if you cannot perform it. The **Step** selector
   shows each step's status. Select a step and click **Go** to return to it.
   Selecting a step changes the instructions; set up the matching situation in
   Rocksmith before following them. Earlier attempts stay in the capture.
4. Follow the prompts through single-player, multiplayer tuning, separate and
   combined instrument activity, pause/resume, tuner/mixer, restarting/exiting a
   song, results, swapped/same arrangements, another song, and two full game
   restarts. Use the **ESC pause menu**, not Riff Repeater, when asked to pause.
5. At the paused multiplayer checkpoints, **Search for player data** appears only
   if the expected player data cannot be read. Click it while the game is paused,
   wait for it to finish, then continue. It may appear again after a game restart.
6. On results steps, **Enter results** is highlighted until you save at least one
   observed **accuracy** or **longest streak** value. **Done** stays disabled until
   then; use **Skip step** if you missed the results. Results entry is disabled on
   other steps. Recording continues automatically during the short results screen;
   you can enter remembered values afterward. Leave unknown values blank. Optional
   duration and arrangement fields help cross-check the capture. Percentages accept
   a decimal point or comma and an optional `%`; duration accepts seconds or `m:ss`. Do not use mastery
   as accuracy or guess values. Repeating a results step requires a new entry.
7. Click **Finish and save**. File Explorer selects the verified results ZIP.

**Send exactly `remastered-multiplayer-results.zip` privately to the RockList
owner.** Do not send the program ZIP or the entire capture folder. The collector
shows the full location and archive SHA-256. Read `REPORT.md` beside the ZIP before
sharing. Nothing is uploaded automatically.

Captures are stored under `%LOCALAPPDATA%\RockList Multiplayer Evidence\capture-…`.
Each run gets its own folder. No existing capture is overwritten.

## If something goes wrong

- If Rocksmith crashes, stop the test and choose **Finish and save**. Tell the
  RockList owner which game action preceded the exit. This tool records process
  exits but does not decide whether they were crashes.
- If the collector closes unexpectedly, leave the original folder intact. Run
  **Recover.cmd**, select that capture folder, and send the verified results ZIP
  it creates in a new `recovered-…` folder. Recovery reports any incomplete final
  record it had to omit. If recovery refuses the capture, keep it and ask for help.
- If the game build is unrecognized, choose **Finish and save** and send that
  small report; do not try to change the pointer addresses yourself.
- If access is denied or Windows/security software blocks the tool, stop and ask
  for help. Do not disable antivirus or add exclusions. The launcher uses an
  execution-policy setting for its own PowerShell process only; it does not
  change your Windows policy or install a service/startup task.
- Recording stops at three hours, 50,000 samples, or 200 MiB of sample data.
  Choose **Finish and save** when a limit is reached.
- If saving fails, keep the collector open, free disk space and use **Retry saving**.
  Recording remains stopped. If retrying fails, keep the capture folder for recovery.

## What is collected

The tool opens Rocksmith with **query and read permissions only**. It reads the
known candidate pointer chains, typed note counters, timers, pause state and a
small allowlist of game-stage names. It records the exact executable fingerprints,
relative timing, pointer dereferences and user-labeled test phases. Values that
cannot be read remain explicitly unknown. There are no writes, freezes, injected
code, debugging, speed changes, memory dumps, or changes to the game installation.

**Search for player data** searches readable non-image memory for decimal `111000` and
plausible nearby note counters. Each request stops at 20 seconds, 512 MiB attempted,
or 256 candidates. It keeps only interpreted fields and addresses, not scanned
memory blocks. A search can briefly add CPU load. Its addresses are temporary;
they are not automatically valid player structures or stable pointer chains.

The results contain no screenshots, raw memory blocks, game packages, account
names, Windows paths, hostname, raw process IDs, profile files, Desktop settings,
credentials or calendar timestamps. Song names and arrangement GUIDs are replaced
with consistent aliases within the capture. Unexpected text is omitted. The
export includes only specifically named collector files and verifies their hashes.
It still reveals gameplay activity and software/build information, so send it
privately. Do not add unrelated logs or screenshots without reviewing them.

## What the receiving developer does

1. Verify `manifest.sha256` inside the ZIP and read `REPORT.md`.
2. Check build/edition and separate sessions in `sessions.json`; the proposed
   regular-Remastered layout remains unvalidated until real evidence supports it.
3. Correlate `phases.json`, `results.json` and continuous `samples.jsonl` by relative
   milliseconds and session ID. User-completed does **not** mean passed.
4. Compare both independent pointer paths per player, counter deltas during
   reciprocal activity, same-arrangement cases and ownership after each restart.
5. Examine raw timers, pause byte, stage and aliases through start, pause, resume,
   restart, natural end and early exit. Account for sampling gaps and pointers
   aliasing at teardown. Compare computed accuracy to observed accuracy.
6. If needed, correlate optional candidate addresses with player activity. Candidates
   are process-local. A later targeted collector or manual pointer investigation
   may still be necessary to derive new roots and restart-stable chains.
7. Implement only after evidence review and successful restart validation. This
   tool does not run the production state reducer, derive mastery, read PSARC
   metadata/duration, or automatically discover arbitrary timers and pointer chains.

The layout reference is RockSnifferLib commit
`72e652a4725d8af8ba7d0c63524ab0feb490cfa5`. The collector fingerprints its own
source in `collector.json` so the exact probe can be identified with each return.

## Development checks

Run from Windows PowerShell in this directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Collector.ps1 -Demo -SmokeTest
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Collector.ps1 -Demo -SmokeTest -WorkflowTest
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Collector.ps1 -Demo
```

Tests use synthetic layouts, including failed reads, teardown aliasing, privacy
checks, bounded candidate matching, process-identity rejection, verified exports
and recovery. Demo mode never attaches to a game. These checks do not establish
real regular-Remastered compatibility.
The workflow check runs from the source checkout and exercises results gating,
repeat isolation, step selection, skipped-step recovery and unique-step progress.
