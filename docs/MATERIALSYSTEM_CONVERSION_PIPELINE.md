# Material System conversion pipeline

**Scope:** static Ghidra analysis of the installed SFM x86 `materialsystem.dll` in the isolated project `D:\GhidraProjects\SFM-Patcher-Isolated`. Input SHA-256: `c78e414891b4d87e3922610a8bf937bca3efbaab95bced488522045998bc87f8`; image base `0x10000000`. Runtime facts below are cross-referenced from [RUNTIME_TRACE_RESULTS.md](RUNTIME_TRACE_RESULTS.md). This work did not modify the locked original Ghidra project or perform new runtime tracing.

## Entry point and argument layout

`FUN_10048ec0` (`materialsystem.dll+0x48EC0`, range `0x48EC0–0x496CD`) is `__cdecl`, returns void, and reads eight 32-bit stack arguments. At function entry (before its prologue), arguments are at ESP+4 through ESP+20h:

| Slot | Entry stack offset | Observed role | Confidence |
|---|---:|---|---|
| 1 | +0x04 | Source/base pointer. Read by copy/conversion paths; callers pass allocated buffers, texture interface return pointers, or cached material storage. | **Verified** pointer use; exact buffer type varies / **Unknown** |
| 2 | +0x08 | Source selector/key. Compared against numeric branches and passed to format-size/dispatch lookup. | **Verified** control value; pixel-format identity **Unknown** |
| 3 | +0x0C | Destination/base pointer. Written by helpers and memcpy paths. | **Verified** pointer use; exact type **Unknown** |
| 4 | +0x10 | Destination selector/key; compared and used in helper dispatch/size lookup. | **Verified** control value; pixel-format identity **Unknown** |
| 5 | +0x14 | Integer-sized row/extent parameter used in byte-size calculation and passed to helpers. Callers use dimensions or allocation fields; width/height interpretation varies by path. | **Strongly supported** extent/dimension input; exact axis **Unknown** |
| 6 | +0x18 | Integer-sized count/row parameter. In generic path controls repeated copies; in the direct 0x44200 helper it is item count. Callers sometimes pass a dimension. | **Strongly supported** count/height-like input; exact role varies |
| 7 | +0x1C | Optional source row-byte/stride-like value. Zero causes default computation from source selector and argument 5. | **Verified** zero-defaulted byte calculation; stride interpretation **Strongly supported** |
| 8 | +0x20 | Optional destination row-byte/stride-like value. Zero causes default computation from destination selector and argument 5. | **Verified** zero-defaulted byte calculation; stride interpretation **Strongly supported** |

Ghidra's initial C types (`float *`, `void *`, `uint`) are not reliable semantic types. Each slot is physically 32 bits in this x86 function; pointer-ness and use above come from data flow, not those decompiler types.

## Branch and dispatch behavior

The implementation is a decision tree, not a single pixel loop:

1. Special source/destination selector pairs dispatch to dedicated conversion routines: source `0x19` to `0x1C/0x18`; source `0x18` to `0x1C/0x1D/0x19`; source `0x1C` to `0x19/0x18/0/0x0C`.
2. Equal selector pairs among `0x0D,0x0E,0x0F,0x22,0x23,0x3C,0x3D,0x3E` copy a computed byte extent.
3. Source `0,2,0x0C,0x10` to destination `0x0D,0x0E,0x0F` uses `FUN_10042fb0`.
4. Source `0x0B` to `0x22/0x23` uses `FUN_10042e50`.
5. A special family from source `0x0D,0x0E,0x0F,0x22,0x23` to destination `0,2,3,0x0C,0x10,0x11,0x12,0x13,0x15` dispatches to specialized routines. Source `0x0F` has a distinct set; source `0x0D` another; source `0x22/0x23` to `0x0C` reaches `FUN_100481f0`.
6. Other selector pairs use descriptor lookup `FUN_100496d0`, derive default row byte counts when arguments 7/8 are zero, and either copy rows or call selected conversion functions per row. Equal-selector fast copy exists for selector `0x1D` when computed source/destination row bytes match.
7. Unhandled combinations return without a result; function return is void. No error/status return is produced.

The routine reads source and writes destination CPU-addressed buffers in the observed branches. It references only a stack-cookie global and CPU helpers; there is **no verified GPU interface/resource access inside 0x48EC0**.

### Helper inventory

All helpers listed below are reached by direct branch or descriptor-selected dispatch in `0x48EC0`. Decompiled bodies show CPU pointer arithmetic and component conversion/repacking. Their semantic format names are unknown, so operations are described structurally.

| Helper RVA(s) | Dispatch context | Verified behavior / limits |
|---|---|---|
| `0x431F0`, `0x475C0`, `0x468C0`, `0x46920`, `0x47690`, `0x43380`, `0x469B0`, `0x46A20`, `0x46FF0` | Special selector pairs | Dedicated per-element or per-row component conversion loops; bodies use CPU pointers and arithmetic. Exact channel/range semantics not established for every routine. |
| `0x42FB0` | source `0/2/0x0C/0x10` → destination `0x0D/0x0E/0x0F` | CPU bit/component expansion/conversion; helper contains integer shifts/masks and loops. No GPU calls. |
| `0x42E50` | source `0x0B` → destination `0x22/0x23` | Specialized CPU conversion with a flag argument; no GPU calls established. |
| `0x47810`, `0x47980`, `0x47AF0`, `0x47C60`, `0x47DD0`, `0x47F30`, `0x48090`, `0x481F0`, `0x48340`, `0x484C0`, `0x48640`, `0x487B0`, `0x48920`, `0x48A90`, `0x48C10` | Source `0x0D/0x0E/0x0F/0x22/0x23` conversion family | Multi-row CPU conversion loops. Several invoke helpers such as `0x42C60`, `0x44DA0`, `0x45330`, `0x45530`, `0x45B00`, `0x462D0`; evidence supports component conversion/packing, but exact format mapping is **Unknown**. No GPU interface calls were identified in these bodies. |
| `0x44200` | Descriptor dispatch via `0x48D90`; table key `0x0C` | For count N: copy source bytes +2,+1,+0 to destination bytes 0,1,2; advance source by 4, destination by 3. CPU-only. This proves byte selection/repacking, not channel order or a pixel format. |
| `0x44240`, `0x442C0`, `0x44350`, `0x44380`, `0x443E0`, `0x44440`, `0x44490`, `0x444E0`, `0x44530`, `0x445A0`, `0x44610`, `0x44690`, `0x44700`, `0x440B0`, `0x43FF0`, `0x43D30`, `0x43B30`, `0x43EC0`, `0x47740` | Descriptor dispatch via `0x48D90` | Additional per-row converter choices. `0x48D90` maps selector keys through a bounded jump table (0..0x1F); invalid keys return null. Exact format names are not assigned. |
| `0x496D0`, `0x496F0` → `0x49790` | Generic sizing | `0x496D0` indexes a descriptor table at `0x10101B84` in 12-byte records (first label string is `RGBA8888`); `0x496F0` forwards dimensions, selector, flags and optional out pointer to `0x49790` for size calculation. Table labels alone are not treated as proof that numeric keys match named formats. |
| `0x46790` | Generic conversion path | Selector-to-function table for another converter family (keys include 0..3, 5, 6, 8..0x1F); returns function pointer or null. |

The special family bodies contain integer and floating-point arithmetic, but no evidence here establishes a conversion as GPU work. Alpha handling is branch-specific and unresolved; do not treat the fourth source byte as alpha based only on four-byte stepping.

## Five static callers of 0x48EC0

Callsites and argument values below are reconstructed from call expressions and setup instructions. Runtime values are not inferred.

| Caller (RVA) | Callsite | Argument reconstruction (1–8) | Data origin / relationship |
|---|---:|---|---|
| `FUN_10006210` (`0x6210`) | `0x63F5` | `local_40[0]`, `iVar4`, caller `param_1`, `param_4`, `param_2`, `param_3`, `uVar6`, `uVar7` | Reads cached/material-like content through interfaces rooted at `DAT_101255C4`, fills `local_40[0]` via a returned interface/function pointer, gets selector from vtable slot `+0x4C`, then converts to caller output. Values 7/8 are derived earlier; their exact meanings remain unknown. **Tentative** material/cache path; not Movie Maker-linked. |
| `FUN_1000B5E0` (`0xB5E0`) | `0xB603` | forwards its eight incoming stack values unchanged | Wrapper only. The static caller at `0x98448` is not assigned a containing function by Ghidra. Separately, runtime breakpoint observed entry from `shaderapidx9.dll+0x13F08` with pending words `0x53E90000,0x0C,0xF29D0010,3,0x500,0x2D0,0x1400,0x0F00`; mapping these to the wrapper's formal source/destination slots is ABI-sensitive because the wrapper reorders pushes before its callee. |
| `FUN_1002CEC0` (`0x2CEC0`) | `0x2CF50` | source = interface vtable `+0x9C` return; selector = vtable `+0x68` return; destination = heap buffer allocated as dimensionA × dimensionB × 3; destination selector = 2; arg5 = dimensionA; arg6 = dimensionB; args7/8 = 0 | Dimensions come from calls through object `param_1` vtable `+0x60/+0x64`. This is a verified CPU allocation plus interface-supplied source pointer/selector; interface semantics are unknown. Not linked to export. |
| `FUN_10079140` (`0x79140`) | `0x792FA`, inside per-element loops | source = vtable `+0x94` result; source selector = object field `this+0x18`; destination = `_Src` scratch buffer plus offset from `FUN_10078FA0`; destination selector = requested `param_1`; arg5 = `local_c`; arg6 = `local_8`; args7/8 = 0 | Converts each element across nested dimensions/layers; advances pointers by byte sizes returned by `0x496F0`. Scratch source is supplied by `MemAllocScratch`; later output is copied into an object-owned cached buffer. Caller appears texture/data conversion oriented, not Movie Maker by static evidence. |
| `FUN_1007BC90` (`0x7BC90`) | `0x7BDCB` | Push order yields source = `EDI` temporary allocation (width × height × 4); selector = 0; destination = object field `param_1[0x16]`; destination selector = `param_1[0x13]`; args5/6 = `param_1[0x14]/[0x15]`; args7/8 = 0 | Builds a temporary descriptor with pointer `EDI`, dimensions `param_1[0x14]/[0x15]), fixed value 4, calls `FUN_1004E840`, then converts into object buffer and frees temporary storage. Caller is a CPU buffer/texture setup path. Static analysis does not associate it with Movie Maker. |

### Call graph and Movie Maker relationship

```text
ifm.dll+0x2B67BC (vtable slot +0x194)
  → shaderapidx9.dll+0x13F08 (indirect call target observed at runtime)
  → materialsystem.dll+0x0B5E0 (runtime hit; static wrapper)
  → materialsystem.dll+0x48EC0 (direct call at 0x0B603; runtime hit)
  → selector-based CPU conversion dispatch (static)
  → 0x48D90 converter lookup; selector key 0x0C → 0x44200 (static)
  → 4-byte-to-3-byte CPU repack (runtime write-watch at 0x44224)
  → ifm.dll MovieDoc CPU buffer (runtime destination pointer)
```

The runtime log verifies the wrapper target, implementation entry, and write to the Movie Maker CPU buffer; a runtime stack included `materialsystem.dll+0x49309`, a return site inside the generic conversion routine. Static disassembly verifies that `0x48EC0` can reach `0x44200` through the conversion dispatch table. Together this makes the conversion routine's participation in the Movie Maker transfer **Strongly supported**. Exact branch selection for that capture and the source's producer are not proven by a recorded runtime selector snapshot. The interface slot and shader call chain are documented in [MOVIE_MAKER_CALL_GRAPH.md](MOVIE_MAKER_CALL_GRAPH.md); do not equate any of these CPU routines with a GPU readback API.

## Buffer provenance and open questions

- For the `0x44200` call reached through the generic converter, static analysis proves only that its input pointer is argument 1 to the chosen converter and its output pointer is argument 3; it does not locate the allocation or producer of the specific Movie Maker runtime source pointer.
- The separate `FUN_1007BC90` caller allocates a temporary `width × height × 4` CPU buffer and passes it as source selector 0. Its descriptor is set up by `FUN_1004E840`; what fills the allocation and whether a GPU read occurs inside that call is unresolved.
- `FUN_1002CEC0` allocates `dimensions × 3` output memory and obtains its source pointer through an object vtable method. Neither the producer nor GPU identity is established.
- Runtime watch proves the 4-to-3 loop writes the Movie Maker CPU buffer and runtime stack links it to shaderapidx9/Material System. The watched source was stack memory during one capture; its upstream writer is not identified.
- The code selects bytes +2,+1,+0 and drops +3 for each four-byte unit. Whether +3 is alpha, padding, or another component is **Unknown**. No pixel-format assignment follows from byte counts alone.
- Synchronization, resource identity, GPU API, row orientation/pitch, and exact channel meanings remain **Unknown**. No vtable/interface offset inside `0x48EC0` itself was found.

## Focused follow-up: FUN_1004E840 temporary buffer (2026-11-02)

`FUN_1004E840` (`materialsystem.dll+0x4E840`, range `0x4E840–0x4EAC6`) is `__cdecl`, takes one pointer to a 32-bit-word descriptor, and returns a status-like value in AL/EAX. The only direct callers found are `FUN_1007BC90` at `0x7BD82` and `FUN_1007AAF0` at `0x7AF85`. No full analysis/import was repeated.

For `FUN_1007BC90`, a temporary allocation is made first with byte count `param_1[0x14] * param_1[0x15] * 4`. The descriptor passed by address at `0x7BD82` is assembled from interface-returned/source fields, object dimensions/strides, flags, constants and that allocation pointer. The callee checks source/destination dimensions and row parameters, requires power-of-two dimensions on its non-identical path, derives reduced dimensions and a float-processing context, then selects processing based on descriptor flags. It does not always use the temporary allocation: when descriptor flag bit 1 (`0x2`) is set and bit 0 (`0x1`) is clear, it allocates an internal `param_1[4] * param_1[2] * param_1[3] * 4` byte temporary, zeroes all bytes with `memset`, and passes that pointer as the final argument to a global function-pointer table entry at `0x101020B0 + 4*index`. It later frees that internal allocation. The outer descriptor buffer from `0x7BC90` is instead passed as a descriptor field to the selected processor; its exact reads/writes depend on that processor and flags.

Simplified verified control flow:

```text
FUN_1007BC90:
  outer = alloc(object_extent_A * object_extent_B * 4)
  descriptor = { dimensions/strides from object, interface-returned source fields,
                 flags/constants, outer }
  ok = FUN_1004E840(&descriptor)
  if ok:
      FUN_10048EC0(outer, 0, object_buffer, object_selector,
                   object_extent_A, object_extent_B, 0, 0)
  free(outer)

FUN_1004E840(&descriptor):
  validate descriptor and derive mip/reduced extents
  if flags request internal temporary:
      scratch = alloc(extent0 * extent1 * extent2 * 4)
      memset(scratch, 0, size)
  processor = global_function_table[index]
  processor(&derived_context, &descriptor, derived extents, ... , scratch)
  free(scratch)
  return status
```

The descriptor's source and dimensions in `0x7BC90` originate partly from a virtual method at the object's vtable slot `+0x94`, and other dimensions/strides from object fields. Its post-call use is direct: if `FUN_1004E840` succeeds, `outer` is source argument 1 (selector 0) to `FUN_10048EC0`, which converts into the object's stored buffer; then `outer` is freed. Thus **Verified**: the outer four-byte-per-element allocation is a temporary CPU conversion source filled/processed through `FUN_1004E840`'s selected processing path, then consumed by `0x48EC0`. The internal zeroed allocation, when enabled, is a separate scratch/output buffer.

`FUN_1007AAF0` provides the structural context: it walks nested object dimensions/layers/mips, gets source and destination pointers and extent metadata from object virtual calls (notably slots `+0x94` and `+0x7C`), builds a descriptor, and selects `FUN_1004ECD0`, `FUN_1004EAD0`, or `FUN_1004E840` by an object field/selector. This strongly supports that these routines belong to a mip/subresource or texture-data processing subsystem. The called processor inside `0x4E840` is indirect through `DAT_101020B0); the target is not resolved for the specific `0x7BC90` descriptor without its runtime flag/index values. The direct data origin behind the interface method and whether any indirect processor reads a GPU resource remain **Unknown**.

### Relevance to Movie Maker

- **Verified:** `0x7BC90` calls `0x4E840`, then passes its outer temporary to `0x48EC0`, whose static dispatch can reach `0x44200`.
- **Unrelated as a statically identified caller path:** `0x7BC90` and `0x7AAF0` are object/mip/subresource conversion paths; no static edge links either caller to the Movie Maker frame-output functions or shader dispatch callsite.
- **Unknown:** whether a common internal processor or source pointer is also used during Movie Maker export. The existing export trace did not capture this descriptor, flags, or the indirect processor target.

The producer is therefore bounded to a descriptor-selected texture/subresource processing function, but its concrete target and source resource are unresolved. Further attribution to the Movie Maker runtime source requires runtime values/resource identity, so this focused static task stops here.
