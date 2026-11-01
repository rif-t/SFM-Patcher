# Reverse-engineering setup

**Status:** installed and verified on 2026-11-01. Ghidra analysis data and third-party tools are outside this repository. No SFM installation files, `AGENTS.md`, renderer code, DXVK, ReShade, or user presets were changed.

## Selected components

| Component | Installed version | Source and verification |
|---|---:|---|
| Ghidra | 12.1.4 | Official NSA release; archive SHA-256 `ddac49f903da9d5bac833e5cc79395098b9c33cfd3279be5f31bd00387d2d4db` |
| JDK | Temurin 21.0.13+9, x64 | Adoptium release; archive SHA-256 `a4dbaa05fca4bd89d84584e58c459bd732efebb1722af80ab13c6516832004f9` |
| GhidraMCP Python package | 2.0.0 | Official [`LaurieWired/GhidraMCP` tag 2.0](https://github.com/LaurieWired/GhidraMCP/tree/2.0), commit `1e980252087b2479a21b9ca6d82d5829538651a7` |
| GhidraMCP extension | 2.0.0 | Built from the same reviewed upstream source against Ghidra 12.1.4; JAR SHA-256 `f38d5d672a33590db2893b96085c1fcc435edb8e4aa40cf3a7616b444252f6e4` |
| Python runtime | 3.13.18, x64 | Managed by uv for GhidraMCP/PyGhidra compatibility |
| uv | 0.13.6 | Existing installation reused |
| MCP Python SDK | 1.30.0 | Installed as a locked `<2` dependency of GhidraMCP; server MCP metadata reported 1.30.0 |

The official Ghidra 12.2 release was newer at setup time, but the GhidraMCP 2.0 release explicitly targets Ghidra 12.1.4. This setup uses the matching released pair instead of an unverified plugin build against 12.2. Revisit after GhidraMCP publishes a matching extension.

## MCP choice and source review

**Selected: GhidraMCP.** Its upstream release provides a Ghidra extension and a stdio MCP bridge, supports a headless PyGhidra mode, and gives an exact Ghidra version pairing. The bridge fits Codex's stdio MCP configuration.

**Not installed: MCPyGhidra.** Its upstream README describes a separate Streamable HTTP server and Python 3.10–3.13. It reports testing against Ghidra 11.1, 11.3 and 12.0+, but did not provide the specific released Ghidra 12.1.4 extension pairing available in GhidraMCP 2.0. Installing both would duplicate the analysis surface.

Reviewed GhidraMCP 2.0 source before building it. The Java backend binds to loopback, checks the HTTP host and rejects browser-origin requests. It has no authentication, so keep it local. Its API exposes eight project-writing operations (symbol, type and comment changes); Codex's allowlist excludes those. Codex exposes only the 19 read-only operations listed below. Per-tool `approval_mode = "approve"` is scoped to those reviewed read-only tools; global command approval settings were not changed.

## Codex server configuration

The user Codex config now contains one `ghidra` stdio server alongside the pre-existing `node_repl` server. It pins the GhidraMCP Git commit, starts the headless bridge for `ifm.dll`, reuses the persistent project, and supplies the local Java/Ghidra environment. A timestamped config backup was created before each edit.

The 19 enabled tools are:

`get_program_info`, `list_functions`, `search_functions_by_name`, `list_classes`, `list_namespaces`, `list_segments`, `list_imports`, `list_exports`, `list_data_items`, `list_strings`, `get_function_by_address`, `decompile_function`, `decompile_function_by_address`, `disassemble_function`, `get_xrefs_to`, `get_xrefs_from`, `get_function_xrefs`, `get_function_calls`, and `read_memory`.

The Ghidra MCP process is optional in Codex. Codex's default optional-server startup grace is one second; a first-turn catalog may omit a Java-backed server that is still starting. The successful Codex CLI verification used a one-run `mcp_optional_startup_grace_ms=0` override. No global startup-grace setting was changed because that would affect other MCP servers. The currently open desktop task also needs a Codex reload/new task to refresh its tool catalog.

## Verification results

- `codex mcp get ghidra` recognizes the server and read-only allowlist; TOML parsing confirms all 19 per-tool approval entries and preserves `node_repl`.
- A noninteractive Codex CLI run made a real `ghidra.get_program_info` tool call. It returned `ifm.dll`, `x86:LE:32:default`, image base `0x10000000`, and SHA-256 `12504c248047e181d31a8500fb6693d1a2d3c805e841fc2227e41aa4e2d2b47e`.
- An independent MCP protocol check listed 28 GhidraMCP tools and successfully called `get_program_info`; the Codex allowlist filters out the eight write operations and the deprecated `list_methods` tool.
- All six imported DLLs retain their pre-import SHA-256 hashes. Ghidra reported six successful imports and six successful analyses.
- The `ifm.dll` Ghidra project contains 43,214 functions, including 3,332 external functions. Ghidra loaded the requested addresses as x86 32-bit PE.
- Ghidra could not locate a matching PDB, and import logs contain unresolved external-DLL and a few invalid-pointer decompiler warnings. These limit type/name recovery; they did not prevent project analysis or the tested queries.

## SFM binaries imported

| Module | SHA-256 |
|---|---|
| `ifm.dll` | `12504c248047e181d31a8500fb6693d1a2d3c805e841fc2227e41aa4e2d2b47e` |
| `engine.dll` | `c084b859142f39e3a1040a550e5e2edf44940f1f5fb9fe46cf257f25bb5b5e4f` |
| `materialsystem.dll` | `c78e414891b4d87e3922610a8bf937bca3efbaab95bced488522045998bc87f8` |
| `shaderapidx9.dll` | `07cee65141f5c1cca85a73b25c675ca01c0068cf6e758e49b07e38424ac75baa` |
| `video_services.dll` | `44f6d35417ef2a053b87c50da2ca22df51d9b9497df49bb75ef391f3fe8ef296` |
| `valve_avi.dll` | `12f40756934cf576020d5f53bba9fd3dc3f8f361e3454f8253a0a302d77d62e7` |

## Reproduction outline

1. Install a 64-bit JDK version declared by the chosen official Ghidra release. For the pinned release here, use JDK 21.
2. Download Ghidra only from the [official NSA releases](https://github.com/NationalSecurityAgency/ghidra/releases) and verify its published SHA-256 before extraction.
3. Check out the [GhidraMCP 2.0 upstream tag](https://github.com/LaurieWired/GhidraMCP/tree/2.0); review the source and build its extension with the target Ghidra installation's `support/gradle/gradlew.bat buildExtension`.
4. Use uv with Python 3.13 and the upstream package specification `ghidra-mcp[headless] @ git+https://github.com/LaurieWired/GhidraMCP@2.0`.
5. Import the six binaries into a new Ghidra project outside the repository using the command in [GHIDRA_WORKFLOW.md](GHIDRA_WORKFLOW.md). Let the PE loader select the language and verify the returned language ID; do not force an architecture based only on a filename.
6. Configure Codex with the upstream stdio bridge and the read-only tool allowlist above. Use environment-specific paths in the local Codex config, not in project documentation or committed files.

Upstream references: [Ghidra releases](https://github.com/NationalSecurityAgency/ghidra/releases), [GhidraMCP](https://github.com/LaurieWired/GhidraMCP), [MCPyGhidra](https://github.com/nightwing-us/mcpyghidra), and [Codex configuration reference](https://developers.openai.com/codex/config-reference).
