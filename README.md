<div align="center">

<img src="assets/logo.svg" alt="NesLifter" width="720">

**Static NES → .NET recompiler.** Feed it a `.nes` file, get a runnable .NET game and readable C#.

[![CI](https://github.com/dpteam/NesLifter/actions/workflows/ci.yml/badge.svg)](https://github.com/dpteam/NesLifter/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4)](#requirements)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)
[![Mappers](https://img.shields.io/badge/mappers-25-success)](#supported-mappers)

</div>

---

## What it does

NesLifter reads an iNES / NES 2.0 image, disassembles the 6502 program, and
translates every reachable instruction into equivalent C#. The result is
compiled with Roslyn into a self-contained .NET assembly that renders with
WinForms and plays through `winmm`.

There is no 6502 interpreter in the hot path: control flow becomes real C#
`goto` labels, and a dispatch table handles `RTS`/`RTI`/indirect jumps. An
interpreter fallback still exists, but only for addresses the static analysis
could not prove reachable — and it reports them back so the next lift can
inline them.

```
   game.nes
      │
      ├─ iNES / NES 2.0 header ──────────► mapper, mirroring, RAM sizes, CRC32
      │
      ├─ Board.Power() ──────────────────► power-on bank layout
      │
      ├─ recursive-descent 6502 analyser ► instructions, labels, functions
      │                                    (decoding through the real bank map)
      │
      ├─ Lifter ─────────────────────────► C# : CPU, PPU, APU, mapper, WinForms
      │
      └─ Roslyn (embedded PDB) ──────────► game.dll + game.runtimeconfig.json
```

## Quick start

```powershell
git clone https://github.com/dpteam/NesLifter.git
cd NesLifter
dotnet build

# Prove the whole toolchain works. No ROM needed.
dotnet run -- --selftest

# Lift a real cartridge.
dotnet run -- "Super Mario Bros. (World).nes"

# Play the result.
dotnet "nes_lifted_output\Super Mario Bros. (World)\Super Mario Bros. (World).dll"
```

`--selftest` builds synthetic cartridges for sixteen mapper families, lifts
them, compiles them, then executes each generated assembly headlessly and
checks it reaches a frame budget without trapping:

```
  PASS  nrom-32k              mapper 0    -> headless: frames=28 instructions=187527
  PASS  mmc1-256k-chrram      mapper 1    -> headless: frames=23 instructions=154991
  PASS  mmc3-256k-chr         mapper 4    -> headless: frames=24 instructions=164331
  PASS  vrc6-128k-chr         mapper 24   -> headless: frames=15 instructions=103877
  ...
Self-test passed: 16/16 cartridges lifted and executed.
```

## Requirements

| | |
|---|---|
| OS | Windows 10 / 11 (the generated game uses WinForms and `winmm`) |
| SDK | .NET 10 SDK |
| Runtime | .NET 10 Desktop Runtime (to *run* a generated game) |

No third-party native dependencies. The only NuGet package is
`Microsoft.CodeAnalysis.CSharp`, used for compilation.

## Command line

```
NesLifter <rom.nes|folder> [options]
NesLifter --selftest [options]
```

| Option | Description |
|---|---|
| `-i, --input <path>` | Input `.nes` file or folder |
| `-o, --output <dir>` | Output directory (default `nes_lifted_output`) |
| `-r, --recursive` | Recurse into sub-folders |
| `--no-compile` | Generate C# only, skip Roslyn |
| `--no-source` | Do not keep the generated C# |
| `--keep-source` | Keep the generated C# (default) |
| `-v, --verbose` | Verbose pipeline output, PDB paths, diagnostics |
| `--selftest` | Lift + compile + execute synthetic ROMs for every mapper |
| `--frames <n>` | Frame budget for `--selftest` execution (default 30) |
| `--no-run` | Compile but do not execute during `--selftest` |
| `--keep` | Keep `--selftest` artifacts |
| `--version` | Version and supported mapper list |
| `-h, --help` | Help |

Generated games accept one flag of their own:

```powershell
dotnet game.dll                     # windowed
dotnet game.dll --headless 600      # 600 frames, no window, exit code 0 on success
```

## Output layout

```
nes_lifted_output/
└── Super Mario Bros. (World)/
    ├── Super Mario Bros. (World).dll             # the lifted game
    ├── Super Mario Bros. (World).pdb             # embedded-symbol PDB
    ├── Super Mario Bros. (World).runtimeconfig.json
    ├── analysis.txt                              # mapper, vectors, coverage
    ├── dynamic_targets.txt                       # manually forced addresses
    ├── dynamic_targets.log                       # runtime-discovered addresses
    └── src/
        └── Game.generated.cs                     # the generated C#
```

`analysis.txt` is the first place to look when a game misbehaves:

```
[Cartridge]
Mapper         : 4 (MMC3)
Mapper support : full
Board summary  : 8 KB PRG / 1-2 KB CHR banking, scanline IRQ, 4 mirroring modes.
Mirroring      : vertical
[Analysis]
Entry         : 0x8000
Instructions  : 18142
Code bytes    : 61204 (74.7% of PRG)
Unknown ops   : 0
Indirect jumps: 37
```

## Supported mappers

Analysis and runtime emission are generated from the same descriptor, so a
mapper cannot be half-supported.

| # | Board | Notes |
|---|---|---|
| 0 | NROM | fixed 16/32 KB PRG |
| 1 | MMC1 | serial shift register, 4 mirroring modes, WRAM disable |
| 2 | UxROM | 16 KB switch at `$8000`, last bank fixed |
| 3 | CNROM | 8 KB CHR banking |
| 4 | MMC3 | 8 KB PRG, 1/2 KB CHR, scanline IRQ, PRG/CHR swap bits |
| 7 | AxROM | 32 KB switch, single-screen mirroring |
| 9 | MMC2 | latch-based CHR switching (Punch-Out!!) |
| 11 | Color Dreams | PRG + CHR from one register, bus conflicts |
| 13 | CPROM | 16 KB CHR RAM, 4 KB banking |
| 21 | VRC4a/c | 8 KB PRG, 1 KB CHR, IRQ |
| 22 | VRC2a | 16 KB PRG, 2 KB CHR |
| 23 | VRC4e/f | VRC4 variant |
| 24 | VRC6a | + 3 expansion sound channels |
| 25 | VRC4b/d | VRC4 variant |
| 26 | VRC6b | VRC6 variant |
| 34 | BNROM / NINA-001 | 32 KB PRG, optional 4 KB CHR |
| 66 | GxROM | PRG + CHR, bus conflicts |
| 69 | Sunsoft FME-7 | command banking, 1 KB CHR, IRQ, 8 KB WRAM |
| 71 | Camerica | UxROM + mirroring bit, bus conflicts |
| 79 | NINA-03/06 | fixed PRG, CHR banking, mirroring bit |
| 87 | Jaleco CHR | fixed PRG, CHR banking |
| 88 | Namco 118 | fixed PRG, CHR banking, mirroring |
| 206 | Namco 118 / DxROM | MMC3 clone without IRQ |
| 232 | Codemasters Quattro | 4 × 128 KB blocks |

Anything else falls back to a flat PRG layout and is reported as
`Mapper support : fallback`. See [`docs/MAPPERS.md`](docs/MAPPERS.md) for the
implementation notes and the remaining gaps.

## How the generated game looks

The output is ordinary, readable C#. One label per 6502 address, one statement
per instruction, with the original disassembly in a comment:

```csharp
L8000:
{
    Runtime.LastPC = 0x8000; Runtime.TraceInstruction(0x8000);
    Runtime.InsCount++; Runtime.AdvanceCycles(7);
    if (Runtime.CheckNmi(0x8000)) goto L8100;
    if (Runtime.CheckIrq(0x8000)) { Runtime.DispatchTarget = Runtime.IrqVector; goto Dispatch; }
    // SEI
    Runtime.P = (byte)(Runtime.P | 0x04);
    goto L8001;
}
```

Because the PDB is emitted, a crash gives a real line number inside
`Game.generated.cs` instead of `<unknown>`.

## Dynamic targets

Recursive descent cannot see code reached only through a computed jump. Three
mechanisms cover that gap:

1. **`dynamic_targets.txt`** — force addresses into the analysis.
   ```
   ; 8231 / 0x8231 / $8231 all work
   8231          ; title screen setup
   ```
2. **`dynamic_targets.log`** — the generated game records every address it had
   to interpret. Re-running the lifter picks the file up and inlines them.
3. **Interpreter fallback** — anything still unknown executes in a built-in 6502
   interpreter at runtime, so the game keeps playing instead of trapping.

## Project layout

```
NesLifter.csproj
├── Core/                 cartridge model and CPU-side analysis
│   ├── InesLoader.cs     iNES / NES 2.0 parsing, CRC32
│   ├── CartInfo.cs       cartridge description
│   ├── CartMapping.cs    page-table memory map (2 KB PRG / 1 KB CHR pages)
│   ├── ProgramImage.cs   power-on address space view for the analyser
│   ├── Cpu6502.cs        256-entry opcode table
│   ├── Disassembler.cs   recursive-descent analyser
│   └── Boards/           one board per mapper family
├── Mappers/              MapperCatalog: identity + family metadata
├── Lifting/
│   ├── Lifter.cs         emits the whole game
│   └── MapperRuntimeEmitter.cs  emits a specialised MapperStub
├── Compilation/          Roslyn wrapper
├── Host/                 CLI, pipeline, self-test
├── Common/               file helpers, synthetic ROM builder
├── tests/                xUnit tests
├── tools/Branding/       logo and icon generator
├── assets/               SVG + PNG + ICO
└── docs/                 architecture, mappers, troubleshooting
```
## Documentation

- [`ROADMAP.md`](ROADMAP.md) — what is done and what is next, easiest first
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — how a lift works, stage by stage
- [`docs/MAPPERS.md`](docs/MAPPERS.md) — per-board implementation notes
- [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md) — black screen, wrong graphics, traps
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — build, test, add a mapper
- [`CHANGELOG.md`](CHANGELOG.md) — release notes

## Known limitations

- **One bank layout is analysed.** The analyser sees the power-on mapping; other
  switchable banks run through the interpreter. Bank-aware analysis is the top
  item in the [roadmap](ROADMAP.md#stage-4--static-analysis-depth-next-big-win).
- **APU sweep and DMC are missing.** Music plays, but some effects are silent.
- **No save states, no battery save persistence yet.**
- **Windows only** for the generated game (WinForms + `winmm`).
- **Large ROMs produce large C#.** A 512 KB PRG game generates tens of MB of
  source; it compiles, but the file is not pleasant to open.

## Legal

NesLifter contains no ROMs and no emulator BIOS files. Use it only with
cartridge images you are legally entitled to. The generated game embeds the
PRG and CHR data of the cartridge you feed it — treat that output with the same
care as the input.

## License

GPL-3.0. See [`LICENSE`](LICENSE).

<div align="center">
<sub>Previously known as <em>NES2ILRecomp</em> / <em>NesToDotNetRecompiler</em>.</sub>
</div>
