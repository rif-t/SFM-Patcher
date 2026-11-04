# Modern SFM Patcher installer

**Status (2026-11-03):** Version 0.2.0 of the WinUI 3 installer is in `src/SfmPatcher.App/ModernSfmPatcher.csproj`. It targets C# 14 / .NET 10 and installs selectable x86 ReShade, DXVK, and REST Enhanced releases, plus selected ReShade effects. The app is x64 and self-contained; SFM and graphics DLLs remain x86. The project minimum API target is Windows 10 version 21H2 (build 19044), matching the current Windows App SDK support floor. The current .NET 10 support matrix limits Windows 10 to Enterprise/LTSC editions; consumer Home/Pro systems are best-effort until tested. Windows 10 runtime validation remains outstanding.

The Presets tab currently offers the bundled “Skibidi Kitty” preset. It references free effects listed by ReShade's official shader package catalog and does not bundle Marty McFly's Patreon-only RTGI shader. On install, it reads the preset's technique list, checks bundled and already-installed effects, downloads only missing effects from official ReShade shader packages, then copies the supplied preset, shader files, and textures into the expected `bin` paths. This work happens only after the user selects Install preset. All installed files share the shader backup manifest and can be restored from either the Presets or Shaders tab. The Info tab displays creator links, checks the current GitHub releases page for the app version, and exports a local diagnostics report with SFM paths and recent logs; review that report before sharing it.

## Install layout

```text
game/bin/d3d9.dll          ReShade 6.8.0 D3D9 proxy
game/bin/dxvk/d3d9.dll     DXVK 3.1.1 x86 D3D9 implementation
game/bin/ReShade.ini       Existing profile plus proxy configuration
```

`RESHADE_MODULE_PATH_OVERRIDE` is set for the current Windows user to `game/bin/dxvk`. The installer preserves existing profiles and preset files, copying root-level versions into `bin` only when a `bin` copy is absent. Existing shader search paths are adjusted for the moved config location. Existing files remain backed up under the legacy `%LOCALAPPDATA%\SFM-Patcher\installs` folder so existing installations remain restorable. Restore skips files modified after installation and reports them rather than overwriting later edits. Backup state is recorded before any SFM files are replaced, and writes are staged before replacement to reduce the chance of a partial file. The Graphics tab's uninstall action restores a managed backup; for a recognized external ReShade → DXVK setup, it removes only those verified x86 modules and the matching proxy entries. It leaves a recovery copy of the DLLs and `ReShade.ini` in the local state folder, and does not delete shader or preset files.

## Pinned downloads

| Component | Source | Integrity check |
|---|---|---|
| DXVK | Official GitHub stable release API | SHA-256 from selected release asset metadata |
| ReShade Add-on setup | Official ReShade setup URL constructed from an upstream GitHub tag | Official signing-certificate thumbprint plus extracted version and x86 checks |
| REST Enhanced | Official GitHub stable release API (`release.zip`) | SHA-256 from selected release asset metadata; exactly one x86 `.addon32` is extracted |

Before install/restore or standalone configuration changes, the app checks that the selected SFM process is closed. The Graphics page can import a configuration file, load the current SFM `dxvk.conf`, edit it, and save the config alone without reinstalling ReShade or DXVK. The first standalone save keeps a separate original copy and offers a restore action; saving writes only `game/dxvk.conf`. The optional install checkbox still controls whether a config is included during a graphics setup installation. HUD checkboxes edit the global `dxvk.hud` option while preserving other settings and HUD tokens. Available indicators follow the official DXVK HUD list. If the Windows `DXVK_HUD` environment variable is set, DXVK ignores `dxvk.hud` from the file, so that variable must be cleared for the in-app HUD setting to take effect. Other config settings are passed through as written.

The ReShade Setup executable is run in its official headless mode against a temporary copy of `sfm.exe` to obtain the 32-bit D3D9 proxy. The app then configures that proxy to load DXVK from the adjacent `bin/dxvk` directory. It does not distribute ReShade binaries. The Add-ons tab installs REST Enhanced as `bin/ReshadeEffectShaderToggler.addon32`; it keeps its first pre-existing copy in a separate backup and restores only if the installed file is unchanged.

At startup the app reads Steam's registered install roots and `steamapps/libraryfolders.vdf`, then checks the standard Source Filmmaker install folders for an x86 `sfm.exe`. Valid candidates with the ReShade 6.8.0 D3D9 proxy, x86 DXVK DLL, and matching proxy settings are preferred and shown as an existing setup. Other valid SFM folders are still offered if no configured setup is found.

## Scope and limits

- REST Enhanced is optional and independently installable/restorable from the Add-ons tab. The Shaders tab reads ReShade's official `EffectPackages.ini`, lets users select packages or individual effects, and downloads current package archives only for selected effects. It stages `.fx` effects plus their package `.fxh` support files and textures under `bin/reshade-shaders`, backing up replaced files and providing a restore action. If an effect listed in the package index is absent from the current archive, that effect is skipped and reported while available effects continue installing. The upstream package list specifies moving repository branches rather than pinned commits, so these installs follow the latest branch contents and are not reproducible by version number.
- It does not alter the system-wide ReShade Vulkan layer registration. A machine with an existing global ReShade layer may log that the second ReShade instance aborted; the local D3D9 proxy remains the intended SFM instance.
- The app is unpackaged and self-contained for x64 Windows PCs. SFM and its injected graphics DLLs remain x86.
- The 0.2.0 build and local package script are the release validation target. Install/restore flows have not been exercised against a disposable SFM copy in this pass. DXVK and REST are checked against GitHub-provided asset digests; ReShade setup versions rely on the official signing certificate and version checks because upstream does not publish per-version hashes in GitHub release assets.
- Direct SFM renderer integration and depth/normal acquisition remain research work. The current app does not provide those buffers to ReShade.
