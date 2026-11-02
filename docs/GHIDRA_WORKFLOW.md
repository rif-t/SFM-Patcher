# Ghidra workflow for Modern SFM Patcher

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

### Program selection limitation and safe target switch

The connected GhidraMCP tool catalog has no `switch_program`, `open_program`, `list_open_programs`, or per-call `program` selector. A `program` argument sent to `get_program_info` was ignored and the response still identified `ifm.dll`. Thus this MCP instance cannot query `materialsystem.dll` while it is configured for `ifm.dll`. There is no evidence that the current MCP service can open a closed project program on demand.

The least disruptive supported route is one program per headless MCP session: stop the current session to release the project lock, then launch the same headless command with the same persistent project directory and project name, `--binary` set to the already-imported `materialsystem.dll`, and `--no-analyze`. The CLI's existing-project path opens a matching stored domain file; `--no-analyze` disables automatic analysis. First verify that the target's project entry already exists and matches the intended binary. Do not use `--import` or a different project name/directory for this switch. The six-module import list above documents that `materialsystem.dll` was previously imported, but its hash should still be checked against [REVERSE_ENGINEERING_SETUP.md](REVERSE_ENGINEERING_SETUP.md) before interpreting addresses.

The process tree was verified as `ChatGPT.exe` → `codex.exe app-server` → `uv.exe` → `ghidra-mcp.exe` → Python wrapper/runtime. The configured MCP entry is in the user-level Codex config; its launch working directory is the SFM-Patcher workspace, and it passes the existing project directory/name, Ghidra install, extension JAR, and Java/Ghidra environment. The active app-server is stdio-controlled by the desktop host. The Ghidra child has no attachable Windows console, and the headless Python session only reaches its orderly `close()` path when the MCP transport exits; that path stops the local backend, saves only if the program is dirty, releases its program consumer, and closes the project.

The installed app-server schema defines `config/mcpServer/reload` with no server or thread selector: request `params` may be omitted or `null`; success returns an empty `McpServerRefreshResponse` object (`result: {}`), while failure is a JSON-RPC error. The upstream implementation iterates loaded threads and refreshes MCP configuration for each ([protocol](https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md), [request handler](https://github.com/openai/codex/blob/main/codex-rs/app-server/src/request_processors/mcp_processor.rs), [refresh implementation](https://github.com/openai/codex/blob/main/codex-rs/app-server/src/mcp_refresh.rs)).

This desktop-launched app-server's stdio is held by `ChatGPT.exe`; no general app-server request tool or SDK/client attachment to that existing stdio is exposed to the current task. `codex app-server proxy` targets a separately managed daemon control socket, and the local daemon socket was absent. The `codex-ipc` named pipe is hosted by the desktop, but its protocol is not a documented route for app-server requests; no guessed IPC messages were sent. Therefore the supported request exists but is not safely invocable here, and it would refresh MCP configuration across loaded threads rather than offer a Ghidra-only restart. Do not kill the child or start a competing instance merely to force a switch. The missing capability is a supported desktop/client bridge for a scoped app-server reload, or an explicitly authorized global reload control with visibility into its affected sessions.

After that control is available, change only the Ghidra MCP binary argument to the already-imported `materialsystem.dll`, retain the exact project directory/name and environment, add `--no-analyze`, then invoke the app-server reload. Verify the active program and hash before using RVAs. A fresh process launched beside the current one cannot open the project while the current process holds its lock.

For simultaneous per-request selection, upstream has an **open, unmerged** proposal, [GhidraMCP PR #162](https://github.com/LaurieWired/GhidraMCP/pull/162), that resolves a selector among already-open `Program` objects and adds `list_open_programs`/`get_current_program`. It does not appear to open/import programs that are not already open. The change spans the Ghidra extension and Python bridge, is not part of the current installed MCP, and its reported build/test uses Ghidra 12.0.4 rather than this setup's 12.1.4. Do not apply it to the live environment without separately building and validating a backup copy. The upstream [GhidraMCP headless implementation](https://github.com/LaurieWired/GhidraMCP/blob/main/src/ghidra_mcp/headless.py) provides the one-program existing-project route described above.

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

- The active MCP session still targets `ifm.dll`; the target switch has not been performed in this task because it requires stopping/restarting the project-locking service, for which no service-control operation is exposed here.
- The installed `materialsystem.dll` SHA-256 matches the recorded source hash (`c78e414891b4d87e3922610a8bf937bca3efbaab95bced488522045998bc87f8`). This verifies the on-disk source identity only; the active Ghidra program is still `ifm.dll`, so target program metadata and function boundaries at the requested Material System RVAs have not been rechecked in this session.
- No matching PDB is available, so decompilation uses generated `FUN_...` names and incomplete types. Static xrefs can miss dynamic Qt lookups and computed dispatch.
- Runtime evidence from the Movie Maker image conversion path is recorded in [BINARY_ANALYSIS.md](BINARY_ANALYSIS.md) and [MOVIE_MAKER_CALL_GRAPH.md](MOVIE_MAKER_CALL_GRAPH.md). The Material System routine that performs the copy is observed, but the GPU resource origin and synchronization remain unestablished; see the runtime call stacks and confidence labels before drawing conclusions.

## Isolated Material System project (2026-11-02)

To avoid the locked persistent `SFM-Patcher` database, an independent project was created at `D:\GhidraProjects\SFM-Patcher-Isolated` with project name `SFM-Patcher-Isolated`. It imports only the installed SFM `materialsystem.dll`; the original project, its lock, and Codex MCP configuration were not touched. Ghidra 12.1.4 headless analysis completed and saved successfully. The project can be reopened with the supported headless command or Ghidra GUI; keep it separate from the original project.

Verified input identity: `D:\SteamLibrary\steamapps\common\SourceFilmmaker\game\bin\materialsystem.dll`, 1,122,304 bytes, PE32 machine `0x014C` (x86), SHA-256 `c78e414891b4d87e3922610a8bf937bca3efbaab95bced488522045998bc87f8`. Ghidra reports language `x86:LE:32:default`, Windows compiler, image base `0x10000000`, same SHA-256. Thus RVA `0x0B5E0`, `0x48EC0`, and `0x44200` map to `0x1000B5E0`, `0x10048EC0`, and `0x10044200`.

The configured Ghidra MCP remains bound to the existing `ifm.dll` session and exposes no project selector. Its direct probe still fails because the original project is locked. Isolated analysis was therefore done through Ghidra's supported `analyzeHeadless.bat` and a temporary read-only Ghidra script; no new MCP service or configuration change was needed.

### Target findings

- **RVA `0x0B5E0` — VERIFIED static wrapper.** Ghidra identifies `FUN_1000b5e0`, range `0x1000B5E0–0x1000B60E`, `__stdcall`, eight stack arguments, no meaningful return value. It rearranges/pushes those eight arguments, directly calls `FUN_10048ec0` at `0x1000B603`, cleans 32 bytes and returns with `ret 0x20`. One static callsite to this wrapper was found at `0x10098448`, but Ghidra did not associate it with a containing function. Runtime tracing separately verified the export-time call into this RVA from `shaderapidx9.dll+0x13F08`.
- **RVA `0x48EC0` — VERIFIED static generic conversion routine; exact API semantics UNKNOWN.** Function `FUN_10048ec0`, range `0x10048EC0–0x100496CD`, `__cdecl`, takes eight 32-bit stack arguments and returns void. The arguments are source pointer, source selector, destination pointer, destination selector, two extent/count values, and two optional row-byte values; exact dimension axes and selector meanings remain unresolved. Branches dispatch to many CPU conversion helpers; generic paths derive byte sizes through `FUN_100496d0` / `FUN_100496F0`, handle equal-format copies, or call selected converters after `FUN_10046790` / `FUN_10048D90` lookups. Five static callers were found: `FUN_10006210` (`0x100063F5`), wrapper `FUN_1000b5e0` (`0x1000B603`), `FUN_1002cec0` (`0x1002CF50`), `FUN_10079140` (`0x100792FA`), and `FUN_1007bc90` (`0x1007BDCB`). Caller values and branch/helper inventory are documented in [MATERIALSYSTEM_CONVERSION_PIPELINE.md](MATERIALSYSTEM_CONVERSION_PIPELINE.md). The 0x44200 byte repacker is reachable through selector dispatch, but no pixel/channel format can be assigned from byte counts. No GPU interface/resource access is identified inside 0x48EC0. The runtime trace proves the wrapper and implementation were active during Movie Maker export and that the generic conversion's deeper path wrote the watched CPU buffer; the exact runtime selector branch and GPU operation remain unknown.
- **RVA `0x44200` — VERIFIED CPU byte repacker.** `FUN_10044200`, range `0x10044200–0x1004423F`, `__cdecl`, arguments are source pointer, destination pointer and item count; no return value. For each item it copies source offsets `+2,+1,+0` to three sequential destination bytes, advances source by 4 and destination by 3, and loops for the count. One static caller is `FUN_10048d90` at `0x10048DCA`. This proves a 4-byte-to-3-byte CPU repack at this routine, not a pixel format or GPU readback by itself. The runtime write-watch and call stack documented above independently place this exact routine in the Movie Maker buffer path.

No matching PDB was located. Decompiler types and names remain incomplete. No static evidence from these functions identifies the source GPU resource, synchronization primitive, or channel semantics. Existing runtime conclusions remain unchanged: the buffer transfer path is verified/strongly supported, while GPU readback identity and synchronization remain unknown.

The follow-up data-flow investigation of `0x48EC0`, all five callers, and conversion dispatch helpers is documented in [MATERIALSYSTEM_CONVERSION_PIPELINE.md](MATERIALSYSTEM_CONVERSION_PIPELINE.md). It reused this isolated project and performed no import or full analysis pass.
