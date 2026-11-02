# SFM-Patcher

SFM-Patcher is a set of tools for SFM optimization and graphics support. The
repository currently contains reverse-engineering notes and an independent
frame-processing foundation; it does not yet contain the DXVK/ReShade installer
described by the original project summary.

## Frame Pipeline

The optional offline pipeline has no SFM, GPU, or image-library dependency. Build
and run its demonstration and self-contained tests with .NET 10:

```powershell
dotnet build src/FramePipeline/FramePipeline.csproj -c Release
dotnet run --project samples/FramePipeline.Demo/FramePipeline.Demo.csproj -c Release
dotnet run --project tests/FramePipeline.Tests/FramePipeline.Tests.csproj -c Release
```

See [docs/FRAME_PIPELINE.md](docs/FRAME_PIPELINE.md) for its data model, limits,
and the OpenEXR recommendation.

## Installer

The first WinUI 3 installer is in `src/SfmPatcher.App`. It installs the pinned
x86 ReShade 6.8.0 / DXVK 3.1.1 proxy chain into an SFM game folder and provides
a backup restore action. Build it with:

```powershell
dotnet build src/SfmPatcher.App/SfmPatcher.App.csproj -c Release
```

See [docs/INSTALLER.md](docs/INSTALLER.md) for artifact sources, backup
behavior, and current validation limits.
