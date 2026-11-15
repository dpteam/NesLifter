# Changelog

All notable changes to NesLifter. The project was previously published as
NES2ILRecomp / NesToDotNetRecompiler.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning is [Semantic Versioning](https://semver.org/).

---

## [0.9.0] — 2026-11-16

Graphical and terminal front ends, plus the icon and packaging pipeline.

### Added

- **WebView2 GUI.** A WinForms host with an embedded Chromium view, styled with
  NES.css and Font Awesome (both vendored under `web/vendor`, so it works fully
  offline). Cartridge browser with mapper/board/size columns, selection, live
  log streaming, progress bar, package options, folder and icon pickers.
- **Three themes** — Light, Dark, OLED — cycled with the toolbar button or F2,
  persisted between runs. OLED is true black for OLED panels.
- **Soft mathematical interface audio**: short synthesised cues (pentatonic,
  low-passed, 220 ms) for select/start/success/failure. No samples, off by
  default, toggleable.
- **Interactive TUI** with the same capabilities as the GUI: menu, cartridge
  browser, options, live log, results. Arrow keys, Space to select, Enter to act.
- Automatic interface selection: GUI when WebView2 is available, TUI when it is
  not, plain line mode when the console cannot host a full-screen UI. Explicit
  arguments always bypass all three.
- `--gui` to force the GUI, `--no-tui` / `--cli` to never start one.
- `tools/Shot`: window screenshot tool using `PrintWindow`, so docs screenshots
  capture only the app and work over remote desktop.

### Changed

- `Main` is now `[STAThread]`. Without it COM initialises as MTA and every
  WebView2 attempt fails with `RPC_E_CHANGED_MODE`.
- The GUI passes `--disable-gpu --disable-gpu-compositing` to Chromium, so it
  renders correctly over RDP and inside VMs instead of showing a blank surface.
- The WebView2 message bridge accepts both a parsed object and a string, since
  `PostWebMessageAsJson` delivers an object while `PostWebMessageAsString`
  delivers text.
- Interactive UIs default to recursive scanning (ROM libraries are always
  nested); an explicit `-r` still wins.
- A path given on the command line is scanned immediately when the GUI opens.

### Fixed

- VRC2 (mapper 22) failed to compile: the IRQ-free VRC variants emitted neither
  the real `ClockScanline` nor the empty stub.
- The TUI had a duplicate `key` local that shadowed the enclosing scope.

---

Readable code generation: the emitted C# is now written as raw string literals,
and the analyser covers every PRG bank.

### Changed

- **All code emission uses raw string literals.** Every `sb.AppendLine("...")`
  wall in `Lifter`, `MapperRuntimeEmitter` and `GamePackager` was replaced with
  `"""` literals (interpolated `$$"""` where values are substituted). The
  emitted C# is now readable *as C#* in the source, with no `\"` escaping and no
  brace doubling. `Lifter.cs` went from 1669 `AppendLine` calls to 13, and those
  remaining ones are genuinely dynamic (per-block labels, per-offset switch
  cases, ROM byte data).
- `EmitOperation` regrouped by category (loads, transfers, arithmetic, shifts,
  flags, stack, illegal RMW, illegal register ops) with interpolated operands.
- The six illegal read-modify-write opcodes (`DCP`, `ISB`, `SLO`, `RLA`, `SRE`,
  `RRA`) now share `EmitIllegalRmw` instead of six near-identical blocks.
- `AppendOpcodeTable` replaces four copy-pasted 256-entry table loops.
- Opcode tables are data, so they stay single-line and unformatted on purpose.

### Fixed

- **VRC2 (mapper 22) failed to compile.** The IRQ-free VRC variants emitted
  neither the real `ClockScanline` nor the empty stub, so the PPU's
  unconditional call did not resolve.

---

Bank-aware analysis, double-clickable output and game icons.

### Added

- **Bank-aware analysis.** Instructions are now keyed by **PRG ROM offset**
  instead of CPU address, and the analyser walks every PRG bank as if it were
  mapped at `$8000`, merging results by offset. The runtime dispatcher resolves
  a CPU address to an offset through the mapper before jumping, so one lift
  covers every bank of a cartridge. Measured effect on real titles: Castlevania
  +72% decoded instructions, Punch-Out!! +65%, Ninja Gaiden +52%, Zelda +33%.
- **`Game.exe` by default.** Lifted games are packaged as `Game.exe` +
  `Game.dll` + deps/runtimeconfig through `dotnet publish`, so they are
  double-clickable. `--dll` keeps the old assembly-only behaviour.
- **`--full-exe`** (aliases `--self-contained`, `--standalone`): self-contained
  single-file executable that runs with no .NET installed (~50 MB, ~25 s).
- **Game icons from screenshots.** `--capture-icon <seconds>` makes the generated
  game sample its own framebuffer, score each frame by colour variety and
  coverage, and keep the most interesting one as a multi-resolution `.ico`. The
  icon is embedded into the `.exe` on the next lift.
- **`--icon <image>`**: any image format is converted to a 9-size ICO.
- `Imaging/IcoWriter`: shared multi-resolution ICO writer (PNG above 128 px, DIB
  below), letterboxing non-square sources.
- `Imaging/PeIcon`: PE icon replacement through the Win32 resource API, for
  cases where the SDK is not available.
- `Compilation/GamePackager`: packaging modes, SDK detection, graceful fallback
  to a plain assembly when `dotnet publish` is unavailable.

### Changed

- `Disassembler` rewritten around offsets: `Instruction.Offset`,
  `TargetOffset`, `FallthroughOffset`; `AnalysisResult.EntryOffset`,
  `NmiOffset`, `IrqOffset`, `BankViews`.
- Generated block labels are `L_00A4C2` (offset-based) instead of `L8000`.
- Runtime dispatch resolves through `MapperStub.ToOffset`.
- `MapperStub` gained `ToOffset` and `WriteChrByte`; CHR RAM writes now reach
  the mapper instead of being dropped.
- CLI help documents packaging and icon flags.

### Fixed

- `NMI`/`IRQ` handlers are now jumped to directly instead of going through
  `Dispatch`, which removes a dispatch round-trip on every interrupt.
- Bank switch code that used to trap (`KIL/HLT`, `Dynamic dispatch -> $10C2`,
  `Missing branch target`) now executes, because the target bank is lifted.

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
