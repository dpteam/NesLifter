# Architecture

How one lift works, stage by stage.

---

## 1. Cartridge model

`InesLoader` parses the 16-byte iNES header (and NES 2.0 extensions) into a
`CartInfo`:

- mapper number, submapper, mirroring, battery, region
- PRG / CHR ROM byte arrays (padded with `$FF` when the file is truncated)
- PRG / CHR RAM sizes (real values for NES 2.0, mapper conventions otherwise)
- CRC32 of PRG, CHR and both combined

Header sanitisation reproduces the well-known garbage headers (`DiskDude`,
`demiforce`, `Ni03`) so those dumps load instead of reporting a bogus mapper.

`CartMapping` is a page table, not a pointer model: 32 PRG pages of 2 KB and 8
CHR pages of 1 KB, each holding `(memory, adjust, isRam)`. It mirrors FCEUmm's
`cart.c` semantics:

```
SetPrg16(0x8000, 3)   →  pages 16..23 point at PRG, adjust = 3*0x4000 - 0x8000
ReadPrg(0x8000)       →  prg[adjust + 0x8000]
```

The point of this indirection is that **banking is data, not code**. A board
only ever writes page entries; readers do not care which mapper produced them.

## 2. Boards

`Core/Boards/` holds one class per mapper family:

| Class | Mappers |
|---|---|
| `NromBoard` | 0 |
| `Mmc1Board` | 1 |
| `UxRomBoard` | 2 |
| `CnromBoard` | 3 |
| `Mmc3Board` | 4, 206 |
| `AxRomBoard` | 7 |
| `Mmc2Board` | 9 |
| `GxRomBoard` | 11, 66 |
| `CpromBoard` | 13 |
| `VrcBoard` | 21-26 |
| `BnromBoard` | 34 |
| `Fme7Board` | 69 |
| `CamericaBoard` | 71 |
| `NinaBoard` | 79, 87, 88 |
| `QuattroBoard` | 232 |

`BoardFactory` maps mapper number → board and reports whether the mapper is
actually implemented. Unknown mappers get `NromBoard` as a best-effort fallback
and are flagged in the report.

`MapperCatalog` in `Mappers/` holds the *metadata*: board name, banking family,
default WRAM size, battery, bus conflicts, IRQ capability. Both the analysis
side (`BoardFactory`) and the code-generation side (`MapperRuntimeEmitter`)
read from it, so they cannot disagree about what a mapper does.

## 3. Program image

The analyser must decode the bytes the 6502 would actually see, so it reads
through `ProgramImage` rather than indexing `PrgRom` directly:

```csharp
ProgramImage image = ProgramImage.Create(rom, mapping);   // mapping already powered on
int offset = image.ToOffset(0xC000);                      // → real PRG offset, or -1
```

For a mapper-aware image, `ToOffset` consults the page table and returns `-1`
when the page is WRAM or unmapped. That single change is what makes banked
cartridges analyse correctly: without it, a 256 KB UxROM game was analysed as if
its last 32 KB were mapped at `$8000`.

## 4. Analysis

`Disassembler` performs recursive descent:

1. Seed the worklist with the RESET, NMI and IRQ vectors, plus every forced
   address from `dynamic_targets.txt`.
2. Decode the opcode at the head, record it, and follow its successors
   (branch target, jump target, fall-through).
3. `JMP ($xxxx)` reads the pointer from the image; if it resolves to valid PRG,
   that address becomes a successor.
4. Everything below `$8000` and the vector table itself is discarded.

The output is an `AnalysisResult`:

- `Instructions` — `SortedDictionary<ushort, Instruction>`
- `Labels` — every decoded address, sorted
- `Functions` — JSR/JMP targets plus the vectors
- `UnknownOpcodes` — bytes with no table entry
- `IndirectJumps` — every `JMP (indirect)` site
- `DynamicTargets` — forced addresses and resolved indirect targets

## 5. Code generation

`Lifter.Generate()` emits one C# file containing:

| Section | Emitted by | Contents |
|---|---|---|
| `Program` | `AppendProgram` | entry point, windowed and headless paths |
| `Runtime` | `AppendRuntime` | CPU registers, ROM data, dispatch, NMI/IRQ, throttle, input |
| `Memory` | `AppendMemory` | bus decode, `$2000`/`$4000`/`$6000` windows |
| `Ppu` | `AppendPpu` | registers, VRAM, per-scanline scroll, renderer |
| `Apu` | `AppendApu` | pulse ×2, triangle, noise, envelopes, length counters |
| `MapperStub` | `MapperRuntimeEmitter` | specialised mapper for *this* cartridge |
| `Audio` | `AppendAudio` | `winmm` streaming, mixing, filtering |
| `PpuForm` | `AppendForm` | window, blitting, keyboard input |
| `L{addr}` blocks | `AppendRun` | one block per decoded instruction |

Control flow is emitted as labels and `goto`:

```csharp
L8029:
{
    Runtime.LastPC = 0x8029; ...
    // INC $0000
    ushort rmw = 0x00;
    byte tmp = (byte)(Memory.Read(rmw) + 1);
    Runtime.SetNZ(tmp);
    Memory.Write(rmw, tmp);
    goto L802C;
}
```

`RTS`, `RTI`, `BRK` and unresolved indirect jumps write a target into
`Runtime.DispatchTarget` and `goto Dispatch`, a `switch` over every emitted
label. Addresses missing from that switch fall into `Runtime.Interpret()`, which
executes 6502 directly until it lands on a static label again.

### Why per-family mapper emission

The old emitter wrote one `MapperStub` containing every mapper and dispatched on
`Runtime.Mapper` at every register write. The current emitter asks
`MapperCatalog` for the cartridge's family and emits only that family's code.
A NROM game gets a five-line write handler; an MMC3 game gets the full IRQ
counter. This removes dead branches from the hottest path in the generated game
and makes the generated source actually readable.

## 6. Compilation

`RoslynCompiler` compiles the generated source with:

- references from `TRUSTED_PLATFORM_ASSEMBLIES`
- `OptimizationLevel.Release`, `deterministic: true`
- a **portable PDB** so stack traces point into `Game.generated.cs`
- a suppression list for machine-generated-code noise (`CS0162`, `CS0219`, …)
- full diagnostic reporting: warnings are surfaced, not swallowed

`--verbose` prints the PDB path and the warning count.

## 7. Runtime model

The generated game runs two threads:

- **CPU thread** — executes the lifted blocks, advancing `CpuCycles`, clocking
  the APU and PPU, and honouring NMI/IRQ.
- **Render thread** — takes a snapshot of PPU state at VBlank, renders it to a
  bitmap, blits, and presents at a fixed rate.

The snapshot is a `RenderState` object swapped atomically at VBlank, so the
renderer never observes a half-written VRAM. Per-scanline scroll values are
captured during the frame, which is what makes mid-frame scroll splits (status
bars, SMB's horizon) line up.

Audio runs on a third thread owned by `winmm`: four ring buffers of 512 samples
at 22 050 Hz, filled from the current APU register state.

## 8. Verification

`--selftest` closes the loop:

1. `SyntheticRom` builds a valid iNES image per mapper with a hand-assembled
   program at `$8000`.
2. The full pipeline lifts and compiles it.
3. The resulting assembly runs with `--headless N` and must reach `N` frames
   without trapping.

Any change that breaks analysis, emission, compilation or the CPU/PPU runtime
fails the self-test before it can reach a real cartridge.

Unit tests (`tests/NesLifter.Tests`) cover the layer below: every catalog entry
must have a board, and each board's banking, mirroring, bus-conflict and IRQ
behaviour is asserted directly.
