# Game Manager

Game Manager is a small Windows app that patches your Steam OpenVR games with [OpenComposite](https://gitlab.com/znixian/OpenOVR), and undoes it in one click.

## Why

The OpenXR Toolkit PSVR2 layer only sees OpenXR games. An OpenVR game only reaches the layer if OpenComposite (a replacement `openvr_api.dll`) translates its calls to OpenXR. Doing that by hand means copying the right DLL into every game folder, keeping the original, and redoing it after each Steam update. Game Manager does it safely and remembers what it changed.

## Requirements

- Windows 10 or 11 (uses the .NET Framework 4.7.2 that comes with Windows).
- Steam, with your games installed.
- SteamVR set as the active OpenXR runtime, and the PSVR2 layer installed (see the main README).

## First run

1. Start `GameManager.exe`. It scans your Steam libraries and lists the games.
2. Click **Download OpenComposite**. A notice appears first: OpenComposite is free software under the GPLv3 (or later), written by its own authors. Game Manager downloads the unmodified DLLs (x64 and x86) directly from znix.xyz, and does not bundle them. Source: https://gitlab.com/znixian/OpenOVR. Nothing is downloaded until you click **Accept and download**.

## The game list

| Column | Meaning |
| --- | --- |
| Type | OpenVR (an `openvr_api.dll` was found), OpenXR (probable) or Unknown. Only OpenVR games can be patched. |
| Compatibility | Works / Broken / Untested (from the shipped `compatibility.json`), or "Anti-cheat - blocked". |
| Status | Not patched, Patched, Unpatched by update, Update available or Changed externally. |
| openvr_api.dll | Every copy found, with its architecture (x86, x64 or ARM64) and path inside the game. |

Hover a row for details. Refresh scans again.

Statuses:

- **Patched**: the DLL is the OpenComposite build Game Manager installed.
- **Unpatched by update**: Steam put the game's original DLL back (usually after a game update).
- **Update available**: the game has an older OpenComposite build than the one you accepted.
- **Changed externally**: the DLL is missing or is neither the original nor the build Game Manager installed. Nothing is changed automatically.

## Patch, Restore, Simulation, opencomposite.ini

Select a game and click:

- **Patch**: copies the original `openvr_api.dll` to `openvr_api.dll.bak`, checks that copy, and only then puts the OpenComposite DLL of the matching architecture in its place. OpenComposite must be downloaded first.
- **Restore**: puts the original back from the `.bak`, checks it, and then deletes the `.bak`. It also works for a game that is now blocked.
- **Simulation - do not change files**: shows every step that would happen and changes nothing in the game folder.
- **Write opencomposite.ini**: with this ticked, Patch also writes `opencomposite.ini` next to the DLL, with one line, `supersampleRatio` (0.5 to 2.0). Game Manager only overwrites or deletes an `opencomposite.ini` it created itself and that has not been edited since.

Game Manager refuses to touch a game that is running, and does one operation at a time. Some cases ask a question first (default No), for example when an unknown `.bak` or DLL would be set aside; batches never ask and skip those games instead.

## Anti-cheat

A game is blocked, and cannot be patched, when the compatibility list marks it as anti-cheat or its folder contains known EasyAntiCheat or BattlEye files. Patching a game with anti-cheat can get your account banned.

If some folders of a game could not be checked (an unreadable folder, or a link to another folder), anti-cheat is "not ruled out". Patch then names those folders and asks for confirmation (default No). Only continue if you are sure the game has no anti-cheat.

## After a Steam update, and new OpenComposite builds

- If Steam restores the original DLL, the game shows **Unpatched by update** and a **Re-patch all unpatched (N)** button appears. It patches those games again.
- At startup (at most once every 24 hours, and only if OpenComposite was downloaded before) and with **Check for OpenComposite update**, Game Manager looks for a newer build. A new build waits until you click **Use new OpenComposite build**. Then **Update all (N)** replaces only the OpenComposite DLL in the patched games. The `.bak` is never touched.

## Where things are

Game Manager's data is in `%LOCALAPPDATA%\OpenXR-Toolkit-PSVR2\GameManager`:

- `settings.json`: licence acceptance and last update check.
- `state.json`: one record per patched DLL (hashes of the original and of the installed build).
- `opencomposite\`: the downloaded DLLs and `cache.json`.

Next to a patched game's DLL you may find:

- `openvr_api.dll.bak`: the game's original DLL.
- `openvr_api.dll.bak.old-<time>`: an older, different `.bak` that Patch kept after you confirmed.
- `openvr_api.dll.replaced-<time>`: an unknown DLL that Restore kept after you confirmed.

If Steam's "Verify integrity of game files" is ever needed (for example the `.bak` is missing), Game Manager tells you.

## Administrator rights

Game Manager runs as a normal user. If Windows denies a write (games under "Program Files" sometimes do), it offers to restart as administrator (default No). The elevated copy is started with `--app-data` pointing at your own data folder, so it keeps using your records and OpenComposite cache instead of the administrator account's.

## Known limitations

- Steam games only; no other stores.
- Patches each game, not the whole system.
- OpenComposite compatibility varies by game. `compatibility.json` is a shipped snapshot, not a guarantee; "Untested" means unknown.
- Only x86 and x64 OpenComposite builds exist upstream; there is no ARM64 build.
- Upstream publishes no version number or checksum, so a "new build" means the download differs from the cached one.
- Steam's own "Verify integrity of game files" removes the patch (Game Manager then shows "Unpatched by update").
- The window will not close while a Patch or Restore is running.
