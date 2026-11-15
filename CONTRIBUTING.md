# Contributing to NesLifter

## Setup

```powershell
git clone https://github.com/dpteam/NesLifter.git
cd NesLifter
dotnet build NesLifter.slnx
dotnet test
dotnet run -- --selftest
```

Requirements: .NET 10 SDK, Windows (the generated game uses WinForms).

`global.json` pins the SDK feature band; `rollForward: latestFeature` lets a
newer patch build work.

## Before you open a PR

All three must pass:

```powershell
dotnet build NesLifter.slnx -warnaserror
dotnet test
dotnet run -- --selftest
```

`--selftest` is the important one: it lifts, compiles **and executes** a
synthetic cartridge for every supported mapper. If it passes, the whole chain is
intact.

## Code style

`.editorconfig` is authoritative. In short:

- 4 spaces, no tabs, CRLF, final newline, no trailing whitespace
- `System` usings first
- braces on their own line
- explicit types over `var` (the codebase reads as C# from 2010s .NET, on purpose:
  the generated code is the artefact, and the lifter should be equally legible)
- XML doc comments on public types and non-obvious members

Comments should explain **why**, not **what**. Hardware quirks especially:
reference the NESdev page or the emulator behaviour you are matching.

## Adding a mapper

This is the most common contribution. The full checklist is in
[`docs/MAPPERS.md`](docs/MAPPERS.md#adding-a-mapper). Summary:

1. `Mappers/MapperCatalog.cs` — add a `MapperDescriptor` (id, board name,
   family, one-line summary, WRAM size, battery, bus conflicts, IRQ).
2. `Core/Boards/` — implement the board. Reuse `LatchBoard` for simple latch
   boards.
3. `Core/Boards/BoardFactory.cs` — register the builder.
4. `Lifting/MapperRuntimeEmitter.cs` — emit the same behaviour for the runtime.
   A mapper is not done until both sides agree.
5. `Common/SyntheticRom.cs` — add a `Spec` to `DefaultMatrix`.
6. `tests/NesLifter.Tests/BoardTests.cs` — assert banking, mirroring, bus
   conflicts and IRQ behaviour directly.

Do not guess register layouts. Use NESdev's mapper page and cross-check against
FCEUmm or Mesen source.

## Changing the analyser or the lifter

The risk here is silent regression: a change that makes one game better and ten
worse. Always run `--selftest` with a real frame budget, not just `--no-run`:

```powershell
dotnet run -- --selftest --frames 120 --keep
```

`--keep` leaves the generated sources under
`nes_lifted_output/_selftest/out/<name>/src/` so you can inspect what changed.

## Changing the PPU or APU

Include in the PR description:

- which game or test ROM showed the difference
- what the old behaviour was and what the new behaviour is
- whether `--selftest` still passes

For PPU work, the per-scanline scroll capture and the `RenderState` snapshot are
the two places most likely to regress. Both are documented in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Commit messages

Imperative, present tense, one logical change:

```
Add VRC6 mapper with scanline IRQ
Fix MMC3 PRG swap using 16 KB bank numbers
Stop treating CNROM writes as bus conflicts
```

Reference the mapper number or the affected subsystem when it helps.

## Generated assets

`assets/*.png` and `assets/icon.ico` are produced from
`tools/Branding/Program.cs`. The SVG sources are maintained by hand and must
stay in sync with the geometry in the generator. After changing either:

```powershell
dotnet run --project tools/Branding
```

Commit the regenerated binaries together with the code change.

## License

By contributing you agree your work is licensed under GPL-3.0, the same license
as the project.
