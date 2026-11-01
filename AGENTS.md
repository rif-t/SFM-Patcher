# SFM Renderer project instructions

- Inspect the relevant source and the current SFM installation before changing code or configuration.
- SFM is 32-bit: all in-process native modules and dependencies must target x86.
- Treat the viewport and Movie Maker export as separate paths until an exported PNG proves otherwise.
- Do not guess GPU formats, resource identities, interface offsets, or call sequences. Label evidence, hypotheses, and unknowns.
- Preserve existing DXVK, ReShade, REST Enhanced, presets, and user configuration. Back up files before any installer changes.
- Keep GPU readback and Material System experiments isolated. Instrument first; do not mutate GPU resources until the exported resource and synchronization are established.
- Validate processing by inspecting actual exported frames, including dimensions and pixel changes. A viewport image is not export validation.
- Use official upstream documentation/source when verifying external APIs. Use Graphify and Context7 when configured and relevant; do not claim either is installed or usable without checking.
- Record material architecture decisions and current implementation status in `docs/`.
