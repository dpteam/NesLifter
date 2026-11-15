# NesLifter roadmap

Ordered from easiest to hardest. Each stage is shippable on its own and leaves
the tool in a working state.

Legend: `[x]` done · `[~]` partial · `[ ]` planned

---

## Stage 0 — Foundation (done)

Cheap, high-leverage work that makes everything after it verifiable.

- [x] .NET 10 toolchain, `global.json`, `Directory.Build` defaults
- [x] Deterministic `.editorconfig` and `.gitignore`
- [x] Roslyn compilation with embedded portable PDB and real diagnostics
- [x] `--selftest`: synthetic ROMs for every mapper, lifted, compiled **and executed**
- [x] xUnit test project covering boards, loader, disassembler, program image
- [x] Headless mode (`--headless N`) so generated games run in CI
- [x] Branding: vector logo, multi-resolution `.ico`, social preview

---

## Stage 1 — Correctness of the mapper layer (done)

The mapper is the single largest source of "it runs but the graphics are wrong".

- [x] One catalog (`MapperCatalog`) as the single source of truth for mapper
      identity, board name, family, bus conflicts and IRQ capability
- [x] Analysis-side boards and runtime emission derived from the same family
- [x] NROM, MMC1, UxROM, CNROM, MMC3, AxROM, MMC2, GxROM, BNROM/NINA-001,
      FME-7, Camerica, NINA-03/06, Jaleco CHR, Namco 118, CPROM, VRC2, VRC4,
      VRC6, Quattro
- [x] Bus conflicts modelled on the boards that have them (GxROM, Camerica,
      NINA, CPROM) and absent where FCEUmm omits them (CNROM)
- [x] Scanline IRQ for MMC3, VRC4, VRC6, FME-7
- [x] Mapper-aware disassembly: the analyser decodes through the power-on bank
      layout instead of assuming a flat image
- [x] Per-family code emission: a generated game carries only its own mapper

---

## Stage 2 — PPU accuracy (done, with known gaps)

- [x] Sprite-0 hit with left-edge clipping
- [x] Sprite overflow flag (sticky, cleared by `$2002` read)
- [x] Eight-sprites-per-scanline budget
- [x] Background and sprite left-edge clipping (`$2001` bits 1/2)
- [x] Mid-frame scroll capture (per-scanline scroll table)
- [x] MMC2 CHR latching from the pattern fetch path
- [~] Sprite priority against the *real* background pixel (currently uses the
      captured background layer, which is correct for the common case)
- [ ] Per-dot (cycle-accurate) PPU: 341 dots, A12 filter, sprite evaluation at
      dot 65/257

---

## Stage 3 — APU accuracy (partial)

- [x] Pulse ×2, triangle, noise with envelope and length counters
- [x] Frame counter, linear counter, sweep skeleton
- [x] DC-blocking and low-pass filtering
- [ ] Sweep units (currently ignored)
- [ ] DMC channel (sample playback)
- [ ] Frame-counter IRQ
- [ ] Per-cycle mixing at 1.79 MHz instead of per-sample approximation
- [ ] VRC6 expansion audio (3 channels) and FME-7/Namco 163 envelopes

---

## Stage 4 — Static analysis depth (next big win)

Today the lifter analyses **one** bank layout: the one visible at power-on.
Every other switchable bank is invisible to the analyser and is handled at
runtime by the interpreter. This is the single largest accuracy gap.

- [ ] **Bank-aware analysis.** Key instructions by `(bank, address)` instead of
      `address`, run recursive descent per PRG bank, and emit per-bank labels
      (`L{bank}_{addr}`) with a dispatch table keyed on `bank << 16 | pc`
- [ ] Bank-switch tracking: record the mapper writes that lead to each bank so
      cross-bank `JMP`/`JSR` edges can be resolved statically
- [ ] Code/data separation: mark reachable bytes, treat everything else as data,
      and stop recursive descent at `BRK`/`RTS` when the next byte is not a
      known entry point
- [ ] Jump-table recovery for `JMP ($xxxx)` / `JMP (table,X)` patterns
- [ ] Dataflow pass for zero-page pointer targets (currently heuristic)
- [ ] Coverage report per bank: lifted bytes / total bytes, so "what is still
      interpreted" is measurable

---

## Stage 5 — Runtime architecture

- [ ] Direct-threaded dispatch instead of `goto` chains for cold blocks
- [ ] Static-link the generated game as a single-file executable
- [ ] Save states (serialise RAM, PPU, APU, mapper and CPU state)
- [ ] Battery-backed SRAM persistence (`.sav` next to the assembly)
- [ ] Rewind buffer
- [ ] Optional `net10.0` (no WinForms) build target: SDL-style or console-only
      output for Linux/macOS

---

## Stage 6 — Emulation-grade fidelity (hard)

- [ ] Cycle-exact 6502 (per-cycle bus accesses, RMW double writes, dummy reads)
- [ ] CPU interrupt timing: NMI hijacking, IRQ at instruction boundaries only
- [ ] PPU open bus and decay
- [ ] Four-screen VRAM and mapper-controlled nametable RAM
- [ ] MMC5: ExRAM, extended attributes, vertical split mode, 8×8 multiply
- [ ] Mapper 163/MMC5 sound, Namco 163 wavetable, Sunsoft 5B
- [ ] Famicom Disk System BIOS and disk I/O
- [ ] VS. System / PlayChoice-10 arcade variants

---

## Stage 7 — Tooling and ecosystem (hard)

- [ ] Diff harness: run the generated game and a reference emulator in lockstep,
      compare RAM + framebuffer per frame, report the first divergence
- [ ] Test-ROM suite integration (nestest, blargg, ppu_vbl_nmi, sprite_hit)
- [ ] Symbol/`.nl` sidecar so traces map to readable names
- [ ] Source-level debugging: `#line` directives back into the disassembly
- [ ] WebAssembly output target
- [ ] Rewrite the lifter as a proper IR (SSA-ish) with peephole optimisation
      instead of string concatenation, then emit C# from the IR

---

## Non-goals

- Bit-exact cycle accuracy for every board. The target is "runs correctly",
  not "passes every hardware test".
- Bundling ROMs, BIOS files or copyrighted data.
- A full emulator. The interpreter fallback exists for coverage, not for
  shipping a general-purpose emulator.
