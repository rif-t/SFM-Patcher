# Modern SFM Patcher

**Current version: v0.1.0**

Modern SFM Patcher is a Windows utility for configuring Source Filmmaker's graphics setup, managing ReShade effects and presets, and developing a future renderer and post-processing extension for SFM.

## What it does today

- Detects a Steam Source Filmmaker installation or lets you choose its game folder.
- Installs and restores selectable x86 ReShade, DXVK, and REST Enhanced components.
- Backs up files before changing them and preserves later user edits during restore.
- Downloads selected ReShade shader packages and installs the Skibidi Kitty preset with its required shader and texture files.
- Checks the current GitHub repository for its latest release and compares it with the running version. The checker reports when no release has been published yet.

The desktop app uses C# 14, .NET 10, and WinUI 3. The app is a self-contained x64 Windows process; SFM and its in-process graphics modules remain x86.

## Build

Build the WinUI app with:

```powershell
dotnet build src/SfmPatcher.App/ModernSfmPatcher.csproj -c Release
```

The optional offline Frame Pipeline is independent of SFM and the UI. Its model, current limits, and build instructions are in [docs/FRAME_PIPELINE.md](docs/FRAME_PIPELINE.md).

## SFM integration research

The longer-term goal is a renderer and post-processing extension integrated with SFM. The current installer does not acquire frames from SFM's rendering pipeline. Research into Movie Maker export, frame acquisition, and auxiliary buffers remains documented separately:

- [Research overview](docs/RESEARCH.md)
- [Runtime trace results](docs/RUNTIME_TRACE_RESULTS.md)
- [Installer implementation and limits](docs/INSTALLER.md)

## Creator

- Discord: `skibidi_kitty`
- YouTube: [rif-t-animation](https://www.youtube.com/@rif-t-animation)
