# Movie Maker static analysis (Phase 3)

**Scope:** read-only GhidraMCP analysis of the existing SFM project. No binaries were re-imported, no database annotations were changed, and no SFM files or renderer code were modified. Ghidra generated most function names (`FUN_...`); no matching PDB is available. These addresses apply only to the fingerprinted `ifm.dll` build below.

## Program identity and integrity

The active program was checked before targeted queries. Program switching also succeeded for `materialsystem.dll`, `shaderapidx9.dll`, and `engine.dll`; all are x86 PE images with base `0x10000000`. SHA-256 hashes of all six imported DLLs were recomputed and match the prior inventory in [REVERSE_ENGINEERING_SETUP.md](REVERSE_ENGINEERING_SETUP.md). The Ghidra project was reused without re-import or re-analysis.

| Module | SHA-256 |
|---|---|
| `ifm.dll` | `12504c248047e181d31a8500fb6693d1a2d3c805e841fc2227e41aa4e2d2b47e` |
| `engine.dll` | `c084b859142f39e3a1040a5500e5e2edf44940f1f5fb9fe46cf257f25bb5b5e4f` |
| `materialsystem.dll` | `c78e414891b4d87e3922610a8bf937bca3efbaab95bced488522045998bc87f8` |
| `shaderapidx9.dll` | `07cee65141f5c1cca85a73b25c675ca01c0068cf6e758e49b07e38424ac75baa` |
| `video_services.dll` | `44f6d35417ef2a053b87c50da2ca22df51d9b9497df49bb75ef391f3fe8ef296` |
| `valve_avi.dll` | `12f40756934cf576020d5f53bba9fd3dc3f8f361e3454f8253a0a302d77d62e7` |

## Export UI and Qt metadata

All VAs below are in `ifm.dll` (Ghidra image base `0x10000000`); RVA is VA minus image base.

| VA / RVA | Finding | Classification and limit |
|---|---|---|
| `0x10075160` / `0x75160` | `FUN_10075160` compares the incoming class name with `CQLayoffMovieDialog`, then calls `QDialog::qt_metacast` on mismatch. | **VERIFIED:** Qt meta-cast behavior. No constructor or export caller was identified. |
| `0x10075F60` / `0x75F60` | `FUN_10075F60` performs the analogous comparison for `CQtMovieLayoffProgress`. | **VERIFIED:** Qt meta-cast behavior. |
| `0x100760F0` / `0x760F0` | `FUN_100760F0` performs the analogous comparison for `CQMovieLayoffSettingsDialog`. | **VERIFIED:** Qt meta-cast behavior. |
| `0x10AA0C63`, `0x10AA0C84`, `0x10AA0C9D` | Strings `OnExportMovie()`, `OnExportRenderSettings()`, `OnExportMovieSettings()`. | **VERIFIED:** strings exist; targeted Ghidra xrefs returned none. No function body, Qt slot connection, or caller recovered. This is not proof they are unused. |
| `FUN_100AA9F0` | References `ExportMovie`, `ExportMovieSettings`, `ExportImage`, and `ExportImageToClipboard`. | **VERIFIED:** static data references. No direct edge from this function to the frame path was found. |
| `FUN_102CEFB0` | References `ExportMovieSettings`, `#BxExportMovie`, and `#BxExportMovieSettings`. | **VERIFIED:** localization/UI identifiers only; no handler edge recovered. |

`CMovieDoc::EndLayoff` is not a recovered symbol. `FUN_102B5190` passes the string at `0x102B558E` to a helper call at `0x102B5595`; this is a diagnostic/profiling label reference, not a call to an address-bearing symbol of that name. The cleanup interpretation is **STRONGLY SUPPORTED** by the function's behavior and label, not by a PDB symbol.

## Recovered MovieDoc frame/output sequence

The strongest static sequence is in `ifm.dll`:

1. `FUN_102B86C0` contains the profiler label `CMovieDoc::RenderFrameBegin` at `0x102B86D4` and calls frame-processing alternatives. It conditionally calls `FUN_102B8060` at `0x102B874D`.
2. `FUN_102B8060` reads the `frameRate` option (`0x102B808E`), updates time-like state, invokes indirect engine-interface methods, checks `videoTarget` (`0x102B840D`), and calls `FUN_102B75C0` at `0x102B8447` and `0x102B8453` on output branches. Its decompilation also shows frame-index/counter updates and termination handling.
3. `FUN_102B75C0` calls `FUN_102B6610` at `0x102B7A8F`; it handles per-frame output setup and the optional `writeDepth` option.
4. `FUN_102B6610` formats frame paths with `"%s%06d%s.%s"`, constructs a `QImage` over the CPU pointer at `this + 0x234` with dimensions from local values and format argument `0xD`, and calls imported `QTGUI4.DLL::QImage::save` at `0x102B6F0A` in the branches selected by `videoTarget` values `5` or `8`. It swaps the first and third byte of each 3-byte pixel before this save branch. Other target branches call image-writing helpers or write `PF`/`Pf` floating-point image headers and pixel data.

The direct instruction edges within this sequence are **VERIFIED**. Their identity as the user-invoked Movie Maker export is **STRONGLY SUPPORTED**, because the code carries `CMovieDoc::RenderFrameBegin`, `videoTarget`, `frameRate`, frame counters, `writeDepth`, layoff cleanup, and per-frame output evidence. The exact UI action-to-controller edge remains **UNKNOWN**.

### Frame call sites and callers

- `FUN_102B9190` calls `FUN_102B86C0` at `0x102B9222` after its `CMovieDoc::MsgFunc_ParticleSystemUpdated`-labelled message handling succeeds. This is a verified direct call, but it does not establish an export-menu invocation.
- `FUN_102BC0B0` calls `FUN_102B86C0` at `0x102BC0CC` when an indirect predicate and state condition pass. Its event/scheduler role is **UNKNOWN**.
- Ghidra reports an additional direct call at `0x1015BD37` with no containing function identified.
- No identified caller of `FUN_102B86C0` is the export action or one of the recovered dialog functions.

## CPU image buffer, readback, and file output

`FUN_102B6610` proves that a CPU-addressable buffer at `this + 0x234` is wrapped by `QImage` and passed to image writers. Its QImage branch constructs the path using the frame number and configured filename/extension fields; `QImage::save` is called at `0x102B6F0A`. The direct QImage branch does not visibly test the returned save status in the recovered pseudocode. Other branches have explicit warning/error handling.

`FUN_102B75C0`'s optional `writeDepth` path acquires an object through `DAT_10F4C774` vtable slot `+0x19C`, then calls that object's slot `+0x194` with rectangle-like local arguments, the buffer at `this + 0x234`, value `0x1D`, and a width-derived stride. It subsequently writes a `Pf` header and depth samples. This is **STRONGLY SUPPORTED** as an interface-mediated buffer transfer/read operation. The exact interface, method identity, source resource, GPU synchronization, and whether the color path uses the same operation are **UNKNOWN**. The vtable offset must not be named `ReadPixels` based only on the public SDK.

In `FUN_102B6610`, slot `+0x19C` is also called to obtain an object, while a later slot `+0x194` call has no explicit buffer/rectangle arguments in the decompiler output. That call is not enough to identify a color readback. The origin and producer of `this + 0x234` are not yet traced. Therefore:

- **VERIFIED:** MovieDoc frame-output code consumes CPU-addressable image data and calls `QImage::save` or other writers.
- **STRONGLY SUPPORTED:** the optional depth route copies/transfers data through an interface before PFM-style output.
- **UNKNOWN:** whether Movie Maker color frames are read back from a GPU target, which target/resource is used, and what synchronization/format applies.
- The earlier `FUN_101A1B10` / `FUN_1027E000` chain remains viewport-side evidence only. No static edge joins it to this MovieDoc output sequence.

## Post-render commands and audio

`FUN_102B5190` calls `FUN_102A28D0` at `0x102B534A` and `0x102B53E7`, supplying `MoviePostRenderCommand`/`MoviePostRenderArguments` and `MovieAfterRenderCommand`/`MovieAfterRenderArguments`. `FUN_102A28D0` configures `QProcess` and can start a process detached or start/wait up to 30 seconds/read stdout and stderr. These direct calls and option references are **VERIFIED**; their use for a particular user export is unobserved. The command-template strings, including `%9d.png`, remain separate static evidence; the exact template-to-process data flow is not recovered.

An `audioTarget` setting string exists, but the inspected frame/output functions do not establish audio rendering, WAV writing, or mux behavior. Audio separation and ordering remain **UNKNOWN**.

## Related module checks

- `materialsystem.dll`: hash verified; x86; exports `CreateInterface` and `entry`. `VMaterialSystem080`, `VMaterialSystemConfig004`, `VMaterialSystemStub001`, `VMaterialSystem2_001`, and `ShaderApi030` are embedded labels, not exports. No named `ReadPixels`/`GetRenderTarget` function was recovered.
- `shaderapidx9.dll`: hash verified; x86; imports include `Direct3DCreate9`, not named D3D readback methods. No `GetRenderTargetData`, `GetFrontBufferData`, `LockRect`, or equivalent requested method appeared as an import/export/function name. COM/device vtable calls can remain unnamed; their absence from symbol tables is not proof of absence.
- `engine.dll`: hash verified; x86; no named readback import/function or direct bridge to the `ifm.dll` frame path was found in the bounded export/import/name/string inspection. Its `startmovie` and `host_framerate` strings are not Movie Maker call proof.
- `video_services.dll`: hash verified; x86; exports `CreateInterface` and `entry`. Its factory contains interface labels including `GIVideoServices002` and `IVideoSubSystem002`, with `video_quicktime`/`video_bink` subsystem strings. It imports file and DLL-loading APIs, including `WriteFile`, but the bounded query found no Movie Maker frame-writer or audio/WAV bridge. `WriteFile` import presence is not evidence that it writes exported frames.
- `valve_avi.dll`: hash verified against the existing project inventory. Prior binary inventory found AVI/Bink-related imports, but this phase did not trace its functions to the MovieDoc path. Its relationship to Movie Maker container/audio output remains **UNKNOWN**.

## Recommended one-frame runtime experiment

A meaningful experiment can now target known static addresses. With a deterministic one-frame Image Sequence export and audio disabled, capture call stacks and arguments at `FUN_102B86C0`, `FUN_102B8060`, `FUN_102B75C0`, `FUN_102B6610`, and the `QImage::save` callsite `ifm.dll+0x2B6F0A`. Add a read-only data watch on the frame buffer at object offset `+0x234`; record it immediately before/after `FUN_102B75C0` and before `QImage::save`, and correlate it with the emitted numbered image. Trace the indirect `DAT_10F4C774` slot calls and D3D9/DXVK transfer/fence events only after the buffer's producer is observed. Compare against a viewport-only frame to determine whether the paths share any target or readback.

This experiment can verify the UI-to-frame entry, buffer producer, target, format, synchronization, and file result without changing GPU resources. No runtime trace or exported-frame inspection has been performed yet.
