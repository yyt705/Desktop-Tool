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

## Core Product Invariants

- Fence dragging must remain **strictly snapped to the desktop icon grid** (never free off-grid pixel movement) with positive Schmitt-trigger hysteresis so it never oscillates between two grid cells.
- Icons inside a dragged fence must preserve exact native 32-bit ARGB appearance (no fallback flood-fill poisoning, no DIB reuse artifacts) and never flash/splash visibly outside the fence on the desktop.
- Conflicting outsider desktop icons at a fence drop target must smoothly animate out rather than teleporting.
