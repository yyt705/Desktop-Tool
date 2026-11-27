# DeskFences Project Rules

## Release, Versioning & Git Workflow (Mandatory)

After **every** code modification in this repository:

1. **Bump version in `src/AssemblyInfo.cs`** using Semantic Versioning (`MAJOR.MINOR.PATCH`):
   - Bug fixes / optimizations / tweaks → increment `PATCH` (e.g. `1.0.1` → `1.0.2`).
   - New user-facing features → increment `MINOR` and reset `PATCH` to `0` (e.g. `1.0.2` → `1.1.0`).
   - Breaking config/architecture changes → increment `MAJOR`.
   - Keep `AssemblyVersion("X.Y.Z.0")`, `AssemblyFileVersion("X.Y.Z.0")`, and `AssemblyInformationalVersion("X.Y.Z")` strictly in sync.
2. **Run the full ComCtl v6 regression test suite** before building or committing:
   - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1`
   - All test suites must pass with exit code 0. Never touch or modify the user's real desktop icons during tests.
3. **Rebuild `DeskFences.exe`**:
   - Stop any running `DeskFences` process if needed, then run `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`.
4. **Automatically commit, tag, and push to GitHub (`origin`)**:
   - Stage only tracked source/test/doc/config-template files (never commit `DeskFences.exe` or `fences_config*.json`).
   - Commit with a clear message referencing the version (e.g. `v1.0.2: ...`).
   - Create an annotated Git tag matching the exact version: `git tag -a vX.Y.Z -m "vX.Y.Z: ..."` so every historical version can be checked out or compared directly by version number.
   - Push both `main` and the new tag to GitHub: `git push origin main vX.Y.Z`.
   - In the final reply, always report the new version (`vX.Y.Z`) and commit hash.

## Anti-Regression Engineering Discipline (Mandatory)

To prevent "fix A, break B" regressions across real Windows 11 Explorer environments:

1. **Evidence-First Diagnosis**: For any real-desktop latency, visual, or Explorer interaction issue, inspect real telemetry (`%TEMP%\DeskFences_drag_perf.log`, `fences_config.json`) BEFORE modifying code. Never patch Explorer Win32 interactions on guesswork alone.
2. **Never Fight Explorer with Illegal Coordinates**: NEVER park real desktop icons at offscreen negative/positive coordinates (e.g. `(-5000, -5000)`). Windows 11 `SHELLDLL_DefView` (`CDefView`) with multi-monitor `LVM_SETWORKAREAS` clamps out-of-workarea icons onto `(0,0)` and permanently displaces existing desktop icons. Keep real icons stationary during drag, hide old member icon rects cleanly via the per-pixel `OldSlotMaskForm` wallpaper mask (zero `LVIS_CUT` residue), and commit member moves once on drop.
3. **Hard UI-Thread Latency Budget (150+ Icon Desktops)**:
   - NEVER call full `GetAllIcons()` synchronously on hover pre-warm, `OnMouseDown`, or steady-state periodic watch ticks when no icon moved.
   - NEVER run blocking out-of-process `PrintWindow` on the UI thread during `OnMouseDown` or `StartActualDrag` (use background `ThreadPool` capture for out-of-process Explorer hosts).
   - NEVER clear `_identityCache` on periodic steady-state ticks (only invalidate on genuine `FileSystemWatcher` events, item count changes, or desktop host changes).
4. **Scale & Invariant Test Gate**: Every bug fix must add or strengthen an automated test in `tests/` (including the 150-icon / 30-member-fence dual-workarea stress test) asserting zero unrelated icon displacement and zero unnecessary full scans.

## Core Product Invariants

- Fence dragging must remain **strictly snapped to the desktop icon grid** (never free off-grid pixel movement) with positive Schmitt-trigger hysteresis so it never oscillates between two grid cells.
- Icons inside a dragged fence must preserve exact native 32-bit ARGB appearance (no fallback flood-fill poisoning, no DIB reuse artifacts), NEVER move offscreen or flash/splash visibly outside the fence, and NEVER displace unrelated desktop icons (especially top-left `(0,0)`).
- Fence drag pickup must remain immediate (`< 25ms` UI-thread work even on 130+ icon desktops with 25+ member fences) with zero synchronous full `GetAllIcons()` scans during drag pickup.
- Conflicting outsider desktop icons at a fence drop target must smoothly animate out rather than teleporting.
- Only manual user drag-in/drag-out or in-fence context-menu creation changes fence membership; dropping a manually dragged icon inside a fence's visual glass body or inner grid must adopt it cleanly without false ejection.
- Multi-screen/resolution switching must preserve per-topology fence layouts, member slots, and loose icon layout proportionally.
