# Movie Maker runtime trace results

**Status: NOT EXECUTED.** This document records the actual environment checks for the requested final runtime analysis. No live call stacks, runtime buffer contents, exported frame, or runtime format were observed. Static findings below are not promoted to runtime evidence.

## Environment verification

- **SFM process:** no `sfm` process was running when checked; consequently no loaded module bases, ASLR-adjusted addresses, active project, or debugger attachment could be recorded.
- **Debugger availability:** `windbg`, `cdb`, `ntsd`, `WinDbgX`, `procdump`, `x32dbg`, `x64dbg`, and `dumpbin` were not available as shell commands. The standard Windows Kits debugger locations and installed WinDbg package check returned no debugger. No debugger was installed as part of this research task.
- **Installed build identity:** `sfm.exe` SHA-256 was `8c719e1b1c913695545eddbcd5ebd80f2737702ae1e68e614b0a38472354671c`; it is not in the existing six-DLL Ghidra fingerprint table, so no comparison to an analyzed executable build is claimed. File version metadata was blank.
- **Architecture:** PE machine field for `sfm.exe`, `ifm.dll`, `engine.dll`, `materialsystem.dll`, `shaderapidx9.dll`, and `video_services.dll` was `0x014C` (x86).
- **Analysis module fingerprint check:** the installed `ifm.dll`, `engine.dll`, `materialsystem.dll`, `shaderapidx9.dll`, and `video_services.dll` SHA-256 values matched the previously documented fingerprints. This confirms static module identity for those files, not runtime loading or execution.
- **Controlled export/output:** no running session or known disposable one-frame export was available to correlate. No exported PNG was inspected; no frame, dimensions, pixel values, or output hash are available.

## Runtime questions and results

| Question | Runtime result | Static-only evidence / current classification |
|---|---|---|
| Does the export action enter the MovieDoc frame chain? | **UNKNOWN; not traced** | Static frame chain exists; menu/UI edge remains unknown. |
| Which thread evaluates and renders the frame? | **UNKNOWN; not traced** | No runtime call stacks. |
| Which call populates the CPU buffer at `this+0x234`? | **UNKNOWN; not traced** | Writer consumes the buffer; producer is unresolved. |
| Is the source viewport, offscreen target, backbuffer, or another resource? | **UNKNOWN; not traced** | No export target/resource identity established. |
| What are actual format, pitch, row orientation, channel order, alpha and color space? | **UNKNOWN; not observed** | Static QImage constructor has argument `0xD` and the branch swaps first/third bytes per 3-byte pixel. This does not safely establish a named format or final file layout. |
| Is the buffer stable and safe to process after a boundary? | **UNKNOWN; not tested** | Lifetime, reuse, thread ownership and synchronization are unobserved. |
| Does output complete as PNG/image sequence and can it be decoded? | **UNKNOWN at runtime** | Static numbered-path and QImage save call evidence only; no output file was correlated. |
| Is Movie Maker HDR/EXR-capable? | **UNKNOWN** | Static `PF`/`Pf` float-image writer branches exist; mapping to export settings and actual output is unresolved. PFM code is not proof of EXR support. |
| Does Movie Maker execute external assembly commands? | **UNKNOWN at runtime** | Static QProcess call path/options exist; invocation during export is unobserved. |
| Is audio separate from frame rendering? | **UNKNOWN at runtime** | Separate WAV setting/strings exist, but no audio call path was captured. |

## Integration-point assessment

| Candidate | Assessment from current evidence |
|---|---|
| Before buffer population | **UNKNOWN / not yet assessable:** producer and resource are unidentified. |
| Immediately after population | **UNKNOWN:** the CPU buffer is statically consumed, but ownership, lifetime, mutability, scheduling and synchronization were not observed. |
| Before channel conversion | **Static candidate only:** `FUN_102B6610` swaps byte 0 and byte 2 for a 3-byte-per-pixel branch before QImage save. Runtime execution and output effect are unverified; patching here is not yet justified. |
| Immediately before `QImage::save` | **Static candidate only:** callsite is RVA `0x2B6F0A`. It may be reachable on export, but buffer lifetime, reentrancy, frame ordering and effect on the file are unknown. |
| After image write | **Strongest low-risk prototype boundary, pending actual export:** an offline tool can consume a copied, verified output image and write a separate image without SFM hooks. No such prototype was built in this research task. |

No candidate is yet runtime-validated as a safe in-process integration point. The simplest viable first prototype remains an offline transformation of an actual exported frame, after the one-frame export and image decoding are verified.

## Next action needed for an actual trace

Install/locate a supported user-mode debugger using Microsoft's official [Debugging Tools for Windows](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/debugger-download-tools), launch SFM with a disposable project capable of a deterministic single-frame image-sequence export, and follow [RUNTIME_TRACE_PLAN.md](RUNTIME_TRACE_PLAN.md). Record loaded module bases and event stacks, then inspect the untouched output frame. The plan uses module-relative RVAs, not machine-specific absolute paths.

The most informative first pass is a breakpoint at `ifm.dll` RVA `0x2B86C0`, then step/trace through `0x2B8060`, `0x2B75C0`, `0x2B6610`, and save callsite `0x2B6F0A`. If that chain hits, the key next observation is the first write to the exact buffer at object offset `+0x234` and the call stack that performs it.
