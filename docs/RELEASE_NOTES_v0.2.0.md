# Modern SFM Patcher v0.2.0

## Added

- Optional `dxvk.conf` import, editing, and installation. Existing configuration is backed up and restored with the graphics setup.
- HUD controls for FPS, DXVK version, GPU/driver details, frame times, draw calls, memory, and other DXVK counters.
- Fixed HUD config generation so it no longer adds a leading comment or blank line to `dxvk.conf`.
- A local diagnostics report with app/runtime details, SFM setup detection, recent application errors, and relevant graphics logs.
- A pre-install review showing the chosen SFM folder, ReShade/DXVK versions, and `dxvk.conf` replacement behavior.
- Backup folder access and clearer recovery information for interrupted installs.
- Preset dependency checking that downloads only required effects missing from both the bundled preset and SFM's installed shader folders.
- Windows 10 API targeting from version 21H2 (build 19044) while retaining a self-contained x64 app package.

## Improved

- Graphics install state is saved before replacing SFM files, and module/configuration copies use staged replacement.
- Restore keeps user-modified files and retains backup state for those files rather than discarding the recovery manifest.
- Install and restore actions check that SFM is closed.
- Updated release version metadata, release ZIP name, and installer documentation.

## Compatibility and limits

- Recipients do not need to install .NET because the release is self-contained.
- SFM and the injected ReShade/DXVK components remain 32-bit; the desktop app runs x64.
- Windows 10 21H2+ is the configured API minimum, but Microsoft's current .NET 10 support matrix limits Windows 10 to Enterprise/LTSC editions. Consumer Windows 10 Home/Pro execution remains best-effort and requires validation on that OS.
- This version does not implement direct renderer integration or depth/normal buffer acquisition. Those remain a separate research track for a later release.
