# SFM Movie Maker post-processing investigation

**Status:** preliminary research, 2026-11-01. No hook or processing PoC has been run. The Movie Maker export route is not yet confirmed.

## Current machine and repository audit

- Repository at audit: README and LICENSE only; one initial commit; no build system, native/Python code, docs, or tests.
- SFM executable was discovered in a non-default Steam library under the Source Filmmaker installation's `game` directory; the installation includes `game\bin\d3d9.dll` and Source engine modules. The process was not running during this audit. The setup manager must discover this layout rather than assume a drive or library path.
- The local `sfm_d3d9.log` reports DXVK **3.1.1**, x86 GCC build, NVIDIA GeForce RTX 4060, driver 617.42.0. This verifies the SFM graphics runtime at the time represented by that log, not the export path.
- The machine-wide ReShade directory contains 32-bit and 64-bit layer DLLs and manifests. The 32-bit manifest matches the supplied global layer setup. SFM's game folder contains `ReShade.ini`, `ReShade2.ini`, `ReShadePreset.ini`, `ReshadeEffectShaderToggler.addon32`, and `dxvk.conf`. The game-folder `ReShade.log` was empty when inspected. These files were only read.
- No export PNGs, known test SFM project, or active SFM process were identified during this audit. Consequently the required Monochrome export test could not be performed.

## Data-flow map

```text
Viewport path (observed setup):
SFM D3D9 rendering -> DXVK D3D9 translation -> Vulkan resources/commands
  -> ReShade Vulkan layer effects on its presentation runtime -> displayed frame

Movie Maker path (unknown after scene render):
SFM scene render -> [render target/backbuffer?] -> [Source readback? / D3D9 readback?
  / separate renderer path?] -> encoded/saved image sequence
```

`vkQueuePresentKHR` is only evidence about presentation. It does not establish that the exported image uses that image. A movie export may render to an off-screen target and read it back independently.

## Approach A: Vulkan/DXVK readback observation

### Verified facts

- Vulkan `vkCmdCopyImageToBuffer` records an image-to-buffer copy; `VkBufferImageCopy` supplies region/layout details. This operation alone does not establish which copy is the export image.
- `vkMapMemory` yields a host-visible mapping; it does not ensure preceding GPU writes are complete. The caller must synchronize device and host access. Non-coherent memory additionally requires cache-management operations such as invalidation where appropriate.
- DXVK's public D3D9 implementation includes a front-buffer-data readback path that copies an image to a buffer and marks it for readback. This shows DXVK translates at least one D3D9 readback API to Vulkan copy operations. It does not show that SFM Movie Maker calls that API.
- Intercepting Vulkan calls can reveal candidate image copies, extents, formats, buffers, synchronization, and mappings, but associating them with a saved movie frame requires a controlled export trace and correlation with resulting files.

### Unresolved

- Whether SFM Movie Maker uses a D3D9 readback call, direct Vulkan operation, engine-specific surface path, or a separate rendering route.
- Which resource and format contain the exact exported frame, whether it is final/display-referred, and whether the copy occurs once per output frame.
- Whether any observed copy is after all required SFM rendering and before the PNG writer consumes pixels.

### PoC shape

An isolated x86 Vulkan-layer/API tracing PoC should log image/buffer creation metadata, image-copy/blit commands, barriers/layouts, submissions/fences/waits, map/invalidate/unmap events, and timestamps. It must not alter resources. Compare traces from idle, viewport activity, and one-frame-at-a-time exports, then correlate candidate buffers to actual PNG dimensions/pixels. Handle dispatchable/non-dispatchable Vulkan handles correctly and avoid dereferencing opaque handles.

## Approach B: Source Material System observation

### Verified facts

- Valve's public Source SDK 2013 exposes Material System interfaces and readback-related API declarations; this is useful ABI/API reference material.
- The SDK is not the complete Source Filmmaker Movie Maker implementation. Public declarations cannot establish which interface function SFM calls or what target it reads.
- SFM's local installation contains `materialsystem.dll`, engine DLLs, and `sfm.exe`, but their presence does not identify the Movie Maker call sequence.

### Unresolved

- Whether Movie Maker invokes `IMatRenderContext::ReadPixels`, `ReadPixelsAndStretch`, another interface, or a non-Material-System path.
- Which render target, format, color transform, and alpha behavior are involved.
- Whether the readback is final and whether an in-process x86 interface hook can safely and stably observe it.

### PoC shape

Build an isolated x86 instrumentation module only after verifying interface versions and obtaining function declarations from upstream headers. Prefer interface discovery and call-site evidence over fixed offsets. Log calls/arguments and correlate export-time events with resulting PNGs. Do not patch pixels in this phase.

## Preliminary comparison

| Criterion | Vulkan readback tracing | Material System tracing |
|---|---|---|
| Coverage | Sees Vulkan work from DXVK and other Vulkan clients in the process | Sees only selected Source interfaces/calls |
| Candidate evidence | Image copies, barriers, buffer maps, synchronization, dimensions/formats (depending on tracked metadata) | Engine-level function, target/rect, destination buffer, dimensions |
| Main difficulty | Mapping opaque GPU resources and asynchronous work to the PNG writer; Vulkan-layer correctness | Discovering the exact SFM path, interface version/ABI, and safe hook point |
| DXVK compatibility | Directly observes DXVK's Vulkan work; effect mutation remains risky | Can coexist conceptually with DXVK because hook is above translation, but unverified |
| Current result | No SFM export trace | No SFM export trace |

## ReShade integration constraints

ReShade's public add-on API describes access to resources/commands used by applications loaded into ReShade and an `effect_runtime` associated with a swap chain. It exposes GPU command recording for rendering effects and screenshot capture of that runtime's backbuffer. This supports a GPU-side add-on strategy on a known compatible resource; it does not establish that the SFM export target is that swap chain or that the API accepts arbitrary CPU buffers. If Movie Maker's final readback is off-screen, full `.fx` reuse requires bringing that target into a compatible ReShade runtime/command context before readback, or a separately implemented post-process. That integration has not been demonstrated.

## Preliminary recommendation

Do not select or implement a pixel-modifying hook yet. Run both instrumentation experiments independently during the same controlled short export. The Vulkan trace is a broad way to detect whether an image-to-buffer path happens; the Material System trace may provide clearer engine semantics if Movie Maker calls a public readback interface. Select a processing point only after a candidate resource is matched to the actual output PNG. If no safe in-process interception is found, an automatic post-export file watcher/queue is a lower-risk fallback, but it must be validated for frame completeness, ordering, and overwrite behavior.

The first likely PoC should be observational only. After it identifies the exact output bytes, compare (1) Source interface interception, (2) Vulkan image processing before readback, and (3) deterministic post-export PNG processing. A ReShade `.fx` interpreter is not implied by merely having ReShade installed.

## EXR and color management notes

The requested `image-final`, `raw-sfm`, and `depth` channels are targets, not currently confirmed passes. `raw-sfm` is achievable only if an unprocessed frame is captured; depth requires locating a trustworthy depth resource and understanding its projection/encoding. Do not label a guessed depth texture as a usable pass. Capture pixel format, transfer function, alpha semantics, and any tonemapping before planning EXR conversion or AgX. EXR storage does not restore highlight or scene-linear information absent from the captured input.

## Sources

- [DXVK D3D9 swapchain implementation](https://github.com/doitsujin/dxvk/blob/master/src/d3d9/d3d9_swapchain.cpp) — D3D9 front-buffer readback implementation example.
- [Vulkan `vkCmdCopyImageToBuffer`](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdCopyImageToBuffer.html) and [copy commands chapter](https://docs.vulkan.org/spec/latest/chapters/copies.html).
- [Vulkan memory chapter](https://docs.vulkan.org/spec/latest/chapters/memory.html) and [`vkMapMemory`](https://docs.vulkan.org/refpages/latest/refpages/source/vkMapMemory.html) — mapping and synchronization constraints.
- [Valve Source SDK 2013](https://github.com/ValveSoftware/source-sdk-2013) and [Material System interface header](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/public/materialsystem/imaterialsystem.h).
- [ReShade API reference](https://github.com/crosire/reshade/blob/main/REFERENCE.md), [API declarations](https://github.com/crosire/reshade/blob/main/include/reshade_api.hpp), and [runtime implementation](https://github.com/crosire/reshade/blob/main/source/runtime.cpp).
