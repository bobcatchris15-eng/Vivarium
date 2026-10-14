# Unattended diagnostics in normal play

Run from the Godot editor as usual. Press Ctrl+Shift+D to start recording, frame the view, and press Tab to hide the HUD. Ctrl+Shift+D stops and saves the capture. Recording does not move the camera, pause the simulation, or impose a time limit.

Editor/normal-launch captures are saved in the application's user folder:
`%APPDATA%\Vivarium\diagnostics\<UTC timestamp>-<process id>`

The optional `Launch Diagnostics.cmd` in the repository root builds the game and starts recording automatically. Its captures and console/error logs are under `build/diagnostics/launch-<timestamp>`. `build/diagnostics/latest-run.txt` identifies that launcher run. This launcher resumes the normal autosave.

Each capture contains:
- `metadata.json`: engine, adapter, settings, viewport, application log location, and measurement limits.
- `samples.jsonl`: one record per approximately one second; exact frame percentiles for that interval, CPU scope maxima, simulation backlog/rate, population, camera position, water particles/triangles, memory, GC, draw/node counts, and application warnings/errors.
- `summary.json`: refreshed during recording, marked completed after Ctrl+Shift+D or orderly game close. Whole-run percentiles use bounded 1 ms histogram bins; individual samples retain exact frame values. Startup and pauses are included, and camera/clock state is recorded.

Writes run in one background job at a time. Memory retention is bounded; storage failures stop capture with a visible error. A crash or editor force-stop can lose the current interval and pending writes, but previous samples and summaries remain. Closing the game window or pressing Ctrl+Shift+D first gives the recorder a chance to finish. If stopping through the editor, press Ctrl+Shift+D before its Stop button.

CPU scopes overlap and worker scopes are off-thread wall time; these are not GPU timings. This recorder does not certify a performance target. Godot-native console errors remain in the editor Output/Debugger; the launcher also retains them in `errors.log`.

Developer check: `powershell -File scripts/diagnostics.ps1 -Check` runs a temporary fresh world for about 12 seconds with autosave disabled, then exits.
