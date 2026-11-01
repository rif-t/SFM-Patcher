# SFM Movie Maker binary analysis

**Scope:** static analysis of one installed x86 SFM build. No installation files were changed. No runtime trace, source-level decompilation, or PDB-assisted symbol recovery was performed. RVAs below are specific to this examined `ifm.dll`; resolve them against that module's loaded base and re-check on any other build.

## Tools and confidence

Used Visual Studio `dumpbin` for PE headers/imports/exports and a temporary Capstone x86 disassembler for code and IAT cross-references. A small PE parser mapped file offsets to section RVAs and parsed x86 MSVC RTTI (TypeDescriptors, Complete Object Locators, class hierarchy descriptors and vtable references). The scripts and temporary Capstone package were kept outside the repository. No Ghidra/IDA/REA native PE provider or matching PDB was available. This is disassembly plus structural metadata analysis, not decompilation.

Evidence labels are defined in [RESEARCH.md](RESEARCH.md). A code address referencing a string proves a static reference, not that the code executed. An imported function proves a dependency; an instruction calling its IAT entry proves a call site exists in the file, not that it runs for Movie Maker.

## Examined `ifm.dll` identity and PE structure

| Property | Value |
|---|---|
| PE machine / bitness | `0x014C`, x86 / 32-bit |
| Preferred image base | `0x10000000` |
| PE timestamp | 2015-01-09 04:31:26 UTC (PE timestamp; build time can be spoofed or preserved) |
| File version resource | `0.0.0.0` / no useful product metadata |
| SHA-256 | `12504c248047e181d31a8500fb6693d1a2d3c805e841fc2227e41aa4e2d2b47e` |
| CodeView record | `ifm.pdb`, GUID `{AA0CA47A-A9BD-44FB-A8EC-958C85384C2C}`, age `3`; recorded PDB was not found beside the installation binaries |
| COFF symbol table | absent (`0` entries) |
| Sections | `.text` RVA `0x1000`; `.rdata` RVA `0xA7B000`; `.data` RVA `0xE77000`; `.reloc` RVA `0xFF6000` |

The build path stored in the CodeView record is intentionally omitted. The SHA and timestamp identify only the artifact examined here; do not assume all SFM installations use this exact binary.

## Exports, imports and module boundaries

### `ifm.dll`

Exports recovered by `dumpbin /exports`:

| Export | RVA | Meaning supported by evidence |
|---|---:|---|
| `CreateInterface` | `0x980C20` | Factory entry-point export; which interfaces it returns was not probed. |
| `NvOptimusEnablement` | `0xE7FD8C` | GPU selection variable. |
| `init_datamodel` | `0x790B10` | Initialization export; signature/behavior not recovered. |
| `init_dmeutils` | `0x7ADDB0` | Initialization export; signature/behavior not recovered. |
| `init_movieobjects` | `0x965720` | Movie-object initialization export; does not establish Movie Maker's export call path. |

There is no exported `OnExportMovie`, `ExportMovie`, readback or image-writer function. Those names are internal strings/metadata, not exports.

`ifm.dll` imports Qt 4 (`QtCore4.dll`, `QtGui4.dll`, `QtSvg4.dll`), Python/shiboken (`python27.dll`, `shiboken-python2.7.dll`), Steam, `tier0.dll`, `vstdlib.dll`, sixense, and Win32 modules. Important imported Qt symbols include:

- `QProcess::startDetached(QString, QStringList)` and an overload accepting a working directory and process-id output.
- `QProcess::start`, `setWorkingDirectory`, `systemEnvironment`, `setEnvironment`, `waitForFinished`, `readAllStandardOutput`, `readAllStandardError`, `terminate`.
- `QImage::save(QString, format, quality)` plus QImage construction, conversion, scaling and pixel APIs.
- `CreateFileA/W`, `WriteFile`, `ReadFile`, and `ShellExecuteA` from Win32 modules.

The decorated Qt import for the four-argument `QProcess::startDetached` overload is static (`S` in the MSVC decoration); its call site pushes arguments and caller-cleans the stack, consistent with x86 `__cdecl`. `QProcess::start` and `waitForFinished` are instance methods called with an `ECX` receiver, consistent with x86 `__thiscall`. The viewport block and helper also use `ECX` receivers and callee cleanup (`ret 8` / `ret 0x1C`), but their private signatures remain unknown. `CreateInterface` signatures/calling conventions were not recovered from private factory source.

These are module-level capabilities. The QImage and file APIs can serve thumbnails, comics, screenshots, or other editor features. They do not identify the Movie Maker frame writer without a caller/data correlation.

### Related modules

| Module | Verified PE/API evidence | Boundary / limitation |
|---|---|---|
| `engine.dll` | x86; exports `CreateInterface` and another engine API symbol; strings include `VEngineClient013`, `IVideoServices002`, `VAvi001`, `VMaterialSystem080`, `VMaterialSystem2_001`, `RenderDevice001`; contains legacy `startmovie`/`endmovie` strings | Interface-name strings are candidate identifiers, not factory-query/runtime proof. Legacy movie recording is distinct from the SFM GUI export path. |
| `materialsystem.dll` | x86; exports `CreateInterface`; candidate strings include `VMaterialSystem080`, `VMaterialSystemConfig004`, `VMaterialSystem2_001`, `RenderDevice001` | No runtime factory probing; no proof which version SFM queries or returns. |
| `shaderapidx9.dll` | x86; exports `CreateInterface`; imports `d3d9.dll`; contains candidate shader API/interface strings such as `ShaderApi030` | Shows a D3D9 backend module/dependency, not a Movie Maker call. |
| `video_services.dll` | x86; exports `CreateInterface`; candidate `IVideoServices002` string | No call-chain evidence to Movie Maker. |
| `valve_avi.dll` | x86; exports `CreateInterface`; imports `AVIFIL32.dll` functions including `AVIFileCreateStreamA`, `AVIMakeCompressedStream`, `AVIStreamSetFormat`, `AVIStreamWrite`, and Bink functions | Provides AVI-related capability. Movie Maker's use is UNKNOWN. |
| `tier0.dll`, `vstdlib.dll` | x86; common Source support modules; `tier0.dll` exports profiling APIs used through `ifm.dll` imports | Shared dependencies do not establish an export flow. |
| DXVK `d3d9.dll` | x86 D3D9 replacement present in the examined setup; DXVK source documents backend translation | Its presence/runtime log proves the D3D9 translation environment, not that a given export readback occurred. |

No direct PE import from `ifm.dll` to `engine.dll`, `materialsystem.dll`, `shaderapidx9.dll`, `video_services.dll`, or `valve_avi.dll` was found. Source-style runtime factory lookup or engine registration is plausible, but static imports do not confirm it. A module's `CreateInterface` export and nearby interface-version strings do not prove a caller successfully queried that interface.

## RTTI, class hierarchy and vtable metadata

RTTI is present despite the stripped COFF symbol table. Parsing x86 MSVC TypeDescriptors and Complete Object Locators confirms the following class names and base chains. Vtable RVAs refer to first function-pointer entries (the RTTI locator is stored immediately before a primary table; secondary tables also occur).

| RTTI class | Verified base chain | Vtable RVA(s) recovered |
|---|---|---|
| `CQLayoffMovieDialog` | `QDialog` → `QWidget` → `QObject` / `QPaintDevice` | `0xABF754`, secondary `0xABF734` |
| `CQtMovieLayoffProgress` | `QDialog` → `QWidget` → `QObject` / `QPaintDevice` | `0xABF9A4`, secondary `0xABF984` |
| `CQMovieLayoffSettingsDialog` | `QDialog` → `QWidget` → `QObject` / `QPaintDevice` | `0xABFF14`, secondary `0xABFEF8` |
| `CTempNeuteredMovieLayoffProgress` | VGUI `Frame` → `EditablePanel` → `Panel` and VGUI client/virtual-inheritance bases | `0xABE1C4` |
| `CPosterLayoff` | VGUI `Frame` → `EditablePanel` → `Panel` and VGUI client/virtual-inheritance bases | `0xABE98C` |
| `CQViewportArea` | `QWidget`/Qt bases plus render, movie-document, tick, time and viewport listener interfaces | primary `0xAC8C24`; multiple secondary interface tables near `0xAC8B8C–0xAC8C04` |
| `CMovieDoc` | element-properties/data-model notification and clipboard listener bases | primary `0xAE7158`; several secondary tables near `0xAE7138–0xAE7150` |

The binary has the standalone string `CQMovieLayoffProgress` at RVA `0xA9D1A4`, and a pointer to it in `.data` at RVA `0xE7CA68`. It does **not** have the exact RTTI type name `.?AVCQMovieLayoffProgress@@`; the similarly named RTTI class is `CQtMovieLayoffProgress`. This distinction prevents conflating a Qt metadata string with a C++ class identity.

Vtable addresses and base metadata prove class structure only. No virtual slot has been assigned a semantic method name unless a separate code/string/import reference supports that interpretation.

## Identifier references and code cross-references

RVAs are relative to the `ifm.dll` image base. File offsets are omitted unless needed. Direct code references below were found by scanning 32-bit absolute operands and checking the instruction with Capstone.

| Identifier | String RVA | Static reference / disassembly | Interpretation |
|---|---:|---|---|
| `CQMovieLayoffProgress` | `0xA9D1A4` | One data pointer at RVA `0xE7CA68`; no direct code reference recovered | Embedded Qt/editor metadata is plausible; no call/caller established. |
| `CQLayoffMovieDialog` | `0xA9D290` | Code at `0x75174` loads the string address; data pointer at `0xE7CA58`; RTTI has a separate TypeDescriptor at RVA `0xE85688` | Direct code reference exists, but is in a string comparison sequence. It does not prove dialog construction or export execution. |
| `OnExportMovie()` | `0xAA0C63` | No direct absolute-pointer reference recovered; not exported | Likely a Qt slot/meta-object method name, but no function body or caller was identified. |
| `OnExportMovieSettings()` | `0xAA0C9D` | No direct absolute-pointer reference recovered; not exported | Same limitation. |
| `ExportMovie` | `0xAAB318` | Code pushes string pointer at RVA `0xAB8A2`, then calls Qt `QString::fromAscii_helper` through IAT at RVA `0xA7B8FC` (`0xAB8A7` call site) | Evidence of string-to-`QString` conversion, not a call to an export function. |
| `ExportMovieSettings` | `0xAAB284` | Code pushes string pointer at `0xABBF1`, then calls `QString::fromAscii_helper` (`0xABBF6`) | Literal conversion; not an export-function call. |
| `Create Movie Command` | `0xAB8200` | Code pushes its address at `0x114250`, then calls `QString::fromAscii_helper` at `0x11425E` | UI/config label usage is supported; execution of its value is not shown by this reference. |
| `After Movie Command` | `0xAB81D4` | Code pushes address at `0x1144DF`, then calls `QString::fromAscii_helper` at `0x1144ED` | UI/config label usage is supported. |
| `CQViewportArea::OnRenderFinished` | `0xAC8604` | Pushed at `0x1A1B34`; followed by `tier0.dll` `CVProfile::EnterScope` call at `0x1A1B39` | Verified profiler-scope label, not recovered function symbol. |
| `CQViewportArea::OnRenderFinished: ReadPixelsFromLDRResultTexture` | `0xAC85C0` | Pushed at `0x1A1B8F`; followed by `CVProfile::EnterScope` call at `0x1A1B94` | Verified nested profiling label. It is not itself proof of a particular API or Movie Maker use. |
| `CMovieDoc::EndLayoff` | `0xAE6974` | Pushed at code RVA `0x2B558E` and passed to private helper at `0x2B5595` | A named string is supplied to an unknown helper. This does not prove a function symbol or the function's semantics. |

The `#BxExportMovie` and `#BxExportMovieSettings` localization keys also have code references at `0x2CF34E` and `0x2CF20E`, respectively. They support menu/resource wiring, but no direct edge from those entries to `OnExportMovie()` was recovered.

## Viewport readback disassembly (not Movie Maker)

### Caller block

At RVA `0x1A1B10`, a compiler-shaped x86 method block uses `ECX` as an object pointer and ends with `ret 8` at `0x1A1C29`, consistent with an MSVC `__thiscall` method with two stack arguments. This is a calling-convention inference from instruction shape; there is no recovered signature or symbol name.

Within the nested `ReadPixelsFromLDRResultTexture` profiler scope, instructions:

1. Read width and height from the two caller stack arguments, via a helper at RVA `0x2BC400` that writes two dimensions to output pointers.
2. Allocate `width * height * 4` bytes if the object's buffer at `[this + 0x94]` is null.
3. Call internal helper RVA `0x27E000` with seven stack arguments: a render/texture-like pointer; `x=0`, `y=0`; width; height; destination buffer; and `0x0C` as the format-like final value.

### Helper block

The internal helper at RVA `0x27E000` is a bounded x86 function candidate (prologue at the entry and `ret 0x1C` at `0x27E072`). It obtains an object from a virtual call and dispatches six of the supplied arguments through a virtual method at vtable byte offset `0x34`. The parameter shape `(x, y, width, height, destination, format)` matches the public SDK `IMatRenderContext::ReadPixels` shape, and the enclosing profiler label says `ReadPixelsFromLDRResultTexture`.

**Classification:** the direct caller/helper/virtual-dispatch sequence is **VERIFIED**. Its interpretation as a Material-System-style `ReadPixels` operation is **STRONGLY SUPPORTED**. The exact private type, virtual method identity, format enum, texture object meaning, pixel orientation, and exported-frame relationship are **UNKNOWN**. The viewport class name and profiler scopes classify this evidence as viewport-side; no xref from Movie Maker identifiers to this block was identified.

The public Source SDK declares `IMatRenderContext::ReadPixels`, but its header is only an API reference and does not prove the installed SFM build uses that exact interface/vtable. See the [SDK header](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/public/materialsystem/imaterialsystem.h).

## Process execution, image saving and file I/O

### Qt process calls

Disassembly confirms direct calls from `ifm.dll` to Qt process APIs:

| Callsite RVA | Imported function | Evidence |
|---:|---|---|
| `0x113015` | `QProcess::startDetached(QString, QStringList, QString, qint64*)` | Static `FF 15` IAT call; surrounding code passes command, argument-list, working-directory-like and process-id output values. |
| `0x113241` | `QProcess::start(QString, QStringList)` | Direct IAT call on an object in `ECX`. |
| `0x11324E` | `QProcess::waitForFinished(int)` | Direct IAT call; preceding timeout value is `0x7530` (30,000 ms). |
| `0x11325A` | `QProcess::readAllStandardOutput()` | Direct IAT call. Nearby code also reaches standard-error reader elsewhere. |
| many | `QProcess::startDetached(QString, QStringList)` | Additional call sites include RVAs `0x6069B`, `0x6107B`, `0x61E6B`, `0x112E6C`, `0x13F4B3`, and others. They are not individually assigned to Movie Maker. |

The same module embeds a configurable movie command template with `%9d.png` image-sequence input, MPEG-4 output options and the labels “Create Movie Command” / “After Movie Command”. This makes external command-based movie assembly **STRONGLY SUPPORTED as an editor capability**. Whether the specific call sites above execute those templates during Movie Maker export is **UNKNOWN**; no direct control-flow/data-flow edge between the template and the process call was recovered.

### Image and file APIs

- `QImage::save` is imported from QtGui and has direct call sites at RVAs `0x45DE3`, `0x5259B`, `0xA1E4E`, `0x1BEFD6`, `0x1BF689`, `0x2918E7`, and `0x2B6F0A`.
- `CreateFileA/W` and `WriteFile` are imported and called at multiple code sites.
- `valve_avi.dll` imports AVI stream creation, format and write functions.

These prove that the editor and related modules can save images/files and produce AVI data. No recovered call stack or caller symbol ties any QImage save, WriteFile or AVIStreamWrite call to `CQMovieLayoffProgress`, `OnExportMovie`, or the `%9d.png` sequence. The precise PNG/TGA writer, frame numbering, atomic-write policy, and whether the command template consumes SFM-generated files remain unknown.

## Potential runtime observation points (no hooks implemented)

1. **Highest value:** in a debugger, capture one-frame Image Sequence export and record call stacks/data at the candidate Material-System `ReadPixels`-shaped helper and image-save/file-write calls. Compare the hit set against viewport-only activity and correlate the exact output PNG. Use module-base-plus-RVA because ASLR changes absolute addresses; revalidate each RVA against the build hash.
2. Trace `ifm.dll+0x113015`, `+0x113241`, `+0x11324E` and nearby process I/O to determine whether export executes the embedded command template. Capture argument strings/process tree without logging credentials or unrelated command lines.
3. Observe D3D9 `GetRenderTargetData` / `GetFrontBufferData` calls and DXVK Vulkan copy, submission, fence, map and invalidation sequences. Correlate resources to the file before classifying them as export readback.
4. Compare a one-frame Movie Maker export with viewport rendering. A hit at the viewport block alone does not establish that its buffer is exported.

Do not patch or alter targets during this observation phase. The first experiment should be read-only and use a deterministic scene with one selected frame, image-sequence output and audio disabled.
