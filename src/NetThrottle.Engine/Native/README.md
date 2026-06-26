# WinDivert native binaries

The engine loads **WinDivert** at runtime. These binaries are **not** committed
to the repository (see `.gitignore`); the release workflow downloads them, and
for local development you place them here yourself.

## Local development

1. Download WinDivert **2.2.x** from
   <https://github.com/basil00/WinDivert/releases> (the `WinDivert-2.2.x-A.zip`).
2. Copy the **x64** files into `native/x64/`:
   - `native/x64/WinDivert.dll`
   - `native/x64/WinDivert64.sys`

The project copies everything under `native/x64/` next to the built executable,
and the app loads `WinDivert.dll` from its own folder. WinDivert needs
administrator rights, so run the app elevated.
