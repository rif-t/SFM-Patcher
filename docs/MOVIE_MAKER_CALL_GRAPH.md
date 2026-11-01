# SFM Movie Maker call graph: evidence-bounded reconstruction

This graph separates static code references, binary metadata, and hypothesized runtime flow. `VERIFIED` edges are directly represented by RTTI or disassembled instructions. `STRONGLY SUPPORTED` edges combine code shape with labels/API shape. Dotted edges are hypotheses or unresolved questions. The graph is not a claim that every edge executes during export.

## Reconstructed graph

```mermaid
flowchart TD
  subgraph UI[ifm.dll editor UI and metadata]
    Menu["#BxExportMovie / #BxExportMovieSettings localization keys"]
    Slot["OnExportMovie() / OnExportMovieSettings() strings"]
    Dialog["CQLayoffMovieDialog RTTI class"]
    Settings["CQMovieLayoffSettingsDialog RTTI class"]
    Progress["CQtMovieLayoffProgress RTTI class"]
    TempProgress["CTempNeuteredMovieLayoffProgress RTTI class"]
    MovieLabel["ExportMovie / ExportMovieSettings text"]
    QString["Qt QString::fromAscii_helper"]
    ProcTemplate["Create Movie Command / After Movie Command + %9d.png template"]
    ProcCalls["QProcess call sites: startDetached, start, waitForFinished, stdout"]
    QProcess["Qt QProcess process execution"]
    ImageSave["QImage::save call sites"]
    FileIo["CreateFile / WriteFile call sites"]
    AVIModule["valve_avi.dll AVI writer APIs"]

    Menu -->|"VERIFIED: code references localization key"| ResourceCode["resource/label construction code; caller identity unknown"]
    MovieLabel -->|"VERIFIED: push literal then call QString conversion"| QString
    ProcCalls -->|"VERIFIED: direct IAT calls in ifm.dll"| QProcess
    ProcTemplate -.->|"STRONGLY SUPPORTED: command template and process APIs coexist; direct data-flow not recovered"| ProcCalls
    Slot -.->|"UNKNOWN: no function body or direct xref recovered"| Dialog
    Dialog -->|"VERIFIED: RTTI base metadata"| QDialog["QDialog / QWidget / QObject"]
    Settings -->|"VERIFIED: RTTI base metadata"| QDialog
    Progress -->|"VERIFIED: RTTI base metadata"| QDialog
    TempProgress -->|"VERIFIED: RTTI base metadata"| VGUIFrame["VGUI Frame / EditablePanel / Panel"]
    ImageSave -.->|"UNKNOWN: no export caller correlated"| PNG["Movie Maker frame image writer"]
    FileIo -.->|"UNKNOWN: generic file I/O call sites"| PNG
    AVIModule -.->|"UNKNOWN: no Movie Maker interface call traced"| MovieFile["final AVI/movie file"]
  end

  subgraph Viewport[Separate viewport path in ifm.dll]
    ScopeOuter["CQViewportArea::OnRenderFinished profiler label"]
    ScopeRead["ReadPixelsFromLDRResultTexture profiler label"]
    Caller["code block RVA 0x1A1B10; width/height; width × height × 4 buffer"]
    Helper["internal helper RVA 0x27E000"]
    Virtual["virtual method at receiver vtable +0x34; six read-like args"]
    SDK["Source SDK IMatRenderContext::ReadPixels signature (analogue only)"]
    ScopeOuter -->|"VERIFIED: nested scope emitted in same code block"| ScopeRead
    ScopeRead -->|"VERIFIED: code allocates buffer and calls helper with target, rect, dimensions, buffer and 0x0C"| Caller
    Caller -->|"VERIFIED: direct call"| Helper
    Helper -->|"VERIFIED: dispatches six stack args through virtual slot"| Virtual
    Virtual -.->|"STRONGLY SUPPORTED: argument shape and profiler label match read-pixels semantics"| SDK
  end

  subgraph Render[Potential engine render/export path — unresolved]
    Export["Movie Maker export request / frame scheduler"]
    Scene["time, shot, camera and scene evaluation"]
    MatSys["Source Material System / render context"]
    D3D9["shaderapidx9.dll / D3D9 device"]
    DXVK["DXVK D3D9 translation"]
    Vulkan["Vulkan images, copies and synchronization"]
    Readback["export target readback / CPU buffer"]
    Writer["PNG/TGA writer and file output"]
    Export -.->|"UNKNOWN: handler and job entry not recovered"| Scene
    Scene -.->|"HYPOTHESIS: normal engine rendering is likely"| MatSys
    MatSys -.->|"HYPOTHESIS: engine backend route for export"| D3D9
    D3D9 -.->|"STRONGLY SUPPORTED for the examined SFM runtime; export-time path untraced"| DXVK
    DXVK -.->|"VERIFIED by DXVK implementation generally; not this export's call"| Vulkan
    Vulkan -.->|"UNKNOWN: export image/resource not identified"| Readback
    Readback -.->|"UNKNOWN: no consumer/writer correlation"| Writer
  end

  Caller -.->|"UNKNOWN: no Movie Maker-to-viewport call/reference found"| Export
```

## Edge ledger

| From → to | Classification | Evidence / limit |
|---|---|---|
| `#BxExportMovie` / `#BxExportMovieSettings` → resource/label-construction code | **VERIFIED** | Code pushes these localization-key addresses in `ifm.dll` at RVAs `0x2CF34E` / `0x2CF20E`. This is not a handler cross-reference. |
| `ExportMovie` / `ExportMovieSettings` string → Qt string conversion | **VERIFIED** | Code pushes each literal then calls imported `QString::fromAscii_helper` (`ExportMovie`: call at `0xAB8A7`; settings: `0xABBF6`). It supports literal/label use, not a function call. |
| `OnExportMovie()` → export handler | **UNKNOWN** | String is at RVA `0xAA0C63`; no direct pointer xref or exported symbol was recovered. Qt metadata may reference method names through offsets not found by this direct pointer scan. |
| export handler → `CQLayoffMovieDialog` / settings / progress classes | **UNKNOWN** | RTTI proves these classes exist and their base classes; no recovered call or constructor reference links them to `OnExportMovie()`. |
| `CQLayoffMovieDialog` / `CQMovieLayoffSettingsDialog` / `CQtMovieLayoffProgress` → Qt dialog bases | **VERIFIED** | Parsed MSVC RTTI class hierarchy metadata. |
| movie command labels/template → `QProcess` call sites | **STRONGLY SUPPORTED** | The command template includes `%9d.png` and MPEG-4 options; `ifm.dll` imports/calls Qt process APIs. No direct control-flow or argument-value link from template to the calls was recovered. |
| `ifm.dll` process call sites → Qt `QProcess` methods | **VERIFIED** | Direct IAT calls include `startDetached` at RVA `0x113015`, `start` at `0x113241`, `waitForFinished` at `0x11324E`, and stdout read at `0x11325A`. These call sites are statically present; execution for export is unverified. |
| `QImage::save` / `WriteFile` call sites → Movie Maker PNG frame writer | **UNKNOWN** | Imports and direct call sites exist, but their callers were not tied to Movie Maker export. |
| `valve_avi.dll` AVI write API → SFM Movie Maker | **UNKNOWN** | AVI writer APIs are imported by the module; no interface/call path from the editor was traced. |
| viewport outer profiler label → inner `ReadPixelsFromLDRResultTexture` scope | **VERIFIED** | Both labels are passed to `tier0.dll` profiling calls in the same `CQViewportArea` code block. These labels are not function symbols. |
| viewport block → buffer allocation and internal helper | **VERIFIED** | At `ifm.dll` RVA `0x1A1B10`–`0x1A1C29`, code computes width×height×4, allocates a buffer, then calls helper RVA `0x27E000` with seven stack args including target-like object, zero origin, dimensions, buffer and `0x0C`. |
| helper `0x27E000` → virtual method at vtable byte offset `0x34` | **VERIFIED** | Disassembly shows the helper obtaining an object and issuing a virtual call with six stack parameters; helper ends in `ret 0x1C`. The receiver's private interface name is unknown. |
| virtual method → `IMatRenderContext::ReadPixels`-like semantics | **STRONGLY SUPPORTED** | Six-parameter shape `(x,y,width,height,data,format)` and `ReadPixelsFromLDRResultTexture` label match the public SDK method shape. The private interface/vtable identity and format are not established. |
| viewport readback path → Movie Maker export path | **UNKNOWN** | No cross-reference from export strings/classes to the viewport block, no export trace, and no matching exported PNG were available. |
| SFM D3D9 runtime → DXVK → Vulkan | **STRONGLY SUPPORTED** for normal rendering in this examined environment; **UNKNOWN** for Movie Maker frame export | SFM is x86, D3D9 backend modules are present, and prior runtime evidence showed DXVK. No export-time target/readback trace exists. |
| Vulkan transfer/readback → output file | **UNKNOWN** | No exported image resource, staging buffer, synchronization chain or CPU consumer was correlated with an output PNG. |

## Most valuable next runtime experiment

Use a deterministic project with one selected frame, Image Sequence output and audio disabled. Under a read-only debugger/API trace, record call stacks and arguments for the viewport readback candidate, `QImage::save`, `WriteFile`, and D3D9/DXVK readback operations; then compare their execution during viewport-only rendering versus Movie Maker export. Decode the produced PNG and correlate dimensions and pixel values with candidate buffers. This single controlled experiment can decide whether Movie Maker reuses the viewport path, uses another Material System/D3D9 path, or writes through a separate pipeline.

Use RVAs only after validating the exact module fingerprint; addresses in a live process are `module base + RVA` and may differ because of ASLR. No observation points have been instrumented in this research phase.
