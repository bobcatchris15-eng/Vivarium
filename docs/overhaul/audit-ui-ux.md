# UI/UX code audit (read-only), Vivarium 0.1.4

Scope: game/UI/*, App/ToolController, App/Main, App/GameSession, UserSettings (App/Bridge.cs:501), Camera/CameraRig, Render/OverlayRenderer, Main.tscn, project.godot. Nothing was run; "unverified" marks claims resting on Godot engine behaviour rather than on code alone. Paths are relative to `game/`.

## 1. Inventory

### 1.1 Screens, panels, windows (all built in code; Main.tscn is a bare Node3D + Main.cs)
| Element | Where | Reached by |
|---|---|---|
| Loading screen (startup) | App/Main.cs:73-84,104,123-126 (CanvasLayer 100, determinate bar, MaxValue=13) | automatic |
| Loading screen (new world) | UI/Windows.cs:389-424 (CanvasLayer 100, indeterminate bar) | "Create vivarium" |
| Fatal message | App/Main.cs:258-266 (bare Label, no theme, no buttons) | content validation or load failure |
| Top bar (clock, pause, speed, status, 7 menu buttons) | UI/UiRoot.cs:40-66 | always |
| Tool chip + tool hint + spring slider/remove | UiRoot.cs:69-83, RefreshToolChip 132-165 | always (top-left) |
| Inspector (Details/Genome/Lineage tabs, Focus/Move/Remove) | UI/Inspector.cs:21-93 | Select tool click |
| Environment probe | UiRoot.cs:92-98, 235-258 | always (bottom-left) |
| Toasts (max 4; 2.5 s ok, 4 s error) | UiRoot.cs:173-184 | any `Feedback`/`Toast` |
| Radial tool wheel (inner ring, groups, species fan-out) | UI/RadialMenu.cs | RMB tap (<450 ms, <6 px): CameraRig.cs:151-166; UiRoot.cs:111 |
| Window: Species catalog 640x560 | Windows.cs:147-213 | button, `C` |
| Window: Ecosystem statistics 600x600 | Windows.cs:215-262 | button, `T` |
| Window: Save & load 560x520 | Windows.cs:265-308 | button; `Ctrl+S` quick-saves only |
| Window: Create a new vivarium 560x540 | Windows.cs:310-452 | "New" button |
| Window: Settings 620x650 | Windows.cs:456-512 | "Settings" button (no key) |
| Window: Developer overlays 520x460 | Windows.cs:516-547 | "Debug" button, `F3` |
| Window: Help / welcome 640x600 | Windows.cs:549-575 | "Help" button, `F1`, automatic on first run |
| Cursor ring, placement ghost, selection ring, pending-plant ghost | Render/OverlayRenderer.cs:25-39,65-87; ToolController.cs:204-266,427-452 | tools |
| Hydrology debug (arrows, depth dots, springs) | OverlayRenderer.cs:93-141 | Debug window; springs also with water tools |
| Ground overlay modes | Windows.cs:521-524 | Debug window |
| Diagnostic recording | App/LiveDiagnostics.cs:36 | Debug button, `Ctrl+Shift+D`; no on-screen indicator |

One window at a time (Windows.cs:78-87 closes the others). Windows are centred, not draggable or resizable.

### 1.2 Tools (ToolKind; keys ToolController.cs:288-306; wheel entries RadialMenu.cs:31-49)
1 Inspect, 2 Grab (X/Esc puts the critter back), 3 Pick plant, 4 Nutrients (hold, repeats each 0.25 s, :136), 5 Poke, 6 Rock, 7 Log (R rotates), 8 Gravel, 9 Add flora, 0 Add fauna, `G` cycles raise/lower/smooth, `H` cycles pour/drain/spring, `[` `]` or Ctrl/Shift+wheel resize, `Delete` removes the selected prop, `F` focuses the selection. MoveProp is entered from the Inspector only.

### 1.3 Bindings (all hard-coded `Key.*`; project.godot has no `[input]` section and no InputMap actions)
| Input | Effect | Where |
|---|---|---|
| RMB drag (>6 px) | mouse look, captured cursor | CameraRig.cs:162-166 |
| RMB tap | tool wheel | CameraRig.cs:155-158 |
| LMB | apply tool / select | ToolController.cs:277 |
| Wheel | dolly to cursor; orbit distance when focused | CameraRig.cs:168-175 |
| Ctrl/Shift + wheel | resize tool | ToolController.cs:278-282 |
| W A S D, Space, Ctrl | level fly, up, down | CameraRig.cs:247-257 |
| Q / E | turn | CameraRig.cs:209 |
| Shift / Alt | x4 / x0.2 speed | CameraRig.cs:234 |
| `+` `-` | fly speed (saved) | CameraRig.cs:176-179 |
| F / Esc / Home | focus / leave focus / reset view | ToolController.cs:304; CameraRig.cs:187,191 |
| P , . | pause, slower, faster | UiRoot.cs:191-193 |
| F1 F3 C T Ctrl+S Ctrl+Shift+D Tab | help, debug, catalog, stats, quick save, record, hide UI | UiRoot.cs:194-200 |

## 2. Flows

**First launch.** Main.cs:22 sets up logging, loads content and `UserSettings.Load`, shows the loading layer, loads `autosave.vivsave` if present, else grows the default preset (Main.cs:94-121). The Help window opens over the world when `ShowHelpOnStart` (Windows.cs:43), then a toast says "Island ready". No title menu, no tutorial, no world choice; the camera lands at a fixed pose (CameraRig.ResetView :55-62).

**World creation** (Windows.cs:310-452). Configurable: preset, seed (any text is hashed, :371-372), island diameter, terrain relief x0.2-2.0, spring flow x0-3, pilot tree (12 choices), guardian animal. Starting populations scale with area implicitly (:379-383). Not configurable: starting species/counts, climate/light, water level, world name (auto "Preset #seed%10000", :385); growth speed lives in Settings (:462-467). No preview of what a preset contains, no "this discards the current world" confirm. Flow: Create, loading overlay, StartWorldAsync, toast.

**Camera navigation.** Free-fly with RMB look, level WASD, wheel dolly toward the cursor, `F` orbit-focus, Esc to leave, Home to reset; collision exists (CameraRig.cs:100-110). No bookmarks, minimap, edge pan or gamepad. Focus re-targets each frame via `SelectedMarker` (ToolController.cs:172).

**Select and inspect.** Tool 1 + LMB -> `SelectionChanged` -> Inspector.Show (UiRoot.cs:114). RichTextLabel of BBCode rebuilt every 0.2 s. Fauna: Details/Genome/Lineage; flora: suitability breakdown; props: Move/Remove; guardian: need bars. A yellow ring marks the selection (OverlayRenderer.cs:31,83). No follow toggle; lineage relatives are plain text, not links.

**Species catalogue: exists** (Windows.cs:147-213, `C`). Text-only list by flora placement group plus fauna: role, habitat sentence, live count, "Introduce" (picks the species, closes the window, toasts). No search, filter, sort, collapse, thumbnails, "find in world", or hide-extinct.

**Ecosystem statistics** (Windows.cs:215-262, `T`). One row per species (fauna, then flora): sparkline, count (+births/-deaths, mean size or biomass), two-line environment summary. History is a UI-side ring of 400 samples at 1/24 bio-day (GameSession.cs:296-300). Sparklines are normalised per row, with no axis, no values, no hover, no click-through; zero-count species still listed.

**Tools and placement feedback.** Cursor ring green/red (OverlayRenderer.cs:20-21,70), ghost for rock/log/gravel, pending-plant ghost, grab circle scaling with distance, tool chip hints (UiRoot.cs:151-164). The reason a placement is invalid (`ToolController.Preview`, e.g. "outside the island") is computed every frame (ToolController.cs:204-266) but **no code reads it**. Reasons reach the player only after a click, as a toast.

**Time controls.** Pause, slower, faster (8 steps 0.125x-16x, src/Vivarium.Sim/Time/Clock.cs:18), keys `P , .`, readout "paused" or "N x", calendar "Week W, Day D  HH:MM" (Clock.cs:58). "Growth speed" (world bio acceleration) is a separate slider in Settings (Windows.cs:462-467). No direct 1x/2x/4x buttons.

**Save/load.** Save window: name box + Save, newest-first list with Load buttons; `Ctrl+S` quick-save toast. Autosave (enable/interval in Settings) writes `saves/autosave`. No overwrite confirm, delete, rename, thumbnail, or unsaved-changes prompt. Load is synchronous (GameSession.LoadFrom :254-259 -> StartWorld :126-129): the whole scene rebuild in one frame, no overlay. No quit entry or prompt (OS close only).

**Settings** (Windows.cs:456-512; settings.json rewritten on every change, Bridge.cs:565-575). Growth speed, fly speed, mouse sensitivity, invert Y, autosave on/interval, interface size, 9 render toggles + blur amount + 3D resolution (4 steps). Missing: fullscreen/resolution, vsync/FPS cap, key rebinding, audio, reset to defaults, colour-blind mode.

**Help/discoverability.** One long BBCode page (Windows.cs:549-575). Tooltips on top-bar buttons (Settings has none). Wheel buttons show the name in the hub on hover only. No pause menu, no contextual hints.

**UI scaling/accessibility.** See U6, U7, U26. Scale changes only the theme default font and a few margins; stretch mode is unset (project.godot:29 sets only `aspect="expand"`).

**Developer diagnostics.** Debug window (overlay modes, hydrology, counters, per-system ms; Windows.cs:516-547), recording (`Ctrl+Shift+D`), status line shows FPS and fly m/s to every player (UiRoot.cs:232), `FrameProfiler.Measure` scopes.

## 3. Visual language as implemented

**Single source:** UiKit.cs. Colours `PanelBg (0.07,0.10,0.13,0.86)`, `Accent (0.36,0.80,0.62)`, `Text (0.92,0.95,0.96)`, `Muted (0.65,0.72,0.76)` (:10-13). Theme scales only default font 15, panel margins 10x8, Button radius 6 and margins 8x4 (:15-32). Row/column separation 6 (:72,80). `UiKit.Window`: title 19, glass padding 14x10, corner 14 (:91-104).

**Type sizes in use (px):** 12 (hint, wheel), 13 (hints/status, 10 uses), 14 (7 uses), 15 (default, toasts, probe title), 16, 17 (inspector title), 18 (clock), 19 (window title), 24 (loading). Nine sizes, no scale. No font file exists in game/, so the default engine font is used.

**Glass:** GlassPanel + Shaders/glass.gdshader (screen-texture blur LOD 3.5, tint, grain, rim). Corners 14 (top bar, windows), 12 (chip, probe), circle (hub). Paddings 14x7, 12x6, 12x8, 14x10, 6x6.

**Inconsistencies (evidence):**
- Three button looks: theme Button (r6 opaque teal, UiKit.cs:21-26), top-bar GlassButton (r8 white alpha, UiRoot.cs:124-130), radial discs (r35 with shadow, RadialMenu.cs:114-125).
- Two panel looks: GlassPanel (top bar, chip, inspector, probe, windows, hub) versus theme PanelContainer (toasts, catalog entries via `UiKit.Panel`, :34-39). Catalog entries are opaque boxes nested in glass.
- Colour literals outside UiKit: UiRoot.cs:129, :176 (toast colours), Windows.cs:209, OverlayRenderer.cs:20-21,31, RadialMenu.cs:121-124, and 14 BBCode hex colours (`#9ab #f88 #8e8 #fd8 #aaa #4f7 #f66`) in Inspector.cs/Windows.cs/UiRoot.cs.
- Loading backdrop `(.035,.055,.045)` appears twice with different alpha (Main.cs:74 .96, Windows.cs:408 .98) and matches neither `boot_splash/bg_color (0.06,0.09,0.12)` (project.godot:21) nor the glass tint: three different dark tones at startup.
- Theme lacks CheckBox, OptionButton/PopupMenu, LineEdit, HSlider, ScrollBar, ProgressBar, HSeparator and tooltips, so Settings, New world and Save windows are mostly default-grey widgets on glass (Windows.cs:271,312-327,456-512). The loading ProgressBar and fatal Label sit in CanvasLayers outside UiRoot's theme (unverified: theme inheritance across CanvasLayer).
- Spacing is ad hoc (6/8/10/12/14/16); top-bar height is assumed 52 (UiRoot.cs:21) rather than measured.
- Icons are Unicode glyphs/emoji (`⏸ ▶ ◀◀ ✕ 🎲 ◎ ✋ ✂ ✦ ↯ ⬢ ▭ ⁂ ❀ ✺ ▲ ≋ ▧ ♧ ♣ ⊚ ⇄ █ ░`) resolved by OS fallback fonts; appearance varies per machine.
- Animation: only the wheel open tween (scale 0.7->1 Back-out 0.16 s, fade 0.12 s, RadialMenu.cs:151-155). Windows, toasts, inspector and chip appear and vanish instantly.

## 4. Per-frame and frequent UI cost
1. **Windows._Process (Windows.cs:130-134)** calls `Layout()` for every visible window every frame (:58-76): `GetMeta`, recursive `FindChild(name)` (:65), `GetCombinedMinimumSize` of the whole body, `scroll.CustomMinimumSize =`, `p.Size=; p.ResetSize()`, then Size/Position again. Each assignment invalidates minimum size and re-sorts the container. With the Catalog open (hundreds of Controls) that is a full relayout per frame.
2. **GlassPanel (GlassPanel.cs:40-44)**: every `NotificationSortChildren` posts `CallDeferred(Sync)` writing 3 shader parameters; `ItemRectChanged += Sync` writes them again. Combined with (1): one deferred call + 3 parameter writes per open window per frame.
3. **Glass shader**: each GlassPanel reads `hint_screen_texture` (5 taps + hash per pixel); Godot back-buffer copies per such item (unverified). Always on: top bar, chip, probe; plus inspector, windows, hub when shown. Cost on the Radeon 860M is unmeasured.
4. **UiRoot._Process at 5 Hz (:207-217)**: RefreshClock builds 3 strings and reads FPS; RefreshProbe builds a long string with `HabitatTagsAt(...).Distinct()` and about 9 field samples; then Inspector.Refresh and Windows.Refresh.
5. **Inspector.Refresh (Inspector.cs:63-93)** rebuilds the text (suitability, lineage, `Lineage.Children`) and assigns `_body.Text` every 0.2 s even when unchanged, so the RichTextLabel re-parses BBCode and relayouts each time.
6. **UpdateStats (Windows.cs:242-262)**, 5 Hz while open: per species `History.Select(h => h.X.FirstOrDefault(...))` over up to 400 samples = O(S^2 x 400) with LINQ allocations, then `Sparkline.SetValues` (Clear/AddRange/QueueRedraw) for every row.
7. **UpdateCatalogCounts (Windows.cs:192-212)**, 5 Hz while open: `w.Flora.Items.Count(f => f.SpeciesId == id)` per species = O(species x flora), woody population/cap queries (`cap` unused, :204), `AddThemeColorOverride` on every label each tick.
8. **Rebuild on open**: RebuildCatalog (QueueFree + recreate all Controls, :156-190), RebuildStats (:225-240), RebuildSaves (:286-308), which also calls `SaveSystem.Inspect` for every save file synchronously, on open and after every save.
9. **ToolController._Process (:105-)**: `_pendingPlantGhosts.Keys.ToArray()` allocated per frame (:109), a `Selection.Raycast` per frame (:119), UpdatePreview, `GuiGetHoveredControl`; `OverlayRenderer.ShowGhost` reassigns `Mesh` each frame (:77).
10. **Settings sliders (Windows.cs:511)**: every drag step runs `Session.ApplySettings()` (re-applies the render stack and rebuilds the Theme via `ApplyScale`, UiRoot.cs:119) and **writes settings.json synchronously**.
11. **Toasts** create/free a PanelContainer+Label+timer per call; holding Nutrients toasts every 0.25 s (ToolController.cs:136,479).
12. **RadialMenu.ClearOuter** frees and recreates species buttons, each with 4 new StyleBoxFlat, per submenu change (RadialMenu.cs:110-129,298-308).

## 5. Defects and opportunities (ranked within group)
"Owner?" = needs a design decision from the project owner.

### 5A. Bugs
**U1 Tab "hide UI" breaks clicking and does not hide the inspector.** UiRoot.cs:200 toggles `Visible` on every UiRoot child except Windows/Toasts, including `Radial` (hidden by default). First Tab makes the full-rect, `MouseFilter=Stop` Radial visible (RadialMenu.cs:81): the hub shows and `_GuiInput` swallows every click (:310-314) until Tab is pressed again. Inspector re-shows itself within 0.2 s (Inspector.cs:67 `Visible=_sel.IsHit`). Remedy: replace with a `_hudHidden` flag, hide a named list (TopBar, ToolChipPanel, ProbePanel; Inspector through a flag read in Refresh), never touch Radial. Size S. Files: UI/UiRoot.cs, UI/Inspector.cs. Owner? no.

**U2 No Esc for windows; keyboard-blocked trap.** Windows close only by the X (UiKit.cs:97). `Toggle` sets `KeyboardBlocked` for SaveLoad/NewWorld (Windows.cs:84) and UiRoot ignores keys while blocked (UiRoot.cs:188), so F1/C/T/Esc cannot close them. Remedy: Esc handler in Windows closes the open window; block only while a LineEdit has focus (use `FocusExited` to unblock). Size S. Files: UI/Windows.cs, UI/UiRoot.cs. Owner? no.

**U3 Unreadable autosave is silently replaced.** Main.cs:100-104 logs a warning and grows a new island; the next autosave overwrites the same path. Remedy: rename the bad file to `autosave.corrupt-<ts>`, toast "Your last session could not be opened", list it in the Save window. Size S. Files: App/Main.cs, App/GameSession.cs. Owner? no.

**U4 Help "show on start" is cleared only by the X button.** The clear is in `Close()` (Windows.cs:95); `F1`/Help-button toggle-close (`Toggle`, :78-87) never clears it, so the welcome window reappears every launch. Remedy: shared `OnClosed(name)` for both paths. Size S. File: UI/Windows.cs. Owner? no.

**U5 Load is synchronous with no feedback; Load and New discard without asking.** Windows.cs:300 -> GameSession.cs:254-259 builds every stage in one frame (a visible freeze), while creation has the staged overlay. Remedy: extract `LoadingOverlay` (new UI/LoadingOverlay.cs) used by Main, Create and Load; Load via `StartWorldAsync`; confirm when the world changed since the last save. Size M. Files: UI/Windows.cs, App/GameSession.cs, App/Main.cs, UI/LoadingOverlay.cs. Owner? yes (discard-confirm policy).

**U6 Interface-size slider mostly does nothing and breaks layout.** Theme scale touches only default font and a few margins (UiKit.cs:15-32). Unscaled: every `UiKit.Label(.., size)`, window sizes (Windows.cs), `TopBarHeight=52` (UiRoot.cs:21) which positions chip (:82), inspector (:88) and windows (Windows.cs:62), wheel `ButtonSize/InnerRadius/OuterRadius` (RadialMenu.cs:29), inspector width 330/350 (Inspector.cs:35; UiRoot.cs:88). At 1.6x the top bar outgrows 52 px and its minimum width exceeds a 1280 window. Remedy: set `window/stretch/mode="canvas_items"` (project.godot:29) and `GetWindow().ContentScaleFactor = UiScale`, drop the scale multipliers from `BuildTheme`, measure the top bar's real height. Size M. Files: project.godot, App/GameSession.cs, UI/UiKit.cs, UI/UiRoot.cs. Owner? yes (confirm 3D `Scaling3DScale` stays independent and the 1600x900 reference captures stay valid).

**U7 Placement validity is colour-only and the reason is never shown.** Green/red ring (OverlayRenderer.cs:20-21,70); `Preview` has no reader. Colour-blind players cannot tell valid from invalid; others learn why only after clicking. Remedy: add `PreviewChanged`, show the text in the tool chip under `_toolInfo` (UiRoot.cs:158-165) or at the cursor; add a shape cue to the invalid ring. Size S-M. Files: UI/UiRoot.cs, App/ToolController.cs, Render/OverlayRenderer.cs. Owner? no.

**U8 Toast spam.** Nutrients hold toasts every 0.25 s (ToolController.cs:136,479); each poke/place success toasts. The 4-deep stack churns over the world. Remedy: coalesce identical consecutive messages ("x3"), skip success toasts for hold/brush tools, fade in/out. Size S. Files: UI/UiRoot.cs, App/ToolController.cs. Owner? no.

**U9 Top bar jitter and silent truncation.** `_clock` has no minimum width (UiRoot.cs:41), so the Pause/Slower/Faster buttons shift as "Week N, Day D  HH:MM" changes width; the Pause text swaps width too (:221); `_status` is clipped at a minimum of 80 px (:48), dropping autosave/lag text first. Remedy: fixed minimum widths for clock and pause; move FPS/fly speed/autosave off the main bar (debug or secondary line). Size S. File: UI/UiRoot.cs. Owner? no.

**U10 Internal stage names reach the player.** "Preparing your island - WaterFluid / SoilDetail / AmbientGround / Forest floor / WorldChanged" (GameSession.cs:181-225; Main.cs:125). Progress `MaxValue=13` is hard-coded (Main.cs:79) while "Forest floor" loops add steps; the creation overlay is indeterminate (Windows.cs:416). Remedy: stage-id -> label + weight table inside the shared overlay. Size S (rides on U5). Files: App/GameSession.cs, UI/LoadingOverlay.cs.

**U11 Fatal screen is a dead end.** Main.cs:258-266: unthemed Label, no Quit/Retry/"start a new island". With U3 a failed load strands the player. Remedy: themed panel with Quit, Open logs folder, New island. Size S. File: App/Main.cs. Owner? no.

**U12 Modifier collisions.** `C/T/P/F` handlers ignore modifiers (UiRoot.cs:191-200); holding Ctrl to descend then pressing C opens the catalog; `Ctrl+S` also flies the camera back while descending (CameraRig.cs:250,255). Remedy: require no Ctrl/Alt for plain keys. Size S. Files: UI/UiRoot.cs, App/ToolController.cs. Owner? no.

### 5B. Missing features
**U13 No pause/escape menu or quit.** Esc does nothing at idle; Settings has no hotkey. Remedy: Esc opens a menu (Resume, Save, Load, New, Settings, Help, Quit) hosted by Windows. Size M. Files: UI/Windows.cs, UI/UiRoot.cs. Owner? yes (menu versus pure-HUD feel).

**U14 Catalogue is a flat text wall.** No search, collapse, thumbnail, locate, sort, hide-extinct (Windows.cs:156-190). Remedy: collapsible sections, filter box, species portrait, "Find nearest" -> `Tools.Select` + `FocusSelected`; split into UI/CatalogWindow.cs. Size L. Files: UI/Windows.cs, UI/CatalogWindow.cs, App/ToolController.cs. Owner? yes (portrait source: baked art or live render; ties to the visual overhaul).

**U15 Statistics are not a dashboard.** Max-normalised sparklines without axis or value, all species incl. extinct, no totals or diversity, fixed window (Windows.cs:215-262; UiKit.cs:114-130). Remedy: show last/max per row, sort by count and dim extinct, totals header, hover readout; store history as per-species arrays (see U21). Size M. Files: UI/Windows.cs, UI/UiKit.cs. Owner? yes (which metrics matter).

**U16 Settings gaps.** No window mode/resolution, vsync/FPS cap, key rebinding, reset, colour-blind mode (DisplayServer is touched only by the review runners). Remedy: add `WindowMode`, `Vsync`, `MaxFps`, `ColorVision` to UserSettings (Bridge.cs:501-523) and the window; define InputMap actions in project.godot and replace hard-coded `Key.*`. Size L. Files: App/Bridge.cs, UI/Windows.cs, project.godot, Camera/CameraRig.cs, App/ToolController.cs, UI/UiRoot.cs. Owner? yes (rebinding scope).

**U17 Save UX and world identity.** World name/seed not shown; no thumbnail, rename, delete, overwrite warning (Windows.cs:265-308). Remedy: name in the top-bar tooltip, screenshot at save, Delete with confirm, overwrite prompt. Size M. Files: UI/Windows.cs, App/GameSession.cs, SaveManifest in src/Vivarium.Sim. Owner? yes.

**U18 Weak first-run guidance.** Help is one dense page; the wheel (the only mouse route to tools) is explained only there; wheel buttons carry no key hints (RadialMenu.cs:90-107). Remedy: key hints on wheel buttons, a short welcome, a "right-click: tools" hint until first use. Size M. Files: UI/RadialMenu.cs, UI/Windows.cs, UI/UiRoot.cs. Owner? yes.

**U19 Lineage/genome are text-only.** Plain BBCode rows with no navigation (Inspector.cs:179-194), block-glyph bars (`UiKit.Bar`). Remedy: `meta_clicked` links to select/focus a relative, drawn bars. Size M. Files: UI/Inspector.cs, UI/UiKit.cs. Owner? no.

### 5C. Performance and polish
**U20 Per-frame window relayout** (section 4.1-2). Remedy: run `Layout` on open, on viewport `size_changed` and on `MinimumSizeChanged`; cache the ScrollContainer in `_w`; set shader parameters only when Size changed. Size S-M. Files: UI/Windows.cs, UI/GlassPanel.cs, UI/UiKit.cs. Owner? no. Best cost-to-fix ratio.

**U21 Refresh churn** (4.4-4.8). Remedy: compare text before assigning; one pass counting per species; per-species history arrays appended once per sample in GameSession.cs:296-300; build Catalog/Stats once per content load and refresh values only. Size M. Files: UI/Inspector.cs, UI/Windows.cs, UI/UiRoot.cs, App/GameSession.cs. Owner? no.

**U22 Settings write path.** Debounce: apply live, save on slider `DragEnded` or a 0.5 s timer; split `ApplySettings` so a UI-scale change skips the render stack. Size S. Files: UI/Windows.cs, App/GameSession.cs. Owner? no.

**U23 Theme consistency** (section 3). Extend `BuildTheme` to CheckBox, OptionButton, PopupMenu, LineEdit, HSlider, ScrollBar, ProgressBar, HSeparator, tooltip; replace colour literals and the three button looks with theme variations; make toasts glass. Size M. Files: UI/UiKit.cs, UI/UiRoot.cs, UI/RadialMenu.cs, UI/Windows.cs, UI/Inspector.cs. Owner? yes (look must match the visual-overhaul direction).

**U24 Fonts and icons.** Bundle a UI font and icon set (SVG or icon font), set them in the theme, drop emoji. Size M. Files: UI/UiKit.cs, new game/assets/ui/*, RadialMenu.cs, UiRoot.cs, Windows.cs. Owner? yes (font choice, licence).

**U25 Motion.** 120-180 ms fades for windows, toasts, inspector, chip, using the durations in section 6. Size S-M. Files: UI/Windows.cs, UI/UiRoot.cs, UI/Inspector.cs. Owner? no.

**U26 Keyboard/gamepad focus.** Every button sets `FocusMode.None` (UiKit.cs:52; RadialMenu.cs:112) and the theme `focus` style is empty (UiKit.cs:25): no Tab or arrow navigation, no focus ring. Remedy: enable focus inside windows, add a focus stylebox; keep None on HUD buttons. Size M. Files: UI/UiKit.cs, UI/Windows.cs. Owner? no.

**U27 Small items.** (a) `Inspector.Show(WorldHit)` hides `Control.Show()` (Inspector.cs:53): rename `Inspect`. (b) Pilot-tree ids (Windows.cs:27) and labels (:322) are two hand-synced arrays. (c) Dead `cap` (Windows.cs:204). (d) Inspector has no max height; long lineage can leave the screen (Inspector.cs:42; UiRoot.cs:86-88): scroll the body. (e) 70 px wheel discs with 12 px `\n`-split names overflow for long species names (RadialMenu.cs:220,258). (f) 12 px tool hint (UiRoot.cs:71). (g) Windows cannot be dragged or dismissed by clicking outside. Size S each. Owner? no.

## 6. Proposed minimal design tokens (put in `UiKit`; call sites stop passing sizes and colours)
Based on values already in use; rely on `ContentScaleFactor` for scaling (U6).

**Type (px):** `Caption 12`, `Small 13`, `Body 15` (theme default), `Label 17`, `Title 19`, `Display 24` (loading). Map existing 14 -> 13 or 15, 16 -> 15 or 17, 18 -> 17 or 19. One bundled UI font, one icon set.

**Spacing (px):** `xs 4`, `s 6`, `m 10`, `l 14`, `xl 20`. Row/column separation `s`; button margins `s` x `xs`; HUD panel padding `m` x `s`; window padding `l` x `m`; screen gutter `m`.

**Radii:** `Control 6` (buttons, fields), `Chip 12`, `Window 14`, `Disc` = half size. Fold GlassButton's 8 into 6 or 12.

**Palette (current hues):**
- Surface: `PanelBg #12191F @0.86`, hover `white @0.13`, active `Accent @0.35`, disabled `#1F2429 @0.8`; expose the glass `tint` uniform from the same token.
- Text `#EBF2F5`, Muted `#A6B8C2`, Accent `#5CCC9E`.
- Status, text colour / world colour: Ok `#CCFFD9` / `#40F273`, Warn `#FFD08A` / `#FFDD55`, Error `#FF998C` / `#FF6B63`, Info `#99AABB`. Every status needs an icon or shape cue besides colour.
- Selection `#FFE64D`.
- Scrim `#090E0B @0.96`, also used for `boot_splash/bg_color` (project.godot:21).

**Motion:** `Instant 0`, `Fast 120 ms` (hover, selection, fade-in), `Base 180 ms` (windows, inspector, toasts), `Slow 280 ms` (loading crossfade). Easing Cubic Out in, Cubic In out; the wheel keeps its Back-out 160 ms. Toast lifetime ok 2.5 s, error 4 s.

**Elevation:** glass blur LOD 3.5 with rim as now; shadow `6 px black @0.35` on discs only.

**Theme variations:** `Button/Glass`, `Button/Disc`, `Label/Muted`, `Label/Title`, `Panel/Glass`, `Panel/Toast`.

## 7. Suggested order
1. U1, U2, U4, U3, U9, U12 (small bugs).
2. U20, U22, U21 (cost; verify with `--perf-test` and the Ui/Windows `FrameProfiler` scopes).
3. U6 + U23 + tokens (owner decisions), then U7, U8, and U5/U10/U11 on a shared LoadingOverlay.
4. Decision-dependent features: U13, U14, U15, U16, U17, U18, U24.
