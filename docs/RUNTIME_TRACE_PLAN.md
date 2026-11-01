# Movie Maker runtime trace plan

**Status:** core path traced; source resource remains unresolved. WinDbg confirmed the export-time MovieDoc chain and successful PNG save, followed the Movie Maker interface dispatch through `shaderapidx9.dll` and `materialsystem.dll`, and stopped at a store into the MovieDoc CPU buffer. The source pointer is stack memory with observed 1280×720 bounds and sample bytes; the underlying GPU/resource operation, format semantics, and synchronization remain unresolved. No persistent SFM installation files or project binaries were modified. WinDbg software breakpoints were used for several callsites and temporarily replace instructions in the live process; they do not change the on-disk SFM binaries.

## Preconditions

1. Start the installed x86 SFM build under a user-mode debugger (or attach before export). WinDbg package `1.2610.1001.0` was detected in the follow-up environment check. Microsoft provides WinDbg and CDB through the [Debugging Tools for Windows](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/debugger-download-tools) and documents breakpoint setup [here](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/setting-breakpoints).
2. Verify module fingerprints against the intended analysis build, including `ifm.dll`. Record module load base and PE timestamp/hash; translate each static RVA as `loaded base + RVA`. These are build-specific offsets without symbols.
3. Load a disposable/minimal project with one deterministic frame and a known camera/shot. Select Image Sequence output, disable audio and optional depth if settings allow, and export to a new empty output directory.
4. Preserve the untouched exported frame and record its dimensions, file format, size and hash. Compare pixels only against a controlled viewport capture of the same evaluated time; do not treat viewport equality as a prerequisite.

For this build, the SFM executable is `game/sfm.exe`; start it through WinDbg's **File → Start debugging → Launch executable** flow, then load the disposable project in SFM. Do not use attach-only tracing after export has already started, since the frame-entry breakpoints could be missed.

## Completed and remaining observations

| Target | Static location | Status / observation |
|---|---:|---|
| Target | Static location | Status / observation |
|---|---:|---|
| `FUN_102B86C0` through `FUN_102B6610` | `ifm.dll` RVAs `0x2B86C0`, `0x2B8060`, `0x2B75C0`, `0x2B6610` | Observed in sequence during `Recordmovie`; see [RUNTIME_TRACE_RESULTS.md](RUNTIME_TRACE_RESULTS.md). UI/menu connection and render-thread role remain unknown. |
| QImage save callsite | RVA `0x2B6F0A` | `QtGui4!QImage::save` returned success for `a3000000.png`; user confirmed file exists. Repeat file pixels not independently inspected. |
| Movie Maker transfer call | `ifm.dll` RVA `0x2B67BC` | Runtime call target `materialsystem.dll+0x2B670`, which forwards to `shaderapidx9.dll+0x1F100`; backend helper `+0x13E90` entered. |
| Material System transfer/copy | RVAs `0x0B5E0`, `0x48EC0`, `0x44224` | `+0x48EC0` hit during export; one-shot breakpoint at `+0x44224` stopped before store to destination `this+0x234`. Stack source pointer was committed thread-stack memory; resource/API/synchronization unknown. |
| Source resource identity and synchronization | unresolved | Highest-value remaining observation. Trace the producer of the stack source and identify the actual resource/API only if further runtime work is desired. |

Prefer processor execution breakpoints (`ba e 1 address`) when possible; this trace also used WinDbg software breakpoints, which temporarily replace target instructions in live process memory and must not be described as non-invasive. The x86 processor has a limited number of debug breakpoint slots. Record every hit and stack. Inspect arguments and bytes without writing to them. Avoid data breakpoints on frequently reused stack memory; the earlier watchpoint produced noisy loop/access stops. Do not instrument every D3D9/Vulkan call preemptively.

If the buffer producer is an interface call, identify the actual receiver/vtable target in the loaded build before interpreting it. Then follow the call down to the D3D9 operation and, only if observed, DXVK copy/map and synchronization. Record resource identity, dimensions, format, pitch/stride, row orientation, channel order, alpha semantics, and completion/fence behavior as separate observations.

## Decision criteria

- **Export entry:** verified only when the export action’s stack reaches the MovieDoc frame path during the controlled export.
- **Shared viewport path:** verified only if the relevant viewport readback function is observed on that same export call chain, or buffer/resource identity is independently correlated.
- **CPU buffer producer:** verified by observing the write/copy that fills the exact buffer consumed by the writer.
- **Format/layout:** verified by runtime arguments plus decoded exported pixels; a decompiler enum/constant alone is insufficient.
- **File completion:** correlate writer call/return and filesystem output; record return/error behavior and confirm the file can be decoded.
- **Stability:** repeat the single-frame export after restarting SFM before considering an in-process processing prototype. No pixel mutation is part of this trace.

## Safe next prototype boundary

After a real exported image is available and inspected, the lowest-risk prototype is an offline processor that reads a copied exported frame and writes a separate result. In-process/GPU processing remains out of scope until the producer, layout and synchronization are observed. PFM branches in static code do not establish that Movie Maker exposes EXR or a high-dynamic-range output mode.
