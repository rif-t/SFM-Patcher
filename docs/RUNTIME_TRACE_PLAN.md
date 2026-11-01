# Movie Maker runtime trace plan

**Status:** prepared; not executed. Read-only observation only. No injection, patching, GPU resource mutation, or SFM installation changes are part of this plan.

## Preconditions

1. Start the installed x86 SFM build under a user-mode debugger (or attach before export). Microsoft provides WinDbg and CDB through the [Debugging Tools for Windows](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/debugger-download-tools) and documents breakpoint setup [here](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/setting-breakpoints).
2. Verify module fingerprints against the intended analysis build, including `ifm.dll`. Record module load base and PE timestamp/hash; translate each static RVA as `loaded base + RVA`. These are build-specific offsets without symbols.
3. Load a disposable/minimal project with one deterministic frame and a known camera/shot. Select Image Sequence output, disable audio and optional depth if settings allow, and export to a new empty output directory.
4. Preserve the untouched exported frame and record its dimensions, file format, size and hash. Compare pixels only against a controlled viewport capture of the same evaluated time; do not treat viewport equality as a prerequisite.

## Candidate observations

| Target | Static location | Observation goal |
|---|---:|---|
| `FUN_102B86C0` | `ifm.dll` RVA `0x2B86C0` | Does the MovieDoc-labelled frame-begin path execute during the single-frame export? Record call stack and thread. |
| `FUN_102B8060` | RVA `0x2B8060` | Record frame/time state, `frameRate`, `videoTarget`, caller and branch. |
| `FUN_102B75C0` | RVA `0x2B75C0` | Record output/depth branch and interface calls. Do not label the indirect call `ReadPixels` without identifying its implementation. |
| `FUN_102B6610` | RVA `0x2B6610` | Record path, dimensions, target branch, buffer address at `this+0x234`, and bytes immediately before writer construction. |
| QImage save callsite | RVA `0x2B6F0A` | Record call stack, filename/extension and image arguments; correlate with the resulting file. |

Begin with execution breakpoints at the function entries/callsite. Record every hit and stack. At the `FUN_102B75C0` and `FUN_102B6610` boundaries, inspect relevant arguments and buffer bytes without writing to them. Determine which instruction first populates `this+0x234`; set a data breakpoint only after confirming the valid buffer range and only if its expected hit volume is manageable. Do not instrument every D3D9/Vulkan call preemptively.

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
