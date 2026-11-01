# Ghidra workflow for SFM-Patcher

Use the dedicated Ghidra project for static analysis of the installed SFM build. Keep the project, Ghidra installation, JDK, uv environment and MCP extension outside this repository. The repository stores only findings and reproducible instructions, not databases or binary dumps.

## Project contents and identity

Imported and fully auto-analyzed these six PE modules: `ifm.dll`, `engine.dll`, `materialsystem.dll`, `shaderapidx9.dll`, `video_services.dll`, and `valve_avi.dll`. The imported source files still match the SHA-256 values recorded in [REVERSE_ENGINEERING_SETUP.md](REVERSE_ENGINEERING_SETUP.md).

Ghidra's PE loader identified `ifm.dll` as `x86:LE:32:default`, compiler `windows`, image base `0x10000000`, and SHA-256 `12504c248047e181d31a8500fb6693d1a2d3c805e841fc2227e41aa4e2d2b47e`. The previous RVAs therefore map as follows for this exact binary:

| Prior RVA | Ghidra address | Observed Ghidra function |
|---:|---:|---|
| `0x1A1B10` | `0x101A1B10` | `FUN_101a1b10`, range `0x101A1B10–0x101A1C2B`, 284 bytes |
| `0x27E000` | `0x1027E000` | `FUN_1027e000`, range `0x1027E000–0x1027E074`, 117 bytes |

RVA conversion is `image_base + RVA`. Recheck the module SHA-256 and image base before reusing these addresses against another SFM build.

## Codex MCP usage

Codex starts the GhidraMCP headless server for `ifm.dll` and reuses the persistent project. The server exposes the current program, not a project-wide `list_programs` operation. The import log is the evidence that six modules were added. To analyze a different imported module, change the `--binary` argument in the local Codex config while retaining the same project and project name; only one headless session may hold that project lock at a time.

The MCP tool results are paginated. Keep requests narrow:

- Confirm the target with `get_program_info` before interpreting addresses. Compare its SHA-256 to the recorded value.
- Use `list_functions` or `search_functions_by_name` with a small `limit` and filter.
- Use `get_function_by_address`, `decompile_function_by_address`, and `disassemble_function` on one function at a time.
- Use `get_function_calls` with `direction = "callers"`, `"callees"`, or `"both"` and a bounded limit.
- Find embedded text with `list_strings`; use `get_xrefs_to` on the returned address to inspect static references.
- Avoid `list_methods`; GhidraMCP marks it deprecated. It is intentionally omitted from Codex's tool allowlist.

GhidraMCP 2.0 exposes 28 MCP tools. The configured Codex allowlist contains 19 read-only analysis tools. It leaves out eight operations that rename functions/data/variables, set prototypes/types, or add comments. This keeps the Codex-connected workflow read-only with respect to the Ghidra database. The local loopback backend itself has no authentication; do not bind or forward it beyond the local machine.

## Focused validation already performed

The checks below used real GhidraMCP protocol calls against the imported `ifm.dll`, plus a real `get_program_info` call from Codex CLI:

- `list_functions` returned a paginated first page and reported 43,214 total functions (including 3,332 external functions).
- `get_function_by_address("0x101A1B10")` resolved `FUN_101a1b10`; it contains the profiler-labelled viewport code block.
- `get_function_by_address("0x1027E000")` resolved `FUN_1027e000`. Ghidra decompiled it. The pseudocode shows a direct call to `FUN_102bf2d0`, a global/vtable lookup, indirect virtual calls including one at vtable offset `0x34`, `FUN_102bf340`, and cleanup calls. The names and exact private interface remain unknown.
- `get_function_calls` reported 11 callees in `FUN_101a1b10`, including a direct call at `0x101A1BF9` to `FUN_1027e000`. It found three static callers of the helper (`FUN_10022d60`, `FUN_10157850`, and `FUN_101a1b10`); Ghidra found no direct static caller of `FUN_101a1b10`.
- `list_strings` found `CQViewportArea::OnRenderFinished: ReadPixelsFromLDRResultTexture` at `0x10AC85C0`; `get_xrefs_to` returned a data reference at `0x101A1B8F` from `FUN_101a1b10`.
- `list_strings` found `OnExportMovie()` and `OnExportMovieSettings()` but no static xrefs to either address. This is a bounded static negative result, not proof the labels are unused.
- `list_strings` found `CQLayoffMovieDialog`, `LayoffMovie`, and `LayoffMovieDialog`. Their data references lead to unnamed functions. It found `CMovieDoc::EndLayoff` at `0x10AE6974`; its sole recorded reference is a data reference from `FUN_102b5190`, not a confirmed call.

These results reproduce the viewport readback evidence from [BINARY_ANALYSIS.md](BINARY_ANALYSIS.md), but they do not establish that Movie Maker export uses the same code or target. Ghidra generated no recovered C++ names for these functions; the examined binary lacks a matching PDB. Import logs also include unresolved external DLLs and a few invalid-pointer decompiler warnings. Treat indirect calls, Qt metadata references and string cross-references accordingly.

## Reimport workflow

Run this once with a new project directory, setting the variables for the local machine. Do not point `GHIDRA_PROJECT_DIR` into the repository.

```powershell
$env:JAVA_HOME = "<JDK 21 install directory>"
$env:PATH = "$env:JAVA_HOME\bin;$env:PATH"
$env:GHIDRA_INSTALL_DIR = "<Ghidra 12.1.4 install directory>"
$SFM_INSTALL_DIR = "<Source Filmmaker install directory>"
$GHIDRA_PROJECT_DIR = "<dedicated analysis project directory>"
$projectName = "SFM-Patcher"

$targets = @(
    "$SFM_INSTALL_DIR\game\bin\tools\ifm.dll",
    "$SFM_INSTALL_DIR\game\bin\engine.dll",
    "$SFM_INSTALL_DIR\game\bin\materialsystem.dll",
    "$SFM_INSTALL_DIR\game\bin\shaderapidx9.dll",
    "$SFM_INSTALL_DIR\game\bin\video_services.dll",
    "$SFM_INSTALL_DIR\game\bin\valve_avi.dll"
)

& "$env:GHIDRA_INSTALL_DIR\support\analyzeHeadless.bat" `
    $GHIDRA_PROJECT_DIR $projectName `
    -import $targets `
    -analysisTimeoutPerFile 900 -max-cpu 6
```

The PE loader selected x86 for the inspected SFM modules. Verify the Ghidra program language for each newly examined binary; do not infer it from its filename. Preserve import logs outside Git, and rehash the original binary files after import if their integrity is in question.

## Current limitations

- The current Codex desktop task has not refreshed its MCP tool catalog. The Codex CLI made a successful real MCP call when run with the temporary `mcp_optional_startup_grace_ms=0` override. No global grace setting was changed because it affects startup behavior for other optional MCP servers; after restarting Codex, check that the `ghidra` tools appear. If the first task omits them, use the temporary override for that CLI run or choose a global startup policy deliberately.
- The MCP server points at `ifm.dll`. Other imported program databases are in the project, but switching the MCP target requires changing the local `--binary` argument and ending any active session first.
- No matching PDB is available, so decompilation uses generated `FUN_...` names and incomplete types. Static xrefs can miss dynamic Qt lookups and computed dispatch.
- No runtime export trace was captured. Viewport readback results do not prove Movie Maker export behavior.
