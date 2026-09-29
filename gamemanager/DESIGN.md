# Game Manager — design

Status: phases 1–6 implemented (game list, compatibility list and anti-cheat block, OpenComposite download and
new-build check, patch and restore, post-update detection and Update all); phase 7 pending.

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
6. After every scan, detect patched games whose DLL was restored by a Steam update (hash comparison) and offer
   to re-patch them.
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
  GameManager/compatibility.json   shipped compatibility list (phase 3), copied next to the exe
```

Core units, each with one purpose and testable on its own:

| Unit | Responsibility | Depends on |
| --- | --- | --- |
| `VdfReader` | Parse Valve KeyValues text (`.vdf`, `.acf`) into a tree | nothing |
| `SteamLocator` | Find the Steam install folder (registry `HKCU\Software\Valve\Steam\SteamPath`, then `HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`) | registry abstraction |
| `SteamLibraryScanner` | Read `libraryfolders.vdf` (current and legacy formats) and every `appmanifest_*.acf`; return `SteamGame { AppId, Name, InstallDir }` | `VdfReader`, file system |
| `PeReader` | Read the machine type of a PE file (DOS header → `e_lfanew` → `PE\0\0` → Machine: `0x14C` x86, `0x8664` x64, `0xAA64` ARM64) | nothing |
| `GameClassifier` | Walk the install folder once; list every `openvr_api.dll` with its architecture; detect `openxr_loader.dll`; record anti-cheat markers (via `AntiCheatDetector`) found during the same walk; return a `GameClassification` | `PeReader`, `AntiCheatDetector`, file system |
| `AntiCheatDetector` | Recognize anti-cheat files and folders by name (`EasyAntiCheat`, `EasyAntiCheat_EOS`, `BattlEye` folders; `EasyAntiCheat*.exe`/`.sys`/`.dll`, `start_protected_game.exe`, `BEService*.exe`, `BEClient*.dll` files) | nothing |
| `CompatibilityList` | Load and parse `compatibility.json` (R6): rejects a wrong `"version"`, skips a duplicate AppID (anti-cheat wins), a null entry, an invalid AppID or an unknown status, each with a warning | `JsonFile` |
| `CompatibilityVerdict` | Combine a compatibility list entry's status with the anti-cheat markers found during classification into one verdict (blocked / works / broken / untested) | nothing |
| `AppDataPaths` | Resolve the per-user app-data root under `%LOCALAPPDATA%` and its subpaths (`settings.json`, the `opencomposite` folder); only `Program.cs` calls `ForCurrentUser` | nothing |
| `SettingsStore` | Load and save `settings.json` (OpenComposite license acceptance and its timestamp, last update-check time) | `JsonFile` |
| `FileHash` | SHA-256 of a file on disk | nothing |
| `AtomicFile` / `JsonFile` | Atomic file replace and delete; DataContractJsonSerializer-based JSON read and write (BOM-less UTF-8, `UseSimpleDictionaryFormat`). The only place in Core allowed to call `File.Write*`/`Directory.CreateDirectory`-style APIs, besides `OpenCompositeCache`, `HttpDownloader` and `PatchExecutor` (the only writer into game folders) | file system |
| `HttpDownloader` | Download a URL to a file over HTTPS; rejects a response body shorter than its declared Content-Length. The only source of network I/O in Core | `System.Net.Http` |
| `OpenCompositeCache` | The per-user OpenComposite cache (R11–R14): download and validate a build, gate downloads on license acceptance, check upstream for a new build, and accept a pending update | `AppDataPaths`, `SettingsStore`, `HttpDownloader`, `FileHash`, `AtomicFile`, `JsonFile`, `PeReader` |
| `OperationGate` | One operation at a time (R33): patch, restore, the batches and the OpenComposite download/check/accept. Taken and released on the UI thread; the window will not close while a patch, restore or batch holds it | nothing |
| `WarningLog` | The window's operation warnings, each shown once (R35) | nothing |
| `PatchStateStore` (`PatchState.cs`) | `state.json`: one record per patched DLL, keyed by its full path (R4, R20). A corrupt file is kept as `state.json.corrupt-<time>`; when invalid records are skipped, the file as it was is first copied to `state.json.skipped-<time>`; a file that cannot be read is never overwritten | `JsonFile`, `AtomicFile` |
| `RunningGameGuard` | Refuses when a running process's executable is under the install folder (or under a junction's target), or a target DLL cannot be opened for exclusive write (R19); `WindowsProcessImageSource` lists processes with `QueryFullProcessImageName` | P/Invoke |
| `OpenCompositeIni` | The allow-listed `opencomposite.ini` text (R21) | nothing |
| `PatchStatusRules`, `PatchBatch`, `PatchAvailability` | Per-DLL and per-game patch status, which games "Re-patch all" and "Update all" cover, and when Patch and Restore are enabled (R23, R24, R36) | nothing |
| `PatchPlanner` | Read-only with respect to game folders: builds Patch, Restore and Update plans and the per-game statuses (R15–R18, R24, R25, R29) | `OpenCompositeCache`, `PatchStateStore`, `PeReader`, `FileHash` |
| `PatchExecutor` / `PatchService` | Runs a plan's steps behind the guard, stops at the first failure, records each completed DLL with the hash of the file actually written (R16, R30); `ApplyAll` runs a batch; simulation returns before anything runs | `RunningGameGuard`, `PatchStateStore`, `AtomicFile` |

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
  original is replaced. An existing `.bak` that already holds the current DLL is reused, never overwritten.
- A file the rules above cannot place is never discarded: an existing `.bak` that differs from the current DLL is,
  after a question (default No), kept as `openvr_api.dll.bak.old-<UTC time>` (copied, hash-verified, only then
  removed) and the current DLL becomes the new `.bak`; at Restore, a current DLL that is neither the original nor a
  known OpenComposite build is, after a question (default No), kept as `openvr_api.dll.replaced-<UTC time>`.
  A DLL that is already OpenComposite without a record is patched only if a `.bak` with a real original sits next
  to it; otherwise the game is blocked, because an OpenComposite DLL must never become "the original".
  Batches ("Re-patch all", "Update all") never ask: such games are skipped and reported.
- Restore (R18, R36): puts the original back from the `.bak` and verifies it (when Steam already put the original
  back, only the `.bak` is removed; with no `.bak`, only the record is), and only after that deletes the `.bak`
  and an `opencomposite.ini` Game Manager created (an ini it did not create is never touched). A `.bak` that no
  longer matches the recorded original stops the restore. Any known OpenComposite
  build is replaced without a question, since that loses nothing. Restore stays enabled for a patched game that is
  blocked or no longer OpenVR (R36): getting back to the original is always the safer direction.
- Anti-cheat confirmation (R29): when a game's folders could not all be checked for anti-cheat files
  (`UncheckedFolders`, for example an unreadable subfolder or a junction), Patch says so and asks (default No);
  it is read from the verdict, never from warning text. A blocked game is never patched or updated.
- Running game guard: before any write, refuse if a running process's executable is under the game's
  install folder, and refuse if the target DLL cannot be opened for exclusive write. A denied permission is
  detected here before anything is written, and then offers a relaunch as administrator behind a Yes/No box
  (default No, R22); Game Manager never elevates by itself.
- One operation at a time (R33): patch, restore, both batches and the OpenComposite download/check/accept share
  one `OperationGate`. A plan is built inside the gate, so the cached DLL cannot change between planning and
  copying; the cached DLL is validated and hashed again when the plan is built (R32, R34).
- State: per DLL, the SHA-256 of the original and of the installed OpenComposite DLL, stored in
  `%LOCALAPPDATA%\OpenXR-Toolkit-PSVR2\GameManager\state.json` (an app-data path, the only per-user root, resolved
  by `AppDataPaths.ForCurrentUser` from `Program.cs`). The OpenComposite hash stored is that of the file actually
  written (R30). After every scan and every operation each record is checked against the file on disk: the
  original again is "Unpatched by update", a different file is "Changed externally" (a warning, no automatic
  action), a missing DLL is "Changed externally" with the record kept, and a recorded build older than the
  accepted cached one is "Update available". A build still waiting as `openvr_api.dll.new` does not count until
  the user accepts it. Simulation runs no step; loading a corrupt `state.json` may still rename it inside the
  app-data folder (R20), and no simulation writes into a game folder.
- Nothing writes to the registry. Only `PatchExecutor` writes into game folders, and only the steps of a plan; the
  window never writes a file itself.
- Anti-cheat: a game is blocked if the compatibility list marks it `anticheat`, or if its folder contains
  known anti-cheat markers, matched by name ignoring case anywhere under the install folder during the same
  single walk as classification: an `EasyAntiCheat`, `EasyAntiCheat_EOS` or `BattlEye` folder, or an
  `EasyAntiCheat*.exe`/`.sys`/`.dll`, `start_protected_game.exe`, `BEService*.exe` or `BEClient*.dll` file.
  This ships before patching exists (phase 3).
- `opencomposite.ini`: OpenComposite aborts on unknown keys, so the manager only writes keys from a fixed
  allow-list (initially `supersampleRatio`).
- Updating OpenComposite in a patched game replaces only the OpenComposite DLL. The `.bak` file is the game's
  original DLL and is never overwritten by an update. Before replacing, the manager checks that the current
  DLL hash equals the OpenComposite hash it recorded for that game; if not (for example Steam restored the
  original), that DLL is left alone with a note and shows as "Unpatched by update" (or "Changed externally"),
  to be handled by Re-patch all or Restore. "Update all" plans only the verify, copy and verify steps of the
  OpenComposite DLL, so the `.bak` never appears in an update plan.
- `opencomposite.ini` sits next to each patched `openvr_api.dll` (OpenComposite reads it from that folder) as the
  single line `supersampleRatio=<value>`. An ini Game Manager did not create is never overwritten.

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
- Cache (phase 4): `%LOCALAPPDATA%\OpenXR-Toolkit-PSVR2\GameManager\opencomposite\{x64|x86}\openvr_api.dll`, with
  `opencomposite\cache.json` recording each build's SHA-256, download time and source URL. A newer upstream build
  found by the check waits as `openvr_api.dll.new` until the user accepts it. ARM64 is not offered (no upstream
  build). The license acceptance and the last check time are kept in `settings.json` in the same folder.
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
5. **Patch and restore** — planner, executor, simulation mode, running-game guard, state store; Patch and Restore
   buttons for the selected game, the optional `opencomposite.ini`, the result and question dialogs.
6. **Post-update detection** — after every scan, a hash check of each patched DLL (Status column and tooltips),
   "Re-patch all unpatched (N)" for games Steam unpatched, and "Update all (N)" when an accepted OpenComposite
   build is newer than the installed one. The status line says how many games each button covers.
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
  manager runs unelevated and offers to relaunch elevated only if a write is denied; whether that is ever needed
  on a default install is still unverified.
