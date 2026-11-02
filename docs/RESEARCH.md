# Source Filmmaker Movie Maker and rendering pipeline research

**Status:** research only. No hooks, pixel processing, renderer changes, or SFM-Patcher implementation were made. This is a snapshot of public source/docs and one installed x86 SFM build; repeat binary and runtime checks against other SFM builds. Phase 2 added static x86 disassembly, RTTI/vtable recovery and code/data cross-reference checks. Phase 3 reused the Ghidra project for targeted decompilation and callsite analysis. A subsequent WinDbg runtime session confirmed the export-time MovieDoc writer chain, a successful PNG save, and a write into its CPU buffer from a Material System byte-copy routine; source-resource identity and format remain unresolved. See [RUNTIME_TRACE_RESULTS.md](RUNTIME_TRACE_RESULTS.md) and [RUNTIME_TRACE_PLAN.md](RUNTIME_TRACE_PLAN.md).

## Evidence labels and scope

- **VERIFIED** means directly present in inspected source, a binary, official documentation, or a reproducible observation described here.
- **STRONGLY SUPPORTED** means multiple evidence sources support a conclusion while a call-chain edge, identity, or interpretation remains uncertain.
- **HYPOTHESIS** means plausible and testable, but not demonstrated.
- **UNKNOWN** means the evidence currently does not decide the question.

SFM Movie Maker internals are proprietary. The public Valve Source SDK 2013 is an API/source reference and an engine analogue; it is not SFM source. An API declaration or an analogous replay renderer is never treated as proof that SFM calls that API. The installed files examined here are described by relative module names only; no per-user or machine-specific installation paths are part of this report.

## Executive findings

### Final runtime readiness check (2026-11-01)

- **VERIFIED:** the installed `sfm.exe` and checked engine/editor DLLs are x86 (`PE machine 0x014C`). The `ifm.dll`, `engine.dll`, `materialsystem.dll`, `shaderapidx9.dll`, and `video_services.dll` hashes match the binaries used in the prior static inventory. `sfm.exe` hash is recorded separately in [RUNTIME_TRACE_RESULTS.md](RUNTIME_TRACE_RESULTS.md); it was not part of the six-DLL Ghidra fingerprint list.
- **VERIFIED:** WinDbg package `1.2610.1001.0` is available and SFM ran under it. A `Recordmovie (24.00)` export entered `FUN_102B86C0 → FUN_102B8060 → FUN_102B75C0 → FUN_102B6610 → QtGui4!QImage::save`. The save received basename `a3000000.png`, null format and quality `-1`, returned `EAX=1`, and the user confirmed the file exists. A hardware write watch also caught a store to the writer's buffer in `materialsystem.dll+0x44224`; the unwound stack leads through `shaderapidx9.dll` to `FUN_102B6610`. See [RUNTIME_TRACE_RESULTS.md](RUNTIME_TRACE_RESULTS.md).
- **UNKNOWN:** UI-to-frame-controller edge, source resource, GPU readback method/synchronization, source-buffer format/layout, and whether the target is the swapchain backbuffer remain unresolved. Deeper stack edges carry WinDbg unwind warnings. The recommended prototype boundary remains offline processing of a copied exported frame; no prototype is included here.
- See [RUNTIME_TRACE_PLAN.md](RUNTIME_TRACE_PLAN.md) for the reproducible trace and [RUNTIME_TRACE_RESULTS.md](RUNTIME_TRACE_RESULTS.md) for the bounded environment results and integration-point assessment.

1. **VERIFIED:** the examined SFM installation is 32-bit. Its `game/bin/tools/ifm.dll` is a large Qt/Python editor module exporting `CreateInterface` and `init_movieobjects`. Static strings identify Movie Maker / “layoff” UI, export settings, frame sequence controls, separate WAV audio, image sequence and movie output modes, and post-export command templates.
2. **VERIFIED:** `ifm.dll` contains profiler-scope labels `CQViewportArea::OnRenderFinished` and `ReadPixelsFromLDRResultTexture`; disassembly shows the viewport code block allocates a pixel buffer and calls a read-like helper. **STRONGLY SUPPORTED:** the helper performs a Material-System-style readback. This is viewport-side evidence and does not establish Movie Maker use.
3. **VERIFIED at runtime:** during `Recordmovie`, the nested `ifm.dll` chain `FUN_102b86c0` → `FUN_102b8060` → `FUN_102b75c0` → `FUN_102b6610` reached `QtGui4!QImage::save`; a traced call saved basename `a3000000.png` and returned success. The UI action-to-frame entry remains untraced.
4. **VERIFIED in part:** `FUN_102b6610`'s `this+0x234` field held a readable CPU pointer. An export-time interface call at `ifm.dll+0x2B67BC` passed that buffer to vtable slot `+0x194`; a write watch caught `materialsystem.dll+0x44224` writing it through a runtime stack path in `shaderapidx9.dll`. The Material System loop reads four bytes per source unit and writes three bytes per destination unit. This is **STRONGLY SUPPORTED** as an interface-mediated pixel transfer/readback path. **UNKNOWN:** exact private method/ABI, source resource and GPU operation/synchronization, semantic channel layout and whether the target is the swapchain backbuffer. The repeated output file was confirmed to exist but not independently decoded or inspected.
5. **VERIFIED:** Source SDK 2013 declares Material System readback and target interfaces. Its replay renderer has an explicit `ReadPixels` layoff example. These are useful analogues only; this SDK does not contain `CQMovieLayoff...` or SFM Movie Maker implementation.
6. **VERIFIED:** D3D9 `GetRenderTargetData` copies a render target to a system-memory surface and has strict size/format/non-multisample requirements. DXVK's D3D9 front-buffer capture implementation records Vulkan image-to-buffer work and marks the destination for readback. Neither fact establishes that SFM calls either API during export.
7. **STRONGLY SUPPORTED:** a ReShade callback tied to a swapchain/presentation runtime can process presented GPU render targets, but that does not establish access to an off-screen Movie Maker export target or to a CPU image buffer. Export compatibility must be tested against the actual target.

## Method and inspected materials

- Inspected the local SFM executable and relevant PE modules: `sfm.exe`, `game/bin/engine.dll`, `materialsystem.dll`, `shaderapidx9.dll`, `video_services.dll`, `valve_avi.dll`, `scenefilecache.dll`, `tier0.dll`, `vstdlib.dll`, `game/bin/tools/ifm.dll`, and the local DXVK `d3d9.dll`.
- Parsed PE machine type, export names, module imports, version metadata/strings, and printable strings. The inspected engine and editor modules above report PE machine `0x014c` (x86). Static strings are evidence of embedded labels or code/data references, not proof of an executed call.
- Searched the official Valve `source-sdk-2013` source checkout. It has no exact SFM symbols such as `CQViewportArea`, `CQMovieLayoff`, or `OnExportMovie`.
- Reviewed official Microsoft D3D9 docs, Khronos Vulkan specification/reference pages, DXVK implementation, ReShade API references, and used Context7 to locate/check Vulkan and ReShade API documentation.
- Phase 2 used Visual Studio `dumpbin` and temporary Capstone x86 disassembly to inspect call sites, PE imports/exports, RTTI, class hierarchies, vtable pointers, and direct string/IAT references. This was disassembly and metadata analysis, not decompilation. The CodeView record names an `ifm.pdb` with GUID/age, but that PDB was not present in the installation; COFF symbols are stripped. REA native PE analysis was not available. No controlled SFM export trace or test project was available.

## Phase 2 binary-analysis update

- **VERIFIED:** `ifm.dll` exports `CreateInterface` at RVA `0x980C20` and `init_movieobjects` at RVA `0x965720`; it does not export `OnExportMovie` or `ExportMovie`. `OnExportMovie()` and related strings do not have direct absolute-pointer references found by the static cross-reference scan. They are not evidence of a called function by themselves.
- **VERIFIED:** RTTI identifies `CQLayoffMovieDialog`, `CQtMovieLayoffProgress`, `CQMovieLayoffSettingsDialog`, `CTempNeuteredMovieLayoffProgress`, `CPosterLayoff`, `CQViewportArea`, and `CMovieDoc`. The exact binary RTTI name `.?AVCQMovieLayoffProgress@@` was not found; `CQMovieLayoffProgress` occurs as a separate embedded string and may be Qt metadata.
- **VERIFIED:** a `CQViewportArea` code block at RVA `0x1A1B10` contains profiler labels for `CQViewportArea::OnRenderFinished` and `ReadPixelsFromLDRResultTexture`. In the nested scope it allocates `width * height * 4` bytes and calls an internal helper at RVA `0x27E000` with target, rectangle, buffer and format-like arguments. That helper forwards six stack arguments to a virtual method. **STRONGLY SUPPORTED:** this is a Material-System-style pixel read for the viewport; the exact private interface and method identity remain unconfirmed. No Movie Maker-to-viewport edge was found.
- **VERIFIED:** `ifm.dll` imports Qt `QProcess` launch/wait/read APIs, `QImage::save`, and Windows file APIs. There are direct call sites for process launch at RVA `0x113015`, process start at `0x113241`, wait at `0x11324E`, and output read at `0x11325A`. A movie-command template and labels are embedded nearby in the same module. **STRONGLY SUPPORTED:** the editor supports external command execution, plausibly including movie assembly. The exact export call path is still not tied to those sites.
- **Phase 2 status:** **UNKNOWN** then; Phase 3 below now directly links `FUN_102b6610` to `QImage::save` from a MovieDoc-labelled frame-output chain. The menu action link and execution during an actual export remain unverified.
- **VERIFIED:** RTTI yields Qt dialog and VGUI progress class hierarchies and vtable addresses. RVAs, code-reference locations, import call sites, and module dependency findings are recorded in [BINARY_ANALYSIS.md](BINARY_ANALYSIS.md). Evidence-labeled flow is in [MOVIE_MAKER_CALL_GRAPH.md](MOVIE_MAKER_CALL_GRAPH.md).

The most valuable next step is to verify the current WinDbg breakpoint state and capture a call stack from the existing one-frame export. If no hit was captured, repeat that export with the four hardware breakpoints visibly enabled, then correlate the exact buffer to the already verified PNG without changing the installation.

## Movie Maker modules, UI, settings, and responsibilities

### Local binary evidence

| Evidence | Classification | Interpretation / limit |
|---|---|---|
| `game/bin/tools/ifm.dll` exports `CreateInterface`, `init_movieobjects`; Qt4, Python 2.7, Steam API, `tier0`, and `vstdlib` dependencies | VERIFIED | The SFM editor/tool module is a plausible owner of Movie Maker UI and integration. Export responsibilities are further supported by strings below. Exports do not reveal internal class vtables or call sequences. |
| `ifm.dll` strings: `CQMovieLayoffProgress`, `CQLayoffMovieDialog`, `CQMovieLayoffSettingsDialog`, `CQtMovieLayoffProgress`, `CQPosterLayoff`, `LayoffMovieDialog`, `LayoffMovieExtended`, `LayoffMovie`, `OnExportMovie()`, `OnExportMovieSettings()`, `OnExportRenderSettings()`, `ExportMovieSettings`, `ExportRenderSettings`, `ExportMovie` | VERIFIED | Embedded symbols/UI identifiers name export and progress components. C++ class-like names are not complete interface specifications. |
| Settings strings include `Image Sequence`, `Movie`, `Half Width (Movie only)`, `Render Only Selected Shots`, `Clear Decals At Start Of Layoff`, `Show Shot ID Overlay`, `Depth Of Field`, `DepthOfFieldSamples`, `SequenceStart`, `SequenceEnd`, `SequenceDuration`, `SFMLayoffRenders` | VERIFIED | Export configuration includes output mode, selected-shot behavior, render/DOF choices, and sequence timing fields. Exact units, defaults, persistence format, validation, and effect on render scheduling remain UNKNOWN. |
| Audio strings include `Audio`, `Separate WAV file`, `LayoffSeparateWav`, `LayoffAudio` | VERIFIED | The UI/configuration has an audio/separate WAV concept. Whether audio is captured synchronously, rendered in a later pass, or delegated to another subsystem is UNKNOWN. |
| Movie command template text contains image2 input `%9d.png`, MPEG-4 codec, aspect/rate/size/bitrate fields and an output path placeholder; labels include `Create Movie Command` and `After Movie Command` | VERIFIED | The binary embeds configurable command templates consistent with image sequence to movie assembly and post-command processing. A string template alone does not prove the default is executed, identify the executable, or establish that every “Movie” export uses it. |
| `engine.dll` strings include `startmovie`, `endmovie`, and “Start recording movie frames” | VERIFIED | Source engine has a legacy movie-recording console path. This must not be conflated with SFM editor Movie Maker / layoff. |
| `video_services.dll` and `valve_avi.dll` exist and export `CreateInterface`; latter imports AVI/Bink-related modules | VERIFIED | Candidate video/audio container services exist in this build. Their use by SFM Movie Maker is UNKNOWN. |

### Architecture reconstruction

```text
SFM editor / ifm.dll (export dialogs, settings, progress)
      │ requests a frame/time and output work (exact interface UNKNOWN)
      ▼
Source engine + Material System + D3D9 shader API
      │ frame evaluation / render passes (exact export route UNKNOWN)
      ├── viewport route: CQViewportArea ... ReadPixelsFromLDRResultTexture [named string]
      └── Movie Maker route: [target, readback and completion path UNKNOWN]
                 │
                 ├── image sequence file(s) [strongly supported by mode/settings/template]
                 ├── optional separate WAV [setting exists; production path unknown]
                 └── movie/container/after-command stage [template exists; execution unknown]
```

**Movie Maker entry point:** **VERIFIED** UI identifiers include `OnExportMovie()` and `ExportMovie`; the actual callable entry point and arguments are not recovered. **UNKNOWN** whether the editor calls an engine layoff interface, a material render callback, a queued job, or another module.

**Frames and timing:** **VERIFIED** the binary contains `SequenceStart`, `SequenceEnd`, and `SequenceDuration`, and UI concepts for frame range / selected shots. **UNKNOWN** whether range endpoints are inclusive, how time is quantized to frame rate, how shot transitions and subframe evaluation are scheduled, and whether frame generation blocks the UI thread. A string in `ifm.dll` describes a routine that “renders one frame” and processes Qt events until an `InteractiveRenderFinished` signal; this is a suggestive editor rendering description, not verified Movie Maker control flow.

**Camera and shot evaluation:** **UNKNOWN** exact sequence. SFM's editor has shot/camera concepts, and “Render Only Selected Shots” is present as a setting. There is no public Movie Maker source or runtime trace tying a particular camera evaluation API to the export.

**Synchronous vs asynchronous:** **UNKNOWN** for Movie Maker. A progress dialog and callback/signal identifiers are compatible with asynchronous frame completion, but do not prove a worker thread or asynchronous GPU/CPU readback. Vulkan work is inherently submitted asynchronously, while a CPU consumer may block on a fence/readback; this general fact does not resolve SFM scheduling.

**Audio/video separation:** **STRONGLY SUPPORTED** by the separate WAV setting plus the image-sequence/mux template. **UNKNOWN** whether audio is emitted during or after video rendering, which audio timeline is mixed, or whether muxing uses an external process, AVI/Video Services interface, or another encoder.

## Frame lifecycle: what can and cannot be reconstructed

| Stage | Evidence and classification |
|---|---|
| 1. Frame/time evaluation | **VERIFIED:** sequence start/end/duration setting labels exist. **UNKNOWN:** exact frame clock, interval inclusion, time-step, and deterministic evaluation contract. |
| 2. Animation and scene evaluation | **HYPOTHESIS:** the engine evaluates scene/animation at each requested time as part of frame rendering. No SFM export call trace/source confirms order or callbacks. |
| 3. Shot/camera selection | **VERIFIED:** selected-shots export option exists. **UNKNOWN:** active shot/camera resolution and transition evaluation sequence. |
| 4. Visibility and scene preparation | **HYPOTHESIS:** normal Source visibility, renderable enumeration and scene setup are used. Not confirmed specifically for Movie Maker. |
| 5. Material and lighting evaluation | **HYPOTHESIS:** Source Material System and shader API render the scene. Local modules are present, but this does not establish the export target or render-pass graph. |
| 6. GPU rendering | **STRONGLY SUPPORTED:** SFM installation uses Source engine D3D9 modules and the examined runtime has a DXVK D3D9-to-Vulkan translation setup. Export-time execution is untraced. |
| 7. Post-processing | **UNKNOWN:** DOF settings and LDR-result viewport string exist, but order, inclusion, gamma/tonemap, overlays, and whether export matches viewport are not confirmed. |
| 8. Target resolution and format | **VERIFIED:** movie settings expose a half-width option; movie command template names 1280×720 as a text default. **UNKNOWN:** actual selected render dimensions, texture format, channel order, color space, alpha, MSAA, and whether the same target dimensions are used before encoding. |
| 9. GPU-to-CPU transfer | **STRONGLY SUPPORTED for color:** Movie Maker calls unnamed interface slot `+0x194` with its CPU destination buffer; the export-time stack enters `shaderapidx9.dll` and a Material System routine that converts four-byte source units into three-byte destination units. The exact GPU resource/API/synchronization remain unknown. **STRONGLY SUPPORTED for optional depth:** separate unnamed interface slot evidence precedes PFM-style output; exact operation/resource is unknown. |
| 10. Encoding and disk writing | **VERIFIED:** the MovieDoc-labelled frame writer formats numbered paths and calls `QImage::save` in selected `videoTarget` branches; other branches write other image forms. **UNKNOWN:** enum-to-UI-mode mapping, exact encoder/format selection, temporary-file/atomicity behavior, and whether every movie mode uses PNG intermediates. |

### Viewport vs export divergence

**VERIFIED:** `ifm.dll` emits profiler labels for `CQViewportArea::OnRenderFinished` and its nested `ReadPixelsFromLDRResultTexture` block; disassembly shows a width×height×4 buffer allocation and a read-like internal helper call. **STRONGLY SUPPORTED:** this is a viewport Material-System-style readback. The export identifiers and settings coexist in `ifm.dll`, but no reference or trace links Movie Maker to this block. **UNKNOWN:** whether export renders into a dedicated off-screen target, a viewport target, a backbuffer, or a per-shot target. Therefore a ReShade present callback or screen capture must not be assumed to see exported pixels.

## Material System and interface availability

### What the public SDK declares

The official Source SDK 2013 `IMatRenderContext` interface declares APIs including `GetRenderTarget`, `GetRenderTargetDimensions`, `ReadPixels`, `ReadPixelsAndStretch`, `CopyRenderTargetToTexture`, and `GetFrameBufferCopyTexture`. Related headers declare target creation/access APIs and shader API interfaces. These establish SDK-level capabilities, not SFM Movie Maker calls or matching ABI.

The SDK replay renderer provides a source-engine analogue: `CReplayRenderer::LayoffFrame` obtains a Material System render context, calls `ReadPixels(..., IMAGE_FORMAT_BGRA8888)` for the configured output dimensions, then writes a TGA buffer or passes it to its movie-maker abstraction. It also contains a commented `ReadPixelsAndStretch` alternative. **VERIFIED as SDK replay-renderer behavior only; not SFM Movie Maker behavior.** Other SDK `ReadPixels` uses include screenshots and view/post-process code.

### Installed SFM binary evidence and limits

| Item | Classification | Finding |
|---|---|---|
| `materialsystem.dll`, `shaderapidx9.dll` present; Material System binary exports `CreateInterface` | VERIFIED | Material System and D3D9 shader implementation modules exist. |
| Strings such as `VMaterialSystem080`, `VMaterialSystemConfig004`, `ShaderApi030`, `VMaterialSystem2_001`, `RenderDevice001` appear in module data | VERIFIED | Candidate interface/version identifiers are embedded in this build. Strings do not prove that a factory returns each interface, that SFM queries it, or that an SDK vtable layout matches. |
| `IMaterialSystem` / `IMatRenderContext` actual interface returned by `CreateInterface`, version queried by SFM, method implementations and vtable offsets | UNKNOWN | No live factory probing, debugger trace, matching private headers, or ABI validation was performed. |
| `IShaderAPI` availability to SFM Movie Maker | UNKNOWN | Module/interface strings are not enough to establish acquisition or use by the editor. |
| `ReadPixels`, `ReadPixelsAndStretch`, `GetRenderTarget`, `GetRenderTargetEx` called during Movie Maker export | UNKNOWN | No SFM callsite or runtime export trace. |
| Depth target creation/access and meaningful depth export | UNKNOWN | An export UI string or target API would not establish usable depth semantics. Projection, resolve, encoding and correspondence must be measured. |
| Context synchronization semantics at export readback | UNKNOWN | Must identify exact call/target, then trace lock, flush, fence or blocking behavior in the active backend. |

The public SDK can guide a future observer only after verifying the installed SFM interface version and x86 ABI. Do not use an SDK vtable offset as a hook address or infer a call from declaration names.

## Direct3D 9 readback and DXVK translation

### D3D9 API semantics

- `GetRenderTargetData` copies a device-memory render target into a system-memory surface. Microsoft documents that the destination must be system-memory, the source a default-pool render target, and dimensions/formats must match; it fails for a multisampled source. **VERIFIED API contract**, not a claim that SFM uses it. [Microsoft documentation](https://learn.microsoft.com/en-us/windows/win32/api/d3d9/nf-d3d9-idirect3ddevice9-getrendertargetdata)
- `GetFrontBufferData` copies the swapchain/display front buffer to system memory, with screen-sized constraints in windowed mode and a standard 32-bit output conversion. Microsoft describes it as very slow. It captures display-front-buffer semantics, not an arbitrary off-screen movie output. [Microsoft documentation](https://learn.microsoft.com/en-us/windows/win32/api/d3d9/nf-d3d9-idirect3ddevice9-getfrontbufferdata)
- `GetRenderTarget` returns a surface reference, while `GetBackBuffer` returns a swapchain backbuffer; neither API alone transfers pixels to CPU memory. `LockRect` exposes lockable surface memory and pitch; a normal default-pool render target is not made CPU-readable merely by obtaining its surface. These calls must be interpreted as part of a sequence, not as proof of frame export.
- Multisampled targets require a resolve/copy to a compatible non-multisampled surface before `GetRenderTargetData` under the documented D3D9 contract. Resolve operations and format conversions need separate tracing; an observed blit is not itself proof of CPU readback.
- `Present`/swapchain operations are presentation clues only. A discard swap effect can make backbuffer contents unavailable after presentation; post-present capture is not a general export strategy.

### DXVK source behavior

**VERIFIED:** the public DXVK D3D9 swapchain implementation has `GetFrontBufferData`. It locks/serializes device access, handles front/backbuffer image copies and multisample/format cases, records an internal `copyImageToBuffer`, marks the destination texture subresource as needing readback, and tracks a mapping sequence number. This demonstrates one D3D9 API-to-Vulkan transfer implementation in DXVK. [DXVK source](https://github.com/doitsujin/dxvk/blob/master/src/d3d9/d3d9_swapchain.cpp)

The reviewed implementation evidence is strongest for `GetFrontBufferData`. The location and precise current implementation of `IDirect3DDevice9::GetRenderTargetData` were not established from the reviewed source file in this research pass. Do not infer that it is implemented identically or that SFM uses it. Installed DXVK binary strings indicate front-buffer/readback function code is present, not that it ran during Movie Maker export.

### Interpreting an observed event

| Observed API / operation | What it supports | What it does not prove |
|---|---|---|
| D3D9 `GetRenderTarget` / `GetBackBuffer` | Surface/backbuffer reference access | CPU readback, export use, or final-frame identity |
| D3D9 `StretchRect` / resolve-like copy | GPU surface transfer/resolve/scaling candidate | CPU readback or correct export stage |
| `GetRenderTargetData` | An explicit D3D9 render-target-to-system-memory request | That destination is consumed by PNG/movie writer; source is final SFM frame until correlated |
| `GetFrontBufferData` | Screen/frontbuffer capture request | Offscreen export image; SFM Movie Maker use |
| `LockRect` / `UnlockRect` | Surface access/lock lifecycle | That data is final or newly rendered without tracking resource, lock flags, pitch and preceding copy |
| Vulkan `vkCmdCopyImageToBuffer` | GPU image-to-buffer transfer candidate | Export identity without tracking image, dimensions, copy region, submission completion and CPU consumer |
| `vkCmdBlitImage` | GPU image-to-image blit/scale/format-conversion candidate | CPU transfer or output writer consumption |
| `vkMapMemory` | Host mapping of an allocation | Completion or visibility of prior GPU writes |
| fence wait + noncoherent invalidate + map read | Synchronized host read path candidate | Export identity unless correlated with file output |
| `vkQueuePresentKHR` | A presentation submission | Movie Maker export frame identity, especially for off-screen rendering |

## Vulkan synchronization and readback requirements

**VERIFIED:** `vkCmdCopyImageToBuffer` records an image-to-buffer copy using an image layout and `VkBufferImageCopy` region(s). The source image must have appropriate transfer usage/layout and the destination buffer appropriate transfer-destination usage. A copy command alone does not mean the CPU can immediately read its bytes. [Khronos reference](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdCopyImageToBuffer.html)

For a valid device-to-host path, the producer must establish the required image layout/access dependency, record the copy, submit it, and synchronize host access with completion (commonly a fence wait). `vkMapMemory` only maps an allocation; mapping does not wait for the GPU. If memory is non-coherent, invalidate the mapped range after device completion before host reads. Coherent memory removes that cache invalidate requirement, not the execution dependency. [Vulkan memory specification](https://docs.vulkan.org/spec/latest/chapters/memory.html) [Vulkan map reference](https://docs.vulkan.org/refpages/latest/refpages/source/vkMapMemory.html)

`vkCmdBlitImage` is image-to-image work (often scaling/filtering/format conversion), not CPU readback. Barriers/layout transitions, command-buffer submission, fences/semaphores, mapping and invalidation need to be traced together. `vkQueuePresentKHR` presents a swapchain image; it is not a generic export callback.

## ReShade Vulkan layer and Add-on API boundary

**VERIFIED from official ReShade API/source docs:** ReShade's API is organized around device, command list, resource/view, swapchain and effect runtime objects. It observes/abstracts the graphics API loaded into, and exposes addon events including swapchain/resource/command operations and ReShade effect events. Its effect runtime is associated with a swapchain in ordinary usage; `render_effects` records effect work for specified render target views/command context. [ReShade API reference](https://github.com/crosire/reshade/blob/main/REFERENCE.md) [API declarations](https://github.com/crosire/reshade/blob/main/include/reshade_api.hpp)

**Implication:** effects shown in the SFM viewport via the ReShade runtime do not automatically run on an unrelated off-screen Movie Maker target. An add-on may observe resources/commands when those pass through ReShade and can schedule GPU work when it has valid resource views, command list and state. It does not follow that ReShade accepts an arbitrary CPU-side PNG buffer as an effect input or that every private engine target is tracked/exposed. Exact target compatibility is UNKNOWN until export-time resource tracking proves the target is visible and writable at the right stage.

ReShade screenshot capture is a screenshot facility tied to a runtime/backbuffer, not proof of Movie Maker export capture. Off-screen render passes may be observed as resource/command events but may not be presented or have a runtime. ReShade's presence as a Vulkan layer also does not prove it sees or preserves all DXVK internal image lifetimes in the way a future processor needs; that requires a controlled compatibility test.

## Readback strategy comparison (research only)

| Candidate observation point | Strength | Main uncertainty / risk |
|---|---|---|
| Source Material System call | Higher semantic context (render target, dimensions, destination buffer) if Movie Maker calls it | Interface version/ABI, actual call not established; private SFM route may differ |
| D3D9 readback API under DXVK | Direct signal that D3D9 requests CPU-readable output | Must correlate surface to exported frame; only catches those API paths, not alternate Material System/backend routes |
| Vulkan copy/map/sync tracking | Broad view of DXVK GPU transfers and synchronization | Hard resource-identity/lifetime/alias tracking; many ordinary upload/readback operations create noise |
| ReShade add-on/runtime | Natural place for GPU effects on tracked compatible targets | Swapchain/post-present orientation may miss offscreen Movie Maker output; target scheduling/state requirements |
| Post-export image processing | Operates on the actual file bytes and avoids GPU/API assumptions | Adds a second pass and requires safe file-completion/order/error handling; not part of current implementation |

No option is selected for implementation. The decisive evidence is a controlled export with a known scene, a known single-frame or short sequence, runtime call/resource trace, and correlation to the file dimensions and pixel values.

## Evidence gaps and experiments needed

1. Obtain or create a small SFM project with a deterministic camera, one selected frame, known resolution and simple color patches. Export a single frame and a short sequence with audio off, then repeat with audio on.
2. Record module load/version/interface-factory results and trace Movie Maker UI activation, frame time, camera/shot evaluation, render-target creation/binding, readback, synchronization, encoder calls and file writes. Preserve x86 calling convention and avoid assuming SDK offsets.
3. Capture idle viewport, moving viewport, one-frame export, image-sequence export and movie export. Compare call counts/resources and correlate exact file timestamp, dimensions, pixel format and decoded pixels.
4. Trace both engine-level readback and DXVK/Vulkan transfers. Track image metadata, views, resource aliases, layout transitions, copy extents, destination buffers, queue submits, fences, map/invalidate/unmap. Treat any one API call as a candidate until correlated with the file.
5. For a ReShade compatibility test, identify whether the export target has a ReShade resource view and an open valid command context before the final readback. Verify whether the effect appears in saved output, not merely the viewport.
6. Verify audio timing/mux separately; a configured WAV or command template is not confirmation of generated audio or final movie codec.

## Sources (official/primary unless identified as the local artifact)

- [Valve Source SDK 2013 repository](https://github.com/ValveSoftware/source-sdk-2013), especially [`imaterialsystem.h`](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/public/materialsystem/imaterialsystem.h) and SDK replay layoff code under `src/game/client/replay/replayrenderer.cpp`.
- [Microsoft `GetRenderTargetData`](https://learn.microsoft.com/en-us/windows/win32/api/d3d9/nf-d3d9-idirect3ddevice9-getrendertargetdata), [`GetFrontBufferData`](https://learn.microsoft.com/en-us/windows/win32/api/d3d9/nf-d3d9-idirect3ddevice9-getfrontbufferdata), [`GetBackBuffer`](https://learn.microsoft.com/en-us/windows/win32/api/d3d9/nf-d3d9-idirect3dswapchain9-getbackbuffer), [`LockRect`](https://learn.microsoft.com/en-us/windows/win32/api/d3d9/nf-d3d9-idirect3dsurface9-lockrect), [`UnlockRect`](https://learn.microsoft.com/en-us/windows/win32/api/d3d9/nf-d3d9-idirect3dsurface9-unlockrect), and [D3D9 swap effects](https://learn.microsoft.com/en-us/windows/win32/direct3d9/d3dswapeffect).
- [DXVK upstream D3D9 swapchain implementation](https://github.com/doitsujin/dxvk/blob/master/src/d3d9/d3d9_swapchain.cpp) and [DXVK repository](https://github.com/doitsujin/dxvk).
- [Khronos `vkCmdCopyImageToBuffer`](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdCopyImageToBuffer.html), [`vkCmdBlitImage`](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdBlitImage.html), [`vkMapMemory`](https://docs.vulkan.org/refpages/latest/refpages/source/vkMapMemory.html), and [memory specification](https://docs.vulkan.org/spec/latest/chapters/memory.html).
- [ReShade upstream API reference](https://github.com/crosire/reshade/blob/main/REFERENCE.md), [API declarations](https://github.com/crosire/reshade/blob/main/include/reshade_api.hpp), and [repository](https://github.com/crosire/reshade).
- [Source Filmmaker official FAQ](https://www.sourcefilmmaker.com/faq/) for high-level product description only; it does not document Movie Maker internals.

## Phase 3 binary follow-up (2026-11-01)

The existing Ghidra project was reopened through its configured read-only MCP connection. The active `ifm.dll` program was verified as x86 PE at image base `0x10000000`, SHA-256 `12504c248047e181d31a8500fb6693d1a2d3c805e841fc2227e41aa4e2d2b47e`. All six imported DLL hashes were recomputed and matched [REVERSE_ENGINEERING_SETUP.md](REVERSE_ENGINEERING_SETUP.md). Program switching was also verified for `materialsystem.dll`, `shaderapidx9.dll`, and `engine.dll`; no binaries were re-imported and no project annotations were changed. Detailed findings and callsites are in [MOVIE_MAKER_STATIC_ANALYSIS.md](MOVIE_MAKER_STATIC_ANALYSIS.md); the graph is updated in [MOVIE_MAKER_CALL_GRAPH.md](MOVIE_MAKER_CALL_GRAPH.md).

### New call-flow evidence

- **VERIFIED:** in `ifm.dll`, `FUN_102b86c0` (RVA `0x2B86C0`) contains the profiler label `CMovieDoc::RenderFrameBegin` and conditionally calls `FUN_102b8060` at VA `0x102B874D`. `FUN_102b8060` reads `frameRate`, advances time/frame state, consults engine interface methods, and calls `FUN_102b75c0` at `0x102B8447` / `0x102B8453` when `videoTarget` is enabled.
- **VERIFIED:** `FUN_102b75c0` calls `FUN_102b6610` at `0x102B7A8F`. `FUN_102b6610` formats numbered frame names with `"%s%06d%s.%s"`, wraps the CPU pointer at object offset `+0x234` in a `QImage`, and calls `QTGUI4.DLL::QImage::save` at `0x102B6F0A` for observed `videoTarget` values `5` or `8`. Other branches call additional image writers or emit `PF`/`Pf` float-image data.
- **STRONGLY SUPPORTED:** this is a MovieDoc/layoff frame-output path because the code combines the `CMovieDoc::RenderFrameBegin` profiler label, `videoTarget`, `frameRate`, frame counters, `writeDepth`, layoff cleanup, and per-frame writers. The static direct edges are established; their execution from the Movie Maker menu action is not.
- **UNKNOWN:** no recovered call or Qt meta-object connection links `OnExportMovie()` to `FUN_102b86c0`. The string is present but Ghidra returned no direct xrefs. The three dialog name helpers are executable Qt `qt_metacast` routines, not export handlers.
- **VERIFIED:** `FUN_102b5190` calls `FUN_102a28d0` with `MoviePostRenderCommand`/`MoviePostRenderArguments` and `MovieAfterRenderCommand`/`MovieAfterRenderArguments`. The helper uses `QProcess` start/wait/read APIs. This establishes command execution capability in the cleanup/post-render code, not the exact template, executable, or runtime use for a particular export.
- **STRONGLY SUPPORTED:** color-buffer transfer into the CPU buffer at `this + 0x234`; export-time WinDbg tracing followed the Movie Maker interface call through `shaderapidx9.dll` and `materialsystem.dll` to a 4-byte-source/3-byte-destination copy loop. Isolated static analysis now verifies `materialsystem.dll+0x0B5E0 → +0x48EC0`, and that the generic converter's selector lookup can dispatch to `+0x44200`. Combined with the runtime write-watch at `+0x44224`, this makes `0x48EC0` participation in the Movie Maker transfer **STRONGLY SUPPORTED**. The source was a stack pointer with observed 1280×720 bounds and sample bytes, but the source GPU resource, exact API, runtime selector branch, format semantics, and synchronization remain **UNKNOWN**. The optional `writeDepth` branch passes the buffer and rectangle-like arguments through an unnamed interface slot; that is **STRONGLY SUPPORTED** as a buffer transfer, but its interface/method and GPU resource are also unknown. Do not identify either path as `ReadPixels` from the SDK signature alone.
- **UNKNOWN:** audio/WAV processing and whether the viewport readback (`FUN_101a1b10` → `FUN_1027e000`) is shared. No static edge joins the viewport path to the MovieDoc output functions.

Material System, ShaderAPI and engine checks found interface/version strings and indirect calls but no named `ReadPixels`/`GetRenderTargetData` implementation tied to the frame-output path. `shaderapidx9.dll` has no requested D3D9 readback methods as imports/exports/function names; COM calls can be unnamed. This bounded negative result does not establish absence.

### Remaining runtime research

The one-frame Movie Maker export chain, successful PNG save, shader backend dispatch, Material System dispatch, and buffer-copy store have been observed. The static conversion analysis and argument/caller tables are in [MATERIALSYSTEM_CONVERSION_PIPELINE.md](MATERIALSYSTEM_CONVERSION_PIPELINE.md). Further research should focus on identifying the source GPU resource and synchronization, and correlating the captured CPU data with the exact exported PNG. Preserve the current evidence classifications; do not infer `ReadPixels`, a named pixel format, or DXVK involvement until the corresponding call/resource is observed.

### Material System temporary-buffer producer follow-up (2026-11-02)

- **VERIFIED:** `materialsystem.dll+0x7BC90` allocates an outer `extentA × extentB × 4` CPU buffer, places it in a descriptor, calls `+0x4E840`, then (on success) passes it as source selector 0 to `+0x48EC0` and frees it.
- **VERIFIED:** `+0x4E840` validates descriptor dimensions/strides/flags and dispatches through a global function-pointer table. Under one flag branch it allocates and zeroes a separate `extent0 × extent1 × extent2 × 4` scratch buffer before dispatch; it frees that scratch on return.
- **STRONGLY SUPPORTED:** the callers `+0x7BC90` and `+0x7AAF0` belong to object mip/subresource texture-data processing: `+0x7AAF0` loops nested extents and obtains source/destination subresource pointers and metadata from virtual methods before using the same descriptor-processing family.
- **UNKNOWN:** the concrete indirect processor selected for the `+0x7BC90` descriptor, the direct producer of its source data, and whether the processor accesses GPU resources. This path has no static edge to the Movie Maker frame-output call chain, so its relationship to the export source remains **UNKNOWN**. See [MATERIALSYSTEM_CONVERSION_PIPELINE.md](MATERIALSYSTEM_CONVERSION_PIPELINE.md).
