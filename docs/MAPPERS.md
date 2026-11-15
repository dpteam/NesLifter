# Mapper notes

Per-board implementation notes: what is modelled, what is not, and which
reference behaviour was followed.

Every mapper listed here has **two** implementations that must stay in sync:

- `Core/Boards/*Board.cs` — used during analysis to establish the power-on layout
  and to walk every bank
- `Lifting/MapperRuntimeEmitter.cs` — emitted into the generated game

Both are selected through `Mappers/MapperCatalog.cs`. If you add a mapper, add
it in all three places (plus a board test).

---

## NROM — 0

Fixed. 16 KB PRG is mirrored to both windows; 32 KB is linear. CHR ROM is
read-only, CHR RAM is writable.

**Modelled:** both size variants, CHR RAM detection.

## MMC1 — 1

Serial shift register: five writes of bit 0 accumulate into a register selected
by the address (`$8000`, `$A000`, `$C000`, `$E000`). Writing with bit 7 set
resets the shift register and forces PRG mode 3.

**Modelled:** all four PRG modes, 4 KB and 8 KB CHR modes, all four mirroring
modes, CHR bit 4 as the outer PRG bank, WRAM disable bit (writes ignored, reads
return the high address byte, matching FCEUmm).

**Not modelled:** the 2-cycle RMW write filter. Emulators use it to swallow the
second write of a read-modify-write instruction; NesLifter's analyser has no CPU
clock, and applying it on only one side would make analysis and runtime
disagree. Games that rely on it are vanishingly rare.

## UxROM — 2

16 KB switchable at `$8000`, last bank fixed at `$C000`, CHR RAM.

**Modelled:** bank register, bank wrapping to ROM size, CHR RAM.

## CNROM — 3

Fixed PRG, 8 KB CHR banking.

**Modelled:** CHR bank register with power-of-two masking.

**Bus conflicts:** deliberately *not* applied, matching FCEUmm's mapper 3.
Games write the bank number to an address whose ROM byte usually differs, and
ANDing it would break them. (GxROM/Camerica/NINA do apply conflicts.)

## MMC3 — 4, 206

The most important mapper in the library.

**Modelled:** bank-select register, eight bank registers, PRG mode bit (6),
CHR mode bit (7), four mirroring modes, PRG RAM protect register, scanline
counter with latch/reload/enable and IRQ assertion.

The scanline counter is clocked once per visible scanline by the PPU
(`MapperStub.ClockScanline()` from `Ppu.Clock()`), which is the correct
approximation of the A12 rising edge for every licensed game.

**Not modelled:** the exact A12 filter (three-falling-edge qualification). Games
that change CHR banks mid-scanline in ways that depend on the filter will show
graphics glitches.

**Mapper 206** uses the same banking without an IRQ. `HasIrq` is a catalog flag,
so the emitted `ClockScanline` is a no-op for 206.

## AxROM — 7

32 KB PRG window; bit 4 of the bank byte selects single-screen low or high
mirroring.

**Modelled:** bank, mirroring bit, CHR RAM.

## MMC2 — 9

`$8000-$9FFF` switchable, `$A000-$FFFF` fixed to the last three 8 KB banks.
CHR is chosen by four latches, normally triggered when the PPU fetches tiles
`$FD`/`$FE` from the pattern tables.

**Modelled:** PRG register, all four CHR latches, mirroring register, and the
latch trigger from the renderer's background fetch path
(`MapperStub.LatchChr`).

**Not modelled:** latching from *sprite* fetches. Punch-Out!! uses background
tiles for the effect, so the visible result is correct.

## Color Dreams (11), GxROM (66)

One register: PRG bank in bits 4-5, CHR bank in bits 0-1.

**Modelled:** both banks, bus conflicts.

## CPROM — 13

Fixed 32 KB PRG, 16 KB of CHR RAM, upper 4 KB window banked.

**Modelled:** the CHR latch, bus conflicts.

## VRC2 / VRC4 / VRC6 — 21-26

Konami's banking family. All three share eight 1 KB CHR registers, PRG
registers, a mirroring register and (except VRC2) a scanline IRQ. They differ in
which CPU address selects which register, which is expressed as a wiring enum:

| Mapper | Wiring | High bit from | IRQ |
|---|---|---|---|
| 21 | VRC4a/c | A1 | yes |
| 22 | VRC2a | — (2 KB CHR granularity) | no |
| 23 | VRC4e/f | A1 | yes |
| 24 | VRC6a | — (dedicated registers) | yes |
| 25 | VRC4b/d | A0 | yes |
| 26 | VRC6b | — (dedicated registers) | yes |

**Modelled:** PRG banking including the swap bit, 1 KB CHR banking (2 KB pairs
for VRC2a), mirroring, IRQ latch/counter/control.

**Not modelled:** VRC6 expansion audio (three extra channels). The APU plays the
2A03 channels only, so VRC6 soundtracks are missing their lead voices.

## BNROM / NINA-001 — 34

Single 32 KB PRG register. NINA-001 additionally banks CHR in 4 KB units through
a register in the `$7FFD-$7FFF` hole.

**Modelled:** PRG register, the low-range CHR register.

## Sunsoft FME-7 — 69

Command/parameter register pair at `$8000`/`$A000`.

**Modelled:** eight 1 KB CHR banks, three 8 KB PRG banks, WRAM enable,
four mirroring modes, 16-bit scanline counter in both CPU-cycle and
scanline mode.

**Not modelled:** Sunsoft 5B expansion audio.

## Camerica — 71

UxROM-style 16 KB banking with a mirroring bit and bus conflicts.

**Modelled:** bank (bits 0-3), mirroring (bit 4), bus conflicts.

## NINA-03/06 (79), Jaleco CHR (87), Namco 118 (88)

Fixed PRG with 8 KB CHR banking.

- **79** and **88** take a mirroring bit from bit 3 of the bank register.
- **87** has no mirroring bit and shifts the bank number by one.

**Modelled:** CHR bank, mirroring where present, bus conflicts.

## Codemasters Quattro — 232

Four 128 KB blocks. Writes to `$8000-$BFFF` select the block from bits 3-4;
writes to `$C000-$FFFF` select the outer 64 KB bank from bits 2-4.

**Modelled:** both registers.

---

## Adding a mapper

1. Add a `MapperDescriptor` to `MapperCatalog.Build()`, choosing the closest
   `MapperFamily` (add a new family only if the banking genuinely differs).
2. Implement the board in `Core/Boards/`.
3. Register it in `BoardFactory.Builders`.
4. Declare its switchable window size in
   `Disassembler.SwitchableWindowBytes()` — 8 KB, 16 KB or 32 KB, or 0 if PRG
   never changes. Getting this wrong makes the bank walk corrupt the layout.
5. Implement emission in `MapperRuntimeEmitter` — either reuse an existing
   family's write body or add a new one plus its `Emit*` helpers.
6. Add a `SyntheticRom.Spec` entry to `SyntheticRom.DefaultMatrix`.
7. Add board tests to `tests/NesLifter.Tests/BoardTests.cs`.
8. Run `dotnet test` and `dotnet run -- --selftest`.

The self-test is the real gate: if the synthetic cartridge lifts, compiles and
executes, the mapper is wired correctly end to end.

## Unsupported mappers

Anything not in the table above falls back to a flat PRG layout. The analysis
report says so explicitly:

```
Mapper support : fallback (flat PRG layout)
```

Most likely next candidates: **MMC5 (5)**, **VRC7 (85)**, **Namco 163 (19)**,
**Sunsoft-4 (68)**, **Taito TC0190 (33)**, **IREM H3001 (65)**.
