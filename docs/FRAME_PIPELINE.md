# Frame Pipeline

**Status (2026-11-02):** an offline, CPU-only .NET 10 foundation is implemented and targeted to x86. It is an optional standalone module. It has no SFM, DXVK, ReShade, image I/O, native, or NuGet dependencies. The repository still has no installer source/build files; this work does not create or replace an installer.

## Data model and memory

`Frame` owns a managed byte array supplied by its creator. Ownership transfers at construction; callers must not mutate or reuse that array. Consumers see pixels as `ReadOnlyMemory<byte>`. A frame records width/height, component type, channel layout, row stride in bytes, color space, alpha mode, and pass identity. UInt8, UInt16, Float16, and Float32 components are representable. Layouts are Gray, GrayAlpha, RGB, BGR, RGBA, and BGRA. Padding at the end of each row is retained. Validation rejects nonpositive dimensions, arithmetic overflow, short strides/buffers, and inconsistent alpha metadata.

The current model represents one available pass per frame: Color, Depth, Normal, or Auxiliary. A frame only exists when data for that pass exists; the pipeline does not manufacture missing depth/normal data. Depth/normal-specific interpretation and multi-pass grouping are deferred.

## Processing and errors

`IFrameStage.Process(Frame)` receives an immutable-by-contract input and returns a frame. `FramePipeline` validates the input and each result. A stage failure propagates as an exception and leaves earlier input frames intact. Stages that allocate output are responsible for returning owned storage. The sample `InvertRgb8Stage` handles UInt8 RGB/BGR/RGBA/BGRA color frames, inverts color components, preserves alpha and row padding, and rejects other input explicitly.

The executor passes frames by reference between stages and does not copy on its own. The sample stage makes one full buffer copy so it cannot corrupt its input. A single 4K RGBA Float32 frame is about 126.6 MiB; callers should avoid retaining unnecessary intermediate frames. GPU buffers, pooling, concurrency, and zero-copy interop are future integration points, not current behavior.

## Build and checks

Requires .NET 10 SDK. No package restore beyond the SDK/framework is required.

```powershell
dotnet build src/FramePipeline/FramePipeline.csproj -c Release
dotnet run --project samples/FramePipeline.Demo/FramePipeline.Demo.csproj -c Release
dotnet run --project tests/FramePipeline.Tests/FramePipeline.Tests.csproj -c Release
```

The demo runs a synthetic RGBA8 frame through the inversion stage. The self-contained test executable checks output pixels, alpha and padding preservation, input immutability, metadata, malformed dimensions/strides/buffers, and unsupported stage input.

## Image I/O recommendation

PNG I/O is deliberately not included in this milestone. For a later lightweight PNG path, evaluate upstream **libpng** (libpng license, a permissive zlib-style license) or .NET's built-in `System.Drawing` only if a Windows-only limitation is acceptable; keep PNG decoding/encoding outside the core frame model.

For HDR and multilayer EXR, use upstream **OpenEXR** (BSD-3-Clause) through its C++ API when the I/O milestone is reached. It is the format's reference implementation and supports scanline/tiled and multipart/multichannel files. Keep it behind an optional I/O module so the core pipeline remains dependency-free. Before integration, verify the current supported OpenEXR version, x86 Windows build availability, and transitive dependency licenses against the selected upstream release. No EXR writer or library is added here.

## SFM boundary and next step

SFM installation evidence in the existing research documents identifies the examined runtime as x86. This pipeline is therefore built x86 for future compatibility, but it runs offline and is not injected into SFM. It makes no claim about exported buffer formats or GPU resources; existing research documents explicitly leave those details unresolved. Keep future SFM adapters separate from core frame processing and establish exported PNG behavior before relying on viewport evidence.

Next milestone: add PNG read/write in a separate I/O module and validate round-trip dimensions and decoded pixel changes on synthetic frames. Then consider EXR/multilayer output and additional typed stages.
