# Changelog

All notable changes to NesLifter. The project was previously published as
NES2ILRecomp / NesToDotNetRecompiler.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning is [Semantic Versioning](https://semver.org/).

---

## [0.6.0] — 2026-11-15

Mapper coverage, runtime correctness and a real verification loop.

### Added

- **Mapper catalog** (`MapperCatalog`) as the single source of truth for mapper
  identity, board name, banking family, WRAM size, battery, bus conflicts and
  IRQ capability.
- **Boards** for MMC3 (4, 206), AxROM (7), MMC2 (9), GxROM (11, 66), CPROM (13),
  VRC2/VRC4/VRC6 (21-26), BNROM/NINA-001 (34), Suntech FME-7 (69),
  Camerica (71), NINA-03/06 (79, 88), Jaleco CHR (87) and Quattro (232).
  Total supported mappers: 25.
- **`MapperRuntimeEmitter`**: per-family mapper emission. A generated game now
  carries only its own mapper instead of a branch ladder over all of them.
- **`ProgramImage`**: mapper-aware address space for the analyser. Recursive
  descent now decodes through the power-on bank layout instead of assuming a
  flat image, which fixes analysis of every banked cartridge.
- **`--selftest`**: builds synthetic cartridges for 16 mapper families, lifts
  them, compiles them, then executes each generated assembly headlessly.
- **Headless mode** for generated games: `dotnet game.dll --headless 600`,
  with meaningful exit codes.
- **xUnit test project** (50 tests): board banking, mirroring, bus conflicts,
  IRQ behaviour, iNES loading, disassembler bounds, program image mapping.
- **Branding**: vector logo, square mark, multi-resolution `.ico`, GitHub social
  preview, all generated from `tools/Branding`.
- Documentation: `ROADMAP.md`, `docs/ARCHITECTURE.md`, `docs/MAPPERS.md`,
  `docs/TROUBLESHOOTING.md`, `CONTRIBUTING.md`, rewritten `README.md`.

### Changed

- Retargeted to **.NET 10** (`net10.0-windows`), with `global.json` pinning the
  SDK feature band.
- Roslyn compilation now emits a **portable PDB** with real source paths, is
  deterministic, reports warnings instead of swallowing them, and suppresses
  only machine-generated-code noise.
- Analysis report rewritten: mapper support status, board summary, code-byte
  coverage as a percentage of PRG, region and mirroring names.
- `dynamic_targets.txt` / `dynamic_targets.log` parsing tolerates trailing
  comments and all three address notations.
- `-v` is now `--verbose`; `--version` is its own flag.
- Pipeline reports human-readable sizes and the hot-reload fallback path.

### Fixed

- **MMC3 PRG swap used 16 KB bank numbers** where 8 KB were required, so the
  fixed windows pointed at the wrong banks.
- **MMC1 write filter** swallowed serial writes when no CPU clock was supplied,
  making analysis and runtime disagree. Removed on both sides.
- **Bus conflicts** were applied inconsistently: CNROM ANDed writes in the
  runtime but not in the analyser. CNROM now ignores conflicts on both sides
  (matching FCEUmm); GxROM, Camerica, NINA and CPROM apply them on both.
- **VRC6 board** wrote past the end of its PRG register array.
- **Generated games** lost CHR RAM writes: `$0000-$1FFF` writes now go through
  the mapper instead of being silently dropped.
- **`$2002` read** cleared the sprite-overflow bit as well as VBlank, so the
  overflow flag could never be observed.

### PPU

- Sprite overflow flag, sticky until `$2002` is read.
- Eight-sprites-per-scanline budget.
- Background and sprite left-edge clipping (`$2001` bits 1 and 2), including in
  the sprite-0 hit test.
- MMC2 CHR latching driven from the background pattern fetch path.

### Removed

- `Mappers/Nrom.cs`, `Mappers/MMC1.cs`, `Mappers/UxROM.cs`, `Mappers/CNROM.cs`,
  `Core/MapperFactory.cs`, `Core/Mappers/IMapper.cs` — the legacy mapper layer
  superseded by `Core/Boards` plus `MapperCatalog`.

---

## [0.5.0] — earlier

- Migrated from a single-file .NET Framework proof of concept to a multi-project
  .NET solution with Roslyn compilation.
- Added `CartInfo`, `CartMapping` (FCEUmm-style page tables) and the first
  boards: NROM, MMC1, UxROM, CNROM.
- Split the lifter into sections: runtime, memory, PPU, APU, mapper, audio, form.
- Added the interpreter fallback and `dynamic_targets` round-tripping.

## [0.1.0] — initial proof of concept

- Single `Program.cs`, .NET Framework, `CSharpCodeProvider`.
- Super Mario Bros. only.
