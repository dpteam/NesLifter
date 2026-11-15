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

## Stage 1b — User interface (in progress)

The tool is usable from the command line, but a recompiler is a visual job:
you pick a ROM folder, watch a queue, read a report, launch a result.

- [x] CLI with explicit arguments (kept forever as the scripting/CI path)
- [x] **Interactive TUI**: arrow-key menu, ROM picker with mapper/board/size
      columns, live lift progress, log pane, per-title result summary. Degrades
      to plain line mode when redirected or `TERM=dumb`.
- [x] **WebView2 GUI** (`--gui`, or automatic when launched bare): WinForms host
      + embedded Chromium, styled with **NES.css**. Local `web/vendor` copies of
      NES.css and Font Awesome, so it works offline.
- [x] **Themes**: Light / Dark / OLED, persisted. OLED is true black.
- [x] **Icon set**: Font Awesome (vendored) for toolbar and status glyphs;
      NES.css pixel components for flavour.
- [x] **"Soft mathematical audio"**: gentle, low-volume generated UI feedback
      (sine/triangle envelopes, no samples) for select / start / success /
      failure, off by default.
- [x] Folder and icon pickers, live log streaming, progress bar.
- [ ] Drag-and-drop ROM folders onto the window.
- [ ] Queue that survives restarts.
- [ ] Embedded game preview: run the generated DLL headlessly inside the GUI and
      show the captured frame as a thumbnail in the result row (shares the icon
      capture machinery from Stage 5b).
- [ ] i18n: the GUI is English-only today.

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

## Stage 2b — In-game overlay (settings / log / debug)

A lifted game currently has no interface of its own: resolution, scaling,
audio, input mapping, save slots and diagnostics all live in generated code
with no way to reach them. A NES-style overlay solves all of it at once and
becomes the natural home for every future option.

- [ ] **Toggle with F2** (and `--overlay-key`), drawn over the framebuffer in
      the same 8-bit visual language as the GUI (NES.css-styled panel rendered
      by the game, not by a browser).
- [ ] **Pause on open.** Freeze the CPU thread and the audio ring so settings can
      be changed without the game running underneath.
- [ ] **Tabs**: Settings · Video · Audio · Input · Debug · Log.
- [ ] **Settings**: integer scale (1x-6x), aspect ratio, vsync, frame limiter,
      region override (NTSC/PAL), sprite limit on/off.
- [ ] **Audio**: master volume, per-channel mute (Pulse ×2, triangle, noise,
      DMC), buffer size, "soft mathematical" UI feedback toggle.
- [ ] **Input**: rebind every button, per-player, with a capture prompt; gamepad
      mapping once controller support lands.
- [ ] **Debug**: live CPU state (A/X/Y/SP/P/PC), mapper registers, PPU scroll
      values, frame timing graph, trap log, "force interpreter" toggle, and a
      RAM search / cheat box.
- [ ] **Log**: tail of `runtime.log` plus `dynamic_targets.log`, filterable,
      with a "copy report" button that bundles `analysis.txt` for bug reports.
- [ ] **Save/restore state** from the overlay once save states exist (Stage 5).
- [ ] Persist overlay settings next to the game as `Game.settings.json` so a
      rebuild keeps them.
- [ ] Headless mode must ignore the overlay entirely.

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

- [~] **Bank-aware analysis.** Instructions are keyed by **PRG offset**, and
      the analyser walks every PRG bank as if it were mapped at `$8000`,
      merging results by offset. The runtime dispatch resolves a CPU address to
      an offset through the mapper before jumping, so one lift covers all banks.
      Done: offset-keyed model, per-bank descent, offset-keyed dispatch.
      Remaining: per-bank coverage accounting and pruning of banks that are
      proven to be data only.
- [ ] Bank-switch tracking: record the mapper writes that lead to each bank so
      cross-bank `JMP`/`JSR` edges can be resolved *statically* instead of
      relying on the runtime resolve-then-dispatch step
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
- [ ] **Apphost `.exe` by default.** A lifted game should be `Game.exe` +
      `Game.dll` + `Game.runtimeconfig.json`, the same shape every .NET 5+
      desktop app ships in, so the user double-clicks instead of typing
      `dotnet Game.dll`. Roslyn already emits the `.dll`; the missing piece is
      generating the apphost (or invoking the SDK's `apphost` template) plus a
      `deps.json`, and copying the required framework references.
- [ ] **"Full EXE" publish mode.** An opt-in single-file, self-contained build
      (`--publish full`) that bundles the runtime so the game runs on a machine
      with no .NET installed at all. Costs ~60-70 MB per game, so it must stay
      a choice, not the default. Needs `PublishSingleFile`,
      `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`, and a
      fallback path when the SDK is not present next to the lifter.
- [ ] Save states (serialise RAM, PPU, APU, mapper and CPU state)
- [ ] Battery-backed SRAM persistence (`.sav` next to the assembly)
- [ ] Rewind buffer
- [ ] Optional `net10.0` (no WinForms) build target: SDL-style or console-only
      output for Linux/macOS

---

## Stage 5b — Game icon (killer feature)

A lifted game deserves its own face. Right now every output is a generic
assembly, which makes a folder of recompiled games indistinguishable.

- [ ] **Screenshot-derived icon.** After the generated game has been running for
      a configurable delay (default **5 seconds**, enough for the title screen to
      settle), capture the framebuffer, downscale it, and write it as the
      executable's icon. Implemented inside the generated game (it owns the
      framebuffer and the window), triggered by
      `--capture-icon <seconds|off>` and persisted next to the game as
      `Game.icon.ico` so a rebuild reuses it instead of re-capturing.
- [ ] **Custom icon input.** `--icon <path>` accepts any image (PNG/JPG/BMP/GIF/
      WebP) and converts it to a multi-resolution `.ico` (16/20/24/32/40/48/64/
      128/256), letterboxing non-square images instead of stretching them. The
      converter already exists for the project's own branding in
      `tools/Branding`; promote it to a shared `Imaging/IcoWriter` used by both.
- [ ] **Icon candidate picking.** Capture N frames around the delay and pick the
      sharpest / most colourful one, so a black fade-in or a blinking cursor
      does not become the icon.
- [ ] Wire the icon into the apphost at build time (Windows reads the icon from
      the PE resource, so the `.ico` must be embedded during the apphost step,
      not swapped afterwards).
- [ ] `--no-icon` to opt out, and `--icon-frame <n>` to capture a specific frame
      number instead of a time delay.

---

## Stage 5c — Modding (killer feature, hard)

A lifted game is C#, and C# is editable. That is the one advantage a
recompilation has over an emulator, and it is currently wasted: the generated
source is a flat pile of `goto` blocks with no seam to hook into.

Three levels of modding, easiest first:

- [ ] **Data mods (no code).** Every cartridge asset becomes a named resource:
      CHR tiles, palettes, nametables, level data, text banks. `--export-assets`
      writes them as PNG/JSON next to the game; `--import-assets` rebuilds the
      game with replacements. This alone covers sprite swaps, palette hacks and
      text edits without touching a single line of 6502.
- [ ] **ROM hack input.** Accept a pre-patched `.nes` (IPS/BPS applied by the
      user) and lift it like any other cartridge. Mostly already works; needs a
      documented workflow plus a diff report showing what the hack changed.
- [ ] **Named symbols.** Emit `.nl` sidecar files mapping every `L_00A4C2` to a
      meaningful name (`Player_Update`, `PPU_WritePalette`) using pattern
      recognition over the lifted blocks. Without this, hand-editing generated
      code is hopeless.
- [ ] **Mod hook points.** Emit explicit extension seams in the generated game:
      `ModHooks.OnFrame`, `OnSprite`, `OnInput`, `OnAudio`, `OnMapperWrite`, and
      a `mods/` folder scanned at startup. A mod is a small C# assembly compiled
      against the game's public surface, loaded without touching generated code.
- [ ] **Split generated code into files.** One file per bank (or per function
      once symbols exist) instead of one 2 MB monolith, so a human can actually
      open, read and patch a specific routine.
- [ ] **Mod API stability.** Version the generated surface so a mod keeps
      working across re-lifts of the same cartridge.
- [ ] **Live patching.** Change a lifted routine and hot-reload it while the
      game runs (Roslyn already sits in the toolchain; the runtime would need an
      assembly reload path).

## Stage 5d — Multiplayer (hard)

The NES had two controller ports, and plenty of games are two-player. Today the
generated game exposes `Joy1` only.

- [ ] **Second controller.** Full `Joy2` implementation (the field exists but is
      never written), plus a second keyboard map.
- [ ] **Gamepad support.** XInput and DualShock/DualSense through a small
      abstraction, with per-controller binding and hot-plug.
- [ ] **Local co-op sanity.** Verify four-screen mirroring, sprite budget and
      input polling timing with two active players; many games behave
      differently when port 2 is live.
- [ ] **Netplay.** Deterministic lockstep over a socket: exchange inputs per
      frame, roll back on divergence, keep the CPU thread authoritative. The
      lifted game is already a deterministic function of inputs + RAM, which is
      exactly what lockstep needs.
- [ ] **Netplay transport.** Direct IP first, then a small relay for NAT
      traversal; spectator mode falls out for free.
- [ ] **Save-state based rollback** for netplay (depends on Stage 5 save states).
- [ ] **Four-player adapters** (NES Four Score / Satellite) for the handful of
      games that support them.

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
