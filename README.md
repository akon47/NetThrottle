# NetThrottle

A Windows desktop app that rate-limits per-process network bandwidth, with separate download and upload caps for each application.

**English** | [한국어](README.ko.md)

## Features

- Per-process bandwidth limits keyed by image name (e.g. `chrome.exe`), so rules survive process restarts.
- Independent **download** and **upload** caps in KB/s, where `0` means unlimited.
- Protocol selection per rule: **TCP**, **UDP**, or **Both**.
- Traffic shaping that **delays** packets to honor the configured average rate instead of dropping them.
- Live up/down throughput shown per rule, sampled once per second.
- Clean WPF UI on .NET 9 with a simple rule grid.
- Installer and portable distributions, with automatic update notifications from GitHub Releases.

## How it works

NetThrottle shapes traffic at the network layer using [WinDivert](https://github.com/basil00/WinDivert). A dedicated pump thread reads every TCP/UDP packet, attributes it to a process, and releases it through a per-(process, direction) token bucket.

The pipeline is:

1. **WinDivert** captures TCP/UDP packets at the network layer (filter `tcp or udp`).
2. The packet's local port is mapped to a **PID** via the Windows IP Helper extended TCP/UDP tables, and the PID resolves to a process image name.
3. Rules are matched by **image name + protocol**.
4. Matching packets pass through a **token bucket** that delays them to enforce the configured rate. Packets are never dropped — they are scheduled and re-sent.

Throughput counters are accumulated for every matched process and differentiated into a live rate by the UI once per second.

## Requirements

- **Windows 10 or 11 (x64).**
- Must run **as Administrator** — WinDivert loads a signed kernel-mode driver.
- `WinDivert.dll` and `WinDivert64.sys` (v2.2.x, x64) must sit next to `NetThrottle.exe`. The installer and portable zip bundle these for you.

## Installation

### Installer (`.exe`)

1. Download `NetThrottle_vX.Y.Z_Setup.exe` from the [latest release](https://github.com/akon47/NetThrottle/releases/latest).
2. Run it and follow the wizard. Administrator elevation is requested automatically.
3. Launch NetThrottle from the Start menu or desktop shortcut.

Installed builds store settings in `%AppData%\NetThrottle\settings.json`.

### Portable (`.zip`)

1. Download `NetThrottle_vX.Y.Z_Portable.zip` from the [latest release](https://github.com/akon47/NetThrottle/releases/latest).
2. Extract the `NetThrottle/` folder anywhere you like.
3. Run `NetThrottle.exe` directly (it will request Administrator elevation).

The portable folder ships a `portable.marker` file next to the executable. When that marker is present, `settings.json` is stored next to the exe, so the whole folder is self-contained and movable.

## Usage

1. Click **Add rule** to create a new entry in the grid.
2. In the **Process (image name)** column, pick a running process or type an image name such as `chrome.exe`. Use **Refresh apps** to update the list.
3. Choose a **Protocol**: TCP, UDP, or Both.
4. Set the **Down KB/s** and **Up KB/s** caps. A value of `0` means unlimited for that direction.
5. Make sure the rule's **On** checkbox is enabled.
6. Press **Start** to begin shaping. The app requires Administrator elevation to open the WinDivert driver.

While running, the **↓ live** and **↑ live** columns show the current measured throughput for each rule, updated once per second. Press **Stop** to release all traffic.

## Updating

On launch, NetThrottle queries the GitHub Releases API for the latest release and compares the tag to its own assembly version. When a newer version is available, a banner appears with **Update now** and **Skip** actions. You can also trigger a check manually with **Check updates**.

- **Installed build:** can self-update — it downloads the `*_Setup.exe` asset, relaunches it elevated, and exits.
- **Portable build:** does not self-install; it links to the releases page for a manual download.

## Building from source

Install the [.NET 9 SDK](https://dotnet.microsoft.com/download), then build the solution:

```bash
dotnet build NetThrottle.sln -c Release
```

To run locally you still need the WinDivert native files present and must launch elevated. For development, drop `WinDivert.dll` and `WinDivert64.sys` (v2.2.x, x64) into:

```
src/NetThrottle.Engine/native/x64/
```

The Engine project copies them to the build output. Alternatively, place the two files beside the built `NetThrottle.exe`.

## Releasing

Releases are tag-driven. Pushing a `vX.Y.Z` tag runs [`.github/workflows/release.yml`](.github/workflows/release.yml), which:

1. Derives the version from the tag.
2. Publishes a self-contained single-file `win-x64` executable (`NetThrottle.exe`).
3. Downloads WinDivert and bundles `WinDivert.dll` + `WinDivert64.sys`.
4. Produces and uploads exactly two assets to the GitHub Release:
   - `NetThrottle_vX.Y.Z_Setup.exe` — NSIS installer.
   - `NetThrottle_vX.Y.Z_Portable.zip` — extract the `NetThrottle/` folder and run `NetThrottle.exe` directly.

```bash
git tag v1.2.3
git push origin v1.2.3
```

## Project structure

| Project | Target | Description |
| --- | --- | --- |
| `src/NetThrottle.Core` | `net9.0` | Models (`ThrottleRule`, `Direction`, `ProtocolKind`), the token-bucket shaper, and settings (`AppSettings`, `SettingsStore`). |
| `src/NetThrottle.Engine` | `net9.0-windows` | WinDivert P/Invoke, IP Helper port→PID mapping, the packet pump (`PacketEngine`), and packet parsing. |
| `src/NetThrottle.App` | `net9.0-windows` (WPF) | MVVM UI, services (`EngineController`, `SettingsService`, `ProcessListProvider`, `GitHubUpdateService`), and the `requireAdministrator` manifest. |

## License

Released under the [MIT License](LICENSE).

Author: Kim, Hwan (akon47@naver.com)
