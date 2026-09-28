# Game Manager — design

Status: phases 1–2 implemented (read-only game list); phases 3–7 pending.

## Purpose

OpenVR games only reach the OpenXR Toolkit PSVR2 layer through OpenComposite, a replacement
`openvr_api.dll` that translates OpenVR calls to OpenXR. Today each user copies that DLL into every game
folder by hand, picks the right architecture, keeps the original somewhere, and redoes it after each Steam
update. Game Manager is a Windows desktop companion that automates this safely.

Success means a user can, without reading any guide:

- see their installed Steam games and which ones are OpenVR;
- patch an OpenVR game with OpenComposite in one click, and undo it in one click;
- be told when a Steam update silently removed a patch;
- never patch a game that ships anti-cheat.

## Scope

In scope (v1):

1. Discover Steam libraries (`libraryfolders.vdf`) and installed games (`appmanifest_*.acf`): name, AppID,
   install folder.
2. Classify each game: OpenVR (an `openvr_api.dll` exists in any subfolder), probably OpenXR/native, or
   unknown.
3. Read the PE header of every `openvr_api.dll` found to know whether it is 32-bit or 64-bit.
4. Patch: back up the original DLL as `openvr_api.dll.bak`, copy the OpenComposite DLL of the matching
   architecture, optionally write `opencomposite.ini`.
5. Restore the original DLL.
6. On startup, detect patched games whose DLL was restored by a Steam update (hash comparison) and offer to
   re-patch them.
7. Compatibility list in JSON (works / broken / anti-cheat). Anti-cheat games cannot be patched.
8. OpenComposite updates: detect that upstream published a newer build than the cached one, and update every
   patched game to it in one click.

Out of scope for v1, but the architecture must allow it: a SteamVR dashboard overlay, in the same
application, to adjust CAS and foveated rendering from inside VR.

Non-goals: system-wide OpenComposite switching (`openvrpaths.vrpath`), non-Steam stores, Linux.

## Constraints

- No change to the OpenXR layer code. Everything lives in `gamemanager/`. The only shared file touched is
  `OpenXR-Toolkit.sln`, to add the new projects.
- No OpenComposite binary in the repository. It is downloaded at runtime from its official source.
- No game file is modified without a verified backup first.
- A simulation mode shows every action that would be taken without touching the disk.
- No file of a running game is touched.
- Code follows the repository conventions: MIT header in every source file, 4-space indentation,
  standard C# naming, same target framework as `companion`.

## Technology

C# 9, WinForms, .NET Framework 4.7.2 — the same stack as `companion/`.

- It builds with the existing solution and CI, with no new tooling.
- .NET Framework 4.7.2+ ships with Windows 10 and 11, so users install nothing.
- A future SteamVR overlay can be driven from C# using Valve's official `openvr_api.cs` bindings.

Rejected: .NET 8/WPF (extra runtime or ~60 MB self-contained package, the only project in the repo using
it); C++ (much more code for VDF, JSON and HTTP with no benefit here).

## Architecture

```
gamemanager/
  DESIGN.md
  GameManager.Core/     class library, no UI dependency — all logic
  GameManager/          WinForms executable — one UI over Core
  GameManager.Tests/    unit tests for Core
  compatibility.json    shipped compatibility list (phase 3)
```

Core units, each with one purpose and testable on its own:

| Unit | Responsibility | Depends on |
| --- | --- | --- |
| `VdfReader` | Parse Valve KeyValues text (`.vdf`, `.acf`) into a tree | nothing |
| `SteamLocator` | Find the Steam install folder (registry `HKCU\Software\Valve\Steam\SteamPath`, then `HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`) | registry abstraction |
| `SteamLibraryScanner` | Read `libraryfolders.vdf` (current and legacy formats) and every `appmanifest_*.acf`; return `SteamGame { AppId, Name, InstallDir }` | `VdfReader`, file system |
| `PeReader` | Read the machine type of a PE file (DOS header → `e_lfanew` → `PE\0\0` → Machine: `0x14C` x86, `0x8664` x64, `0xAA64` ARM64) | nothing |
| `GameClassifier` | Walk the install folder; list every `openvr_api.dll` with its architecture; detect `openxr_loader.dll`; return a `GameClassification` | `PeReader`, file system |

Later phases add, in Core: `CompatibilityList`, `AntiCheatDetector`, `OpenCompositeCache`, `PatchPlanner`,
`PatchExecutor`, `PatchStateStore`, `RunningGameGuard`.

The UI only calls Core. The future overlay will be a second front end over the same Core, plus a settings
writer for the toolkit's per-application registry keys (`HKCU\SOFTWARE\OpenXR_Toolkit\<app>`).

### Classification rules

- **OpenVR**: at least one `openvr_api.dll` anywhere under the install folder. Every copy is reported with
  its relative path and architecture, because Unity games often ship more than one (for example one under
  `Plugins\x86` and one under `Plugins\x86_64`).
- **OpenXR (probable)**: no `openvr_api.dll`, but an `openxr_loader.dll` exists.
- **Unknown**: neither.
- **Excluded from the list**: Steam tools that are not games, at minimum SteamVR (AppID 250820, which
  itself ships `openvr_api.dll`) and Steamworks Common Redistributables (228980).

Folder walking always walks the install folder itself, even if it is a junction or symlink (users who move a
game to another drive and leave a link behind must still get a classification). Reparse-point subfolders
found below the install folder are skipped and recorded as warnings, to avoid link loops and walking outside
the game. Reparse-point files are not skipped: compression tools such as CompactGUI or `compact /exe` (WOF)
may mark a compressed file as a reparse point, though that attribute is not always visible to user-mode code
on such a file, and many Steam users compress their games, so skipping reparse-point files would risk hiding
`openvr_api.dll`. Folders it cannot read are recorded as warnings instead of failing. Scanning runs off the UI
thread.

### Safety model (phases 4–6)

- Every change is first computed as a **plan**: a list of actions (back up file, copy file, write ini,
  restore file). Simulation mode shows the plan and stops. Normal mode executes the same plan. There is no
  code path that writes without a plan.
- Backup: the original is copied to `openvr_api.dll.bak` and the copy's SHA-256 is verified before the
  original is replaced. If a `.bak` already exists and its hash differs from the current DLL, the operation
  stops and asks the user.
- Running game guard: before any write, refuse if a running process's executable is under the game's
  install folder, and refuse if the target DLL cannot be opened for exclusive write.
- State: per game, the SHA-256 of the original DLL and of the installed OpenComposite DLL, stored in
  `%LOCALAPPDATA%\OpenXR-Toolkit-PSVR2\GameManager\state.json`. On startup, a patched game whose current
  DLL hash equals the original hash again is reported as "unpatched by an update".
- Anti-cheat: a game is blocked if the compatibility list marks it `anticheat`, or if its folder contains
  known anti-cheat markers (`EasyAntiCheat` folder or `EasyAntiCheat*.exe`, `start_protected_game.exe`,
  `BattlEye` folder or `BEService*.exe`). This ships before patching exists (phase 3).
- `opencomposite.ini`: OpenComposite aborts on unknown keys, so the manager only writes keys from a fixed
  allow-list (initially `supersampleRatio`).
- Updating OpenComposite in a patched game replaces only the OpenComposite DLL. The `.bak` file is the game's
  original DLL and is never overwritten by an update. Before replacing, the manager checks that the current
  DLL hash equals the OpenComposite hash it recorded for that game; if not (for example Steam restored the
  original), the game is handled as "unpatched by an update" instead.

## OpenComposite: source and license

- Source: https://gitlab.com/znixian/OpenOVR (GPLv3, "version 3 or later").
- Per-game DLLs, always the latest `openxr` branch build, served as raw DLLs:
  - x64: `https://znix.xyz/OpenComposite/download.php?arch=x64&branch=openxr`
  - x86: `https://znix.xyz/OpenComposite/download.php?arch=x86&branch=openxr`
- No checksums or signatures are published upstream, so the manager records the SHA-256 of what it
  downloads and installs, and verifies the PE architecture of each file before use.
- No version number is published either (the DLLs carry no version resource). A new build is detected by
  downloading the latest DLL of each architecture (about 2.5 MB) to a temporary file and comparing its
  SHA-256 with the cached one. The check runs at startup and from a "Check for update" button; a download
  that fails or is not a valid PE of the expected architecture never replaces the cache.
- License position: Game Manager never links to, bundles, mirrors or modifies OpenComposite. The user's
  machine downloads the unmodified binary directly from upstream, so GPLv3 distribution obligations fall on
  the upstream distributor, not on this MIT project. The app shows the GPLv3 notice and a link to the source
  before the first download. This is a reasoned reading of the license, not legal advice.

## Phases

Each phase ends with a stop for testing on a real library.

1. **List games** — `VdfReader`, `SteamLocator`, `SteamLibraryScanner`; window listing name, AppID, folder.
2. **Classify** — `PeReader`, `GameClassifier`; type and DLL architecture columns. Phases 1–2 are read-only.
3. **Compatibility list and anti-cheat block** — `compatibility.json`, `AntiCheatDetector`.
4. **Download OpenComposite** — cache folder, SHA-256 record, license notice, new-build check.
5. **Patch and restore** — planner, executor, simulation mode, running-game guard, state store.
6. **Post-update detection** — startup hash check and re-patch offer for games Steam unpatched, and
   "Update all" when a new OpenComposite build is available.
7. **Docs and release** — user guide, add the manager to the release ZIP, pull request.

## Testing

Core is covered by unit tests with fixture files: sample `libraryfolders.vdf` (both formats), sample
`.acf`, tiny synthetic PE headers for x86/x64/ARM64/invalid, and temporary folder trees for
classification. Patching (phase 5) is tested against temporary folders only, never real games. The UI is
tested by hand on a real Steam library.

## Open questions for later phases

- Whether the layer re-reads its registry settings while a game runs (needed for the overlay to take effect
  live). Unverified.
- Whether writing into a Steam library under `Program Files (x86)` needs elevation on a default install. The
  manager will run unelevated and offer to relaunch elevated only if a write is denied.
