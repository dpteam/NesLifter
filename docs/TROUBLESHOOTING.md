# Troubleshooting

Start with `analysis.txt` in the output folder. It tells you which mapper was
detected, whether that mapper is implemented, and how much of PRG was reached.

---

## The lift itself fails

### `Missing iNES signature (NES\x1A)`

The file is not an iNES image. `.unf`, `.unif` and raw `.bin` dumps are not
supported; convert them first.

### `Header reports zero PRG ROM size`

A corrupt header. Some dumps store the PRG size in the wrong nibble. Try another
dump of the same cartridge.

### `Mapper N is not implemented`

Expected. The report will say `Mapper support : fallback (flat PRG layout)` and
the game may still run if the mapper only banks CHR. See
[`MAPPERS.md`](MAPPERS.md) for the supported list.

### `Roslyn compilation failed`

Run with `--verbose`; the diagnostics include the file, line and column inside
`Game.generated.cs`. Keep the generated source (`--keep-source`, the default) and
open it — a compile error means the lifter emitted invalid code, which is a bug
worth reporting.

### `Previous build is in use; writing a hot-reload build`

The previous game is still running and Windows has the DLL locked. The new build
is written as `<name>.rebuild-<timestamp>.dll`. Close the game and re-run if you
want the canonical filename.

---

## The game starts but the screen is black

### Check `analysis.txt` first

```
Instructions  : 0
```

means the analyser found no code at all. Almost always this is a vector problem:
the RESET vector points outside PRG, or the mapper maps the vector bank
elsewhere. Try forcing the entry point:

```
# dynamic_targets.txt
8000
```

### Check `runtime.log`

The generated game writes `runtime.log` next to the DLL:

```
2026-11-15 12:04:31.118 [session] === game started; mapper=4; log=... ===
2026-11-15 12:04:31.120 [reset] CPU reset; entry will be $8000
2026-11-15 12:04:31.121 [cpu-start] CPU thread started
2026-11-15 12:04:31.140 [nmi] vblank/frame=1 cycles=27394 pc=$8123
```

If `[nmi]` never appears, the CPU never reached the VBlank wait — look at the
last `PC=` value.

### Run headless for a clean signal

```powershell
dotnet game.dll --headless 600
```

Exit codes: `0` reached the frame budget, `2` ran but fell short (usually a
trap), `3` trapped. The trap message names the address.

Headless is **frame-locked**: the CPU thread finishes a frame, parks, and waits
for the driver to capture it before continuing. That makes `frame N` mean
exactly frame N, which is what makes the options below reproducible.

### Capture screenshots and traces headlessly

```powershell
# PNGs at specific frames, plus an audio trace of the same run
dotnet game.dll --headless 600 --shots 120,240,600 --shot-dir .\probe --apu-trace .\probe\audio.raw

# Per-subsystem traces
dotnet game.dll --headless 600 --diag cpu,ppu,apu --diag-dir .\probe
```

| Flag | What it writes |
| --- | --- |
| `--shots <frames>` | `frame_NNNNNN.png`, one per requested frame |
| `--shot-dir <dir>` | where the PNGs and traces go (created if absent) |
| `--apu-trace <file>` | raw signed 16-bit LE mono at 44.1 kHz |
| `--diag <list>` | any of `cpu,ppu,apu,mapper,mem,frame`, or `all` |
| `--diag-dir <dir>` | where the `.log` traces go |
| `--press <script>` | scripted controller input, see below |

### Tell "stuck on the title screen" from "broken"

A game that never receives input renders one static frame forever, which looks
exactly like a game that failed to start. Script a button press to distinguish
them:

```powershell
# Hold Start from frame 200 to 215, then Right from 500 to 700.
Game.exe --headless 1400 --shots 400,800 --press 200:08,215:00,500:80,700:00
```

The mask is the shift-register bit order the pad produces:

| Bit | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Button | A | B | Select | Start | Up | Down | Left | Right |

So `08` is Start, `80` is Right, `88` is Start+Right. The last entry at or
before the current frame stays held until the next entry replaces it.

If the screen changes after Start, the engine is fine and the earlier run was
simply waiting. If it does not, look at `cpu.log` for the PC it is spinning on:
a loop that never reads `$4016` is not waiting for input at all.

`tools/Probe` drives all of this and summarises the result:

```powershell
dotnet run --project tools/Probe -- --exe .\Game.exe --frames 120,240,600 --audio
```

It reports distinct colours and "ink" percentage per frame (a blank or
single-colour frame is a rendering bug) and silence/clipping/RMS for the audio
trace (100% silence means every channel is off; clipping means the mixer summed
without headroom).

### Read the per-subsystem traces

Each trace is one event per line with fixed columns, so two runs can be diffed
directly.

- `frame.log` — one line per frame: `ctrl`, `mask`, `status`, scroll, base
  nametable, split scanline, sprite 0, palette entry 0. **Start here** when the
  picture is wrong: if `ctrl`/`mask` look sane, the bug is in the renderer, not
  in the game's PPU setup.
- `ppu.log` — every `$2000-$2007` write with the live `v`/`t` addresses and the
  current scanline.
- `cpu.log` — one line per instruction: frame, cycle, PC, opcode, A/X/Y/SP/P,
  and a decoded mnemonic. Large: expect hundreds of MB for a few hundred frames.
- `apu.log` — every `$4000-$4017` write with the decoded channel state
  (enable, period, length counters).
- `mapper.log` — every write above `$4020` with the resulting PRG window
  offsets, which is how you catch a bank switch that landed on the wrong bank.
- `mem.log` — accesses outside the regions a well-behaved game uses.

---

## Graphics are wrong

### Wrong colours / wrong palette

Check `Mirroring` in `analysis.txt`. Single-screen mirroring (AxROM, MMC1
one-screen modes) is the usual culprit; a mapper that reports the wrong
mirroring mode will fetch nametables from the wrong table.

### Status bar or HUD jumps

Mid-frame scroll changes are captured per scanline. If a game changes scroll by
writing `$2006` rather than `$2005`, the split point is estimated from the
sprite-0 position. Report it with the game name and the expected row.

### Sprites flicker or disappear

The renderer applies the hardware eight-sprites-per-scanline limit and the
`$2001` left-edge clipping bits. A game that overflows the budget on purpose
(multiplexing sprites across frames) needs the CPU and renderer to stay in sync;
if `frames=` in `runtime.log` advances unevenly, the CPU is being throttled.

### CHR tiles are garbage on MMC2 games

CHR latching happens on background pattern fetches. If a game latches from sprite
fetches instead, tiles will be wrong — this is a known gap (see
[`MAPPERS.md`](MAPPERS.md#mmc2--9)).

### Banked game shows the wrong code

The analyser decodes through the **power-on** bank layout. Code in a bank that
is only switched in later runs through the interpreter. It will execute, but
slowly, and any code the interpreter cannot classify shows up in
`dynamic_targets.log`. Feed that file back in and re-lift.

---

## The game traps

A trap writes `trap.log` with the last 256 instructions (PC, A, X, Y, SP, P) and
prints the reason in the window and in `runtime.log`.

| Message | Meaning |
|---|---|
| `Unknown opcode $XX at $YYYY` | The byte has no entry in the opcode table. Almost always the analyser decoded data as code. Force the real entry point. |
| `Missing branch target $YYYY` | A branch leaves the decoded region. Add the target to `dynamic_targets.txt`. |
| `Dynamic dispatch -> $YYYY` | An indirect jump went somewhere not statically known and the interpreter could not classify it. |
| `No valid entry/reset vector` | RESET points outside PRG. See above. |
| `KIL/HLT opcode executed` | The CPU reached a deliberate halt. Usually data, not code. |

The fix loop is always the same:

1. Read the address from `trap.log`.
2. Add it to `dynamic_targets.txt`.
3. Re-run the lifter.

---

## Audio

### No sound at all

`winmm` is Windows-only. On other platforms the game runs but is silent. Check
`runtime.log` for `Audio.TryStop()` — headless mode disables audio deliberately.

### Crackling or stuttering

The mixer runs on its own thread with four 512-sample buffers. If the CPU thread
cannot keep up (a large game running through the interpreter), buffers starve.
Confirm with `instructions=` in the headless summary: if the count is far below
what the frame budget implies, the CPU is the bottleneck.

### Missing instruments

APU sweep and DMC are not implemented, and VRC6 expansion audio is absent. Music
plays, but those voices are silent. See the roadmap.

---

## Performance

### The generated C# is enormous

Expected for large cartridges: every reachable instruction becomes a labelled
block, and PRG/CHR are embedded as byte arrays. A 512 KB PRG game produces tens
of MB of source and a few MB of assembly.

Mitigations today: `--no-source` to skip writing the file. Structural fixes
(splitting into multiple files, direct-threaded dispatch) are roadmap items.

### The lift takes a long time

The dominant cost is Roslyn compiling a very large single file. `--no-compile`
generates C# only and returns in seconds; compile it yourself if you want to
iterate on the source.

---

## Reporting a bug

Include:

1. `analysis.txt`
2. `runtime.log` (or `trap.log` if it trapped)
3. the output of `dotnet game.dll --headless 600`
4. `dotnet --version` and the NesLifter version (`--version`)
5. the cartridge's mapper number and CRC32 from `analysis.txt`

Do **not** attach the ROM.

---

## The GUI

### The window closes immediately, or the terminal UI appears instead

The WebView2 runtime is missing. It ships with Windows 11 and with current Edge
on Windows 10, but not with every stripped-down or LTSC install.

Install the **Microsoft Edge WebView2 Runtime** (Evergreen Bootstrapper) from
Microsoft. NesLifter falls back to the terminal UI automatically, so nothing is
broken meanwhile — you just get the TUI.

### `RPC_E_CHANGED_MODE`

This was a NesLifter bug, fixed in 0.9.0: `Main` was not `[STAThread]`, so COM
initialised as MTA and every WebView2 call failed. If you see it on a current
build, something is starting the process from an MTA thread — launch the exe
directly rather than from another managed host.

### The window is blank or black over remote desktop

Also fixed in 0.9.0: Chromium is now forced onto software rendering
(`--disable-gpu --disable-gpu-compositing`). On very old RDP versions you may
still see a blank first paint; resizing the window forces a redraw.

### The cartridge list is empty after picking a folder

- Check the path in the box: it must be the folder itself, not a parent.
- **Recurse into sub-folders** is on by default; if the ROMs are nested deeper
  than expected, that is what finds them.
- A folder with no `.nes` files shows "No cartridges yet".

### A row is red

The mapper is not implemented. `analysis.txt` will say
`Mapper support : fallback (flat PRG layout)`. The game may still run if the
mapper only banks CHR. See [`MAPPERS.md`](MAPPERS.md).

### Icons need two lifts

The game captures its own icon **while running**, so the icon is embedded into
the `.exe` on the next lift. To get it right the first time, supply `--icon`
with your own image.

---

## The TUI

### Arrow keys print escape characters

The console is redirected or reports `TERM=dumb`. NesLifter detects this and
uses line mode, but a wrapper (some CI runners, some SSH clients) can defeat the
detection. Set `NesLifter_NO_TUI=1` to force line mode.

### Line mode ignores my flags

Line mode is only reached when no arguments were given. With arguments the CLI
path runs directly, which is what scripts want.
