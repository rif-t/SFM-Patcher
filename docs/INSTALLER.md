# Modern SFM Patcher installer

**Status (2026-11-02):** WinUI 3 installer implementation is in `src/SfmPatcher.App/ModernSfmPatcher.csproj`. It targets C# 14 / .NET 10 and can install selectable x86 ReShade, DXVK, and REST Enhanced releases, plus selected ReShade effects from the official package list. It discovers supported versions from upstream GitHub tags/releases; “Latest” is the newest stable item returned. ReShade’s official setup binary is hosted on reshade.me because upstream does not publish the installer as a GitHub release. The installer validates `sfm.exe`, checks DXVK/REST release asset digests, verifies ReShade’s signing-certificate thumbprint and extracted version/architecture, backs up modified files, and provides restore actions.

The Presets tab currently offers the bundled “Skibidi Kitty” preset. On install, it reads the preset's technique list, downloads any required effects not included in its bundled files from their official ReShade shader packages, then copies the supplied preset, shader files, and textures into the expected `bin` paths. This work happens only after the user selects Install preset. All installed files share the shader backup manifest and can be restored from either the Presets or Shaders tab. The Info tab displays creator links and checks the current GitHub releases page for Modern SFM Patcher's version.

## Install layout

```text
game/bin/d3d9.dll          ReShade 6.8.0 D3D9 proxy
game/bin/dxvk/d3d9.dll     DXVK 3.1.1 x86 D3D9 implementation
game/bin/ReShade.ini       Existing profile plus proxy configuration
```

`RESHADE_MODULE_PATH_OVERRIDE` is set for the current Windows user to `game/bin/dxvk`. The installer preserves existing profiles and preset files, copying root-level versions into `bin` only when a `bin` copy is absent. Existing shader search paths are adjusted for the moved config location. Existing files remain backed up under the legacy `%LOCALAPPDATA%\SFM-Patcher\installs` folder so existing installations remain restorable. Restore skips files modified after installation and reports them rather than overwriting later edits. The Graphics tab's uninstall action restores a managed backup; for a recognized external ReShade → DXVK setup, it removes only those verified x86 modules and the matching proxy entries. It leaves a recovery copy of the DLLs and `ReShade.ini` in the local state folder, and does not delete shader or preset files.

## Pinned downloads

| Component | Source | Integrity check |
|---|---|---|
| DXVK | Official GitHub stable release API | SHA-256 from selected release asset metadata |
| ReShade Add-on setup | Official ReShade setup URL constructed from an upstream GitHub tag | Official signing-certificate thumbprint plus extracted version and x86 checks |
| REST Enhanced | Official GitHub stable release API (`release.zip`) | SHA-256 from selected release asset metadata; exactly one x86 `.addon32` is extracted |

The ReShade Setup executable is run in its official headless mode against a temporary copy of `sfm.exe` to obtain the 32-bit D3D9 proxy. The app then configures that proxy to load DXVK from the adjacent `bin/dxvk` directory. It does not distribute ReShade binaries. The Add-ons tab installs REST Enhanced as `bin/ReshadeEffectShaderToggler.addon32`; it keeps its first pre-existing copy in a separate backup and restores only if the installed file is unchanged.

At startup the app reads Steam's registered install roots and `steamapps/libraryfolders.vdf`, then checks the standard Source Filmmaker install folders for an x86 `sfm.exe`. Valid candidates with the ReShade 6.8.0 D3D9 proxy, x86 DXVK DLL, and matching proxy settings are preferred and shown as an existing setup. Other valid SFM folders are still offered if no configured setup is found.

## Scope and limits

- REST Enhanced is optional and independently installable/restorable from the Add-ons tab. The Shaders tab reads ReShade's official `EffectPackages.ini`, lets users select packages or individual effects, and downloads current package archives only for selected effects. It stages `.fx` effects plus their package `.fxh` support files and textures under `bin/reshade-shaders`, backing up replaced files and providing a restore action. The upstream package list specifies moving repository branches rather than pinned commits, so these installs follow the latest branch contents and are not reproducible by version number.
- It does not alter the system-wide ReShade Vulkan layer registration. A machine with an existing global ReShade layer may log that the second ReShade instance aborted; the local D3D9 proxy remains the intended SFM instance.
- The app is unpackaged and self-contained for x64 Windows PCs. SFM and its injected graphics DLLs remain x86.
- Release build succeeds. Version discovery and install actions have not been exercised against an isolated SFM installation. DXVK and REST are checked against GitHub-provided asset digests; ReShade setup versions beyond the previously pinned 6.8.0 build rely on the official signing certificate and version checks because upstream does not publish per-version hashes in GitHub release assets.
