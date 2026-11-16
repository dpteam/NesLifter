using System;
using System.Text;
using NesLifter.Core;
using NesLifter.Mappers;

namespace NesLifter.Lifting;

/// <summary>
/// Emits the <c>MapperStub</c> runtime class for one cartridge.
///
/// Only the code for the cartridge's own mapper family is generated, so a
/// generated game carries a lean, specialised mapper instead of a fat
/// "if (mapper == N)" ladder. The page-table core (2 KB PRG pages, 1 KB CHR
/// pages, setters) is shared by every family and mirrors
/// <see cref="NesLifter.Core.CartMapping"/> so that the analysis layout and
/// the runtime layout cannot drift apart.
///
/// The emitted source is written with raw string literals: what you read here
/// is what ends up in <c>Game.generated.cs</c>, braces and quotes included.
/// </summary>
public sealed class MapperRuntimeEmitter
{
    private readonly CartInfo _cart;
    private readonly MapperDescriptor _descriptor;
    private readonly StringBuilder _sb = new();

    public MapperRuntimeEmitter(CartInfo cart)
    {
        _cart = cart ?? throw new ArgumentNullException(nameof(cart));
        _descriptor = MapperCatalog.Get(cart.Mapper);
    }

    /// <summary>True when the mapper can raise a scanline IRQ.</summary>
    public bool HasIrq => _descriptor.HasIrq;

    /// <summary>True for MMC2, which latches CHR from pattern-table fetches.</summary>
    public bool HasChrLatch => _descriptor.Family == MapperFamily.Mmc2;

    /// <summary>Mapper family actually emitted.</summary>
    public MapperFamily Family => _descriptor.Family;

    public string Emit()
    {
        _sb.Clear();

        EmitHeader();
        EmitPageTables();
        EmitBankingHelpers();
        EmitResetAndPower();
        EmitMemoryInterface();
        EmitFamilyState();

        _sb.AppendLine("}");
        _sb.AppendLine();

        return _sb.ToString();
    }

    // =================================================================
    // Header
    // =================================================================

    private void EmitHeader()
    {
        Raw($$"""
            static class MapperStub
            {
                public const int MapperId = {{_descriptor.Id}};
                public const string MapperName = "{{Escape(_descriptor.Name)}}";
                public const bool HasIrq = {{Bool(HasIrq)}};
                public const bool HasChrLatch = {{Bool(HasChrLatch)}};

            """);
    }

    // =================================================================
    // Page tables
    // =================================================================

    private void EmitPageTables()
    {
        Raw("""
                // 2 KB PRG pages cover $0000-$FFFF; only $6000-$FFFF is ever mapped.
                public static byte[][] PrgPageMem = new byte[32][];
                public static int[] PrgPageBase = new int[32];
                public static bool[] PrgPageIsRam = new bool[32];

                // 1 KB CHR pages cover $0000-$1FFF.
                public static byte[][] ChrPageMem = new byte[8][];
                public static int[] ChrPageBase = new int[8];
                public static bool[] ChrPageIsRam = new bool[8];

                static byte[] _prg = Runtime.PrgRom;
                static byte[] _chr = Runtime.ChrRom.Length > 0 ? Runtime.ChrRom : Runtime.ChrRam;
                static int _prgBanks8 = Math.Max(1, Runtime.PrgRom.Length / 0x2000);
                static int _prgBanks16 = Math.Max(1, Runtime.PrgRom.Length / 0x4000);
                static int _prgBanks32 = Math.Max(1, Runtime.PrgRom.Length / 0x8000);
                static int _chrBanks1 = Math.Max(1, _chr.Length / 0x400);
                static int _chrBanks2 = Math.Max(1, _chr.Length / 0x800);
                static int _chrBanks4 = Math.Max(1, _chr.Length / 0x1000);
                static int _chrBanks8 = Math.Max(1, _chr.Length / 0x2000);

            """);
    }

    // =================================================================
    // Banking helpers
    // =================================================================

    private void EmitBankingHelpers()
    {
        Raw("""
                // === PRG banking ===
                public static void SetPrg8(ushort addr, int bank)
                {
                    bank = Wrap(bank, _prgBanks8);
                    int page = (addr >> 11) & 31;
                    int baseOff = bank * 0x2000 - addr;
                    for (int i = 0; i < 4; i++)
                    {
                        int p = page + i;
                        if (p >= 32) break;
                        PrgPageMem[p] = _prg; PrgPageBase[p] = baseOff; PrgPageIsRam[p] = false;
                    }
                }

                public static void SetPrg16(ushort addr, int bank)
                {
                    bank = Wrap(bank, _prgBanks16);
                    int page = (addr >> 11) & 31;
                    int baseOff = bank * 0x4000 - addr;
                    for (int i = 0; i < 8; i++)
                    {
                        int p = page + i;
                        if (p >= 32) break;
                        PrgPageMem[p] = _prg; PrgPageBase[p] = baseOff; PrgPageIsRam[p] = false;
                    }
                }

                public static void SetPrg32(ushort addr, int bank)
                {
                    bank = Wrap(bank, _prgBanks32);
                    int page = (addr >> 11) & 31;
                    int baseOff = bank * 0x8000 - addr;
                    for (int i = 0; i < 16; i++)
                    {
                        int p = page + i;
                        if (p >= 32) break;
                        PrgPageMem[p] = _prg; PrgPageBase[p] = baseOff; PrgPageIsRam[p] = false;
                    }
                }

                /// <summary>Maps WRAM into $6000-$7FFF. bank is in 8 KB units.</summary>
                public static void SetWram8(int bank)
                {
                    if (Runtime.Wram.Length == 0) return;
                    bank = Wrap(bank, Math.Max(1, Runtime.Wram.Length / 0x2000));
                    int baseOff = bank * 0x2000 - 0x6000;
                    for (int i = 0; i < 4; i++)
                    {
                        int p = 12 + i;
                        PrgPageMem[p] = Runtime.Wram; PrgPageBase[p] = baseOff; PrgPageIsRam[p] = true;
                    }
                }

                // === CHR banking ===
                public static void SetChr1(ushort addr, int bank)
                {
                    bank = Wrap(bank, _chrBanks1);
                    int page = (addr >> 10) & 7;
                    ChrPageMem[page] = _chr; ChrPageBase[page] = bank * 0x400 - addr; ChrPageIsRam[page] = Runtime.ChrRom.Length == 0;
                }

                public static void SetChr2(ushort addr, int bank)
                {
                    bank = Wrap(bank, _chrBanks2);
                    int page = (addr >> 10) & 7;
                    int baseOff = bank * 0x800 - addr;
                    for (int i = 0; i < 2; i++)
                    {
                        int p = page + i;
                        if (p >= 8) break;
                        ChrPageMem[p] = _chr; ChrPageBase[p] = baseOff; ChrPageIsRam[p] = Runtime.ChrRom.Length == 0;
                    }
                }

                public static void SetChr4(ushort addr, int bank)
                {
                    bank = Wrap(bank, _chrBanks4);
                    int page = (addr >> 10) & 7;
                    int baseOff = bank * 0x1000 - addr;
                    for (int i = 0; i < 4; i++)
                    {
                        int p = page + i;
                        if (p >= 8) break;
                        ChrPageMem[p] = _chr; ChrPageBase[p] = baseOff; ChrPageIsRam[p] = Runtime.ChrRom.Length == 0;
                    }
                }

                public static void SetChr8(int bank)
                {
                    bank = Wrap(bank, _chrBanks8);
                    for (int i = 0; i < 8; i++)
                    {
                        ChrPageMem[i] = _chr; ChrPageBase[i] = bank * 0x2000; ChrPageIsRam[i] = Runtime.ChrRom.Length == 0;
                    }
                }

                static int Wrap(int bank, int banks)
                {
                    if (banks <= 1) return 0;
                    if ((banks & (banks - 1)) == 0) return bank & (banks - 1);
                    bank %= banks;
                    return bank < 0 ? bank + banks : bank;
                }

                public static byte ReadChrByte(int addr)
                {
                    int page = (addr >> 10) & 7;
                    byte[] mem = ChrPageMem[page];
                    if (mem == null) return 0;
                    int off = ChrPageBase[page] + addr;
                    if ((uint)off >= (uint)mem.Length) return 0;
                    return mem[off];
                }

                public static void WriteChrByte(int addr, byte value)
                {
                    int page = (addr >> 10) & 7;
                    if (!ChrPageIsRam[page]) return;
                    byte[] mem = ChrPageMem[page];
                    if (mem == null) return;
                    int off = ChrPageBase[page] + addr;
                    if ((uint)off >= (uint)mem.Length) return;
                    mem[off] = value;
                }

                public static byte ReadPrg(ushort addr)
                {
                    int page = (addr >> 11) & 31;
                    byte[] mem = PrgPageMem[page];
                    if (mem == null) return 0;
                    int off = PrgPageBase[page] + addr;
                    if ((uint)off >= (uint)mem.Length) return 0;
                    return mem[off];
                }

                static void WritePrgPage(ushort addr, byte value)
                {
                    int page = (addr >> 11) & 31;
                    if (!PrgPageIsRam[page]) return;
                    byte[] mem = PrgPageMem[page];
                    if (mem == null) return;
                    int off = PrgPageBase[page] + addr;
                    if ((uint)off >= (uint)mem.Length) return;
                    mem[off] = value;
                }

            """);
    }

    // =================================================================
    // Reset / power
    // =================================================================

    private void EmitResetAndPower()
    {
        Raw("""
                public static void Reset()
                {
                    for (int i = 0; i < 32; i++) { PrgPageMem[i] = null; PrgPageBase[i] = 0; PrgPageIsRam[i] = false; }
                    for (int i = 0; i < 8; i++) { ChrPageMem[i] = null; ChrPageBase[i] = 0; ChrPageIsRam[i] = false; }
                    Array.Clear(Runtime.Wram, 0, Runtime.Wram.Length);
                    Power();
                }

                public static void Power()
                {
                    SetWram8(0);
            """);

        EmitPowerBody();

        Raw("""
                }

            """);
    }

    private void EmitPowerBody()
    {
        switch (_descriptor.Family)
        {
            case MapperFamily.Nrom:
            case MapperFamily.Cprom:
                Raw("""
                            if (_prgBanks16 <= 1) { SetPrg16(0x8000, 0); SetPrg16(0xC000, 0); }
                            else SetPrg32(0x8000, 0);
                    """);

                if (_descriptor.Family == MapperFamily.Cprom)
                    Raw("        _latch = 0; Sync();");
                else
                    Raw("        SetChr8(0);");

                break;

            case MapperFamily.UxRom:
                Raw("""
                            SetPrg16(0x8000, 0);
                            SetPrg16(0xC000, _prgBanks16 - 1);
                            SetChr8(0);
                    """);
                break;

            case MapperFamily.Cnrom:
                Raw("""
                            if (_prgBanks16 <= 1) { SetPrg16(0x8000, 0); SetPrg16(0xC000, 0); }
                            else SetPrg32(0x8000, 0);
                            SetChr8(0);
                    """);
                break;

            case MapperFamily.Mmc1:
                Raw("""
                            _shift = 0; _count = 0;
                            // FCEUX's MMC1CMReset: DRegs[0] = 0x1F, not 0x0C.
                            // 0x1F means horizontal mirroring, PRG mode 3 and 4 KB CHR
                            // mode; 0x0C would mean one-screen-low mirroring, which
                            // makes the first frames after reset come from the wrong
                            // nametable.
                            _ctrl = 0x1F; _chr0 = 0; _chr1 = 0; _prgReg = 0;
                            Sync();
                    """);
                break;

            case MapperFamily.Mmc3:
                Raw("""
                            _cmd = 0;
                            _r[0] = 0; _r[1] = 2; _r[2] = 4; _r[3] = 5;
                            _r[4] = 6; _r[5] = 7; _r[6] = 0; _r[7] = 1;
                            _irqLatch = 0; _irqCounter = 0; _irqReload = false; _irqEnabled = false; _irqPending = false;
                            Sync();
                    """);
                break;

            case MapperFamily.AxRom:
                Raw("""
                            _latch = 0;
                            SetPrg32(0x8000, 0);
                            SetChr8(0);
                            Runtime.Mirroring = 3;
                    """);
                break;

            case MapperFamily.GxRom:
            case MapperFamily.Bnrom:
                Raw("""
                            _latch = 0;
                            SetPrg32(0x8000, 0);
                            SetChr8(0);
                    """);
                break;

            case MapperFamily.Camerica:
                Raw("""
                            _latch = 0;
                            SetPrg16(0x8000, 0);
                            SetPrg16(0xC000, _prgBanks16 - 1);
                            SetChr8(0);
                    """);
                break;

            case MapperFamily.Nina:
                Raw("""
                            _latch = 0;
                            if (_prgBanks16 <= 1) { SetPrg16(0x8000, 0); SetPrg16(0xC000, 0); }
                            else SetPrg32(0x8000, 0);
                            SetChr8(0);
                    """);
                break;

            case MapperFamily.Quattro:
                Raw("""
                            _block = 0; _outer = 0;
                            Sync();
                    """);
                break;

            case MapperFamily.Mmc2:
                Raw("""
                            _prgReg = 0; for (int i = 0; i < 4; i++) _latchRegs[i] = 0;
                            Sync();
                    """);
                break;

            case MapperFamily.Fme7:
                Raw("""
                            _cmd = 0; for (int i = 0; i < 8; i++) _chrRegs[i] = 0;
                            _prgRegs[0] = 0; _prgRegs[1] = 1; _prgRegs[2] = 2;
                            _irqCounter = 0; _irqControl = 0; _irqPending = false;
                            Sync();
                    """);
                break;

            case MapperFamily.Vrc2:
            case MapperFamily.Vrc4:
            case MapperFamily.Vrc6:
                EmitVrcPower();
                break;

            default:
                Raw("""
                            SetPrg32(0x8000, 0);
                            SetChr8(0);
                    """);
                break;
        }
    }

    private void EmitVrcPower()
    {
        Raw("""
                    for (int i = 0; i < 8; i++) _chrRegs[i] = 0;
                    _prgRegs[0] = 0; _prgRegs[1] = 1; _prgRegs[2] = 2;
            """);

        if (VrcWiringCode() != 0)
            Raw("        _prgSwap = 0;");

        if (HasIrq)
            Raw("        _irqLatch = 0; _irqCounter = 0; _irqControl = 0;");

        Raw("        Sync();");
    }

    // =================================================================
    // Memory interface
    // =================================================================

    private void EmitMemoryInterface()
    {
        Raw("""
                /// <summary>
                /// Resolves a CPU address to the PRG ROM offset currently mapped there.
                /// Returns -1 when the address is not backed by PRG ROM (WRAM, I/O, open bus).
                /// This is what makes offset-keyed dispatch possible: the same byte in a
                /// switchable bank resolves to one offset no matter which address it appears at.
                /// </summary>
                public static int ToOffset(ushort addr)
                {
                    if (addr < 0x6000) return -1;
                    int page = (addr >> 11) & 31;
                    byte[] mem = PrgPageMem[page];
                    if (mem == null || !ReferenceEquals(mem, _prg)) return -1;
                    int off = PrgPageBase[page] + addr;
                    return (uint)off < (uint)_prg.Length ? off : -1;
                }

                /// <summary>CPU read for $4020-$FFFF.</summary>
                public static byte Read(ushort addr)
                {
                    if (addr < 0x8000)
                    {
                        if (addr >= 0x6000) return ReadPrg(addr);
                        return ReadLow(addr);
                    }
                    return ReadPrg(addr);
                }

                /// <summary>CPU write for $4020-$FFFF.</summary>
                public static void Write(ushort addr, byte value)
                {
                    if (addr < 0x8000)
                    {
                        if (addr >= 0x6000) { WriteWram(addr, value); return; }
                        WriteLow(addr, value);
                        return;
                    }
            """);

        EmitWriteBody();

        Raw("""
                }

                static byte ReadLow(ushort addr)
                {
            """);

        if (_descriptor.Family == MapperFamily.Mmc1)
        {
            // MMC1 returns the high address byte when PRG RAM is disabled,
            // which some games use to detect the WRAM state.
            Raw("""
                        if (Runtime.Wram.Length > 0 && (_prgReg & 0x10) != 0) return (byte)(addr >> 8);
                """);
        }

        Raw("""
                    return 0;
                }

                static void WriteLow(ushort addr, byte value)
                {
            """);

        if (_descriptor.Family == MapperFamily.Bnrom)
        {
            // NINA-001 maps its CHR register into the $7FFD-$7FFF hole.
            Raw("""
                        if (addr >= 0x7FFD) { SetChr4(0x0000, value); return; }
                """);
        }
        else
        {
            Raw("            // No low-range registers on this board.");
        }

        Raw("""
                }

                static void WriteWram(ushort addr, byte value)
                {
            """);

        if (_descriptor.Family == MapperFamily.Mmc1)
        {
            // MMC1 bit 4 of the PRG register disables WRAM writes.
            Raw("            if ((_prgReg & 0x10) != 0) return;");
        }

        Raw("""
                    WritePrgPage(addr, value);
                }

            """);
    }

    private void EmitWriteBody()
    {
        switch (_descriptor.Family)
        {
            case MapperFamily.Nrom:
                Raw("            // NROM has no bank registers.");
                break;

            case MapperFamily.Cprom:
                Raw("""
                            value &= ReadPrg(addr);   // bus conflict
                            _latch = value; Sync();
                    """);
                break;

            case MapperFamily.UxRom:
                Raw("            SetPrg16(0x8000, value);");
                break;

            case MapperFamily.Cnrom:
                Raw("            SetChr8(value);");
                break;

            case MapperFamily.Mmc1:
                EmitMmc1Write();
                break;

            case MapperFamily.Mmc3:
                EmitMmc3Write();
                break;

            case MapperFamily.AxRom:
                Raw("""
                            _latch = value;
                            SetPrg32(0x8000, value & 0x07);
                            Runtime.Mirroring = (value & 0x10) != 0 ? 4 : 3;
                    """);
                break;

            case MapperFamily.GxRom:
                Raw("""
                            value &= ReadPrg(addr);   // bus conflict
                            _latch = value;
                            SetPrg32(0x8000, (value >> 4) & 0x03);
                            SetChr8(value & 0x03);
                    """);
                break;

            case MapperFamily.Camerica:
                Raw("""
                            value &= ReadPrg(addr);   // bus conflict
                            _latch = value;
                            SetPrg16(0x8000, value & 0x0F);
                            Runtime.Mirroring = (value & 0x10) != 0 ? 4 : 3;
                    """);
                break;

            case MapperFamily.Nina:
                Raw("""
                            value &= ReadPrg(addr);   // bus conflict
                            _latch = value;
                            SetChr8(value);
                    """);

                if (_descriptor.Id != 87)
                    Raw("            Runtime.Mirroring = (value & 0x08) != 0 ? 1 : 0;");

                break;

            case MapperFamily.Bnrom:
                Raw("""
                            _latch = value;
                            SetPrg32(0x8000, value & 0x0F);
                    """);
                break;

            case MapperFamily.Quattro:
                Raw("""
                            if (addr >= 0xC000) _outer = (value >> 2) & 0x07;
                            else _block = (value >> 3) & 0x03;
                            Sync();
                    """);
                break;

            case MapperFamily.Mmc2:
                Raw("""
                            switch (addr & 0xF000)
                            {
                                case 0xA000: _prgReg = value; SetPrg8(0x8000, value); break;
                                case 0xB000: _latchRegs[0] = value; SetChr4(0x0000, value); break;
                                case 0xC000: _latchRegs[1] = value; SetChr4(0x0000, value); break;
                                case 0xD000: _latchRegs[2] = value; SetChr4(0x1000, value); break;
                                case 0xE000: _latchRegs[3] = value; SetChr4(0x1000, value); break;
                                case 0xF000: Runtime.Mirroring = (value & 1) != 0 ? 0 : 1; break;
                            }
                    """);
                break;

            case MapperFamily.Fme7:
                Raw("""
                            if (addr < 0xA000) { _cmd = value & 0x0F; return; }
                            switch (_cmd)
                            {
                                case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7:
                                    _chrRegs[_cmd] = value; SetChr1((ushort)(_cmd * 0x400), value);
                                    break;
                                case 8: _prgRegs[0] = value & 0x3F; Sync(); break;
                                case 9: _prgRegs[1] = value & 0x3F; Sync(); break;
                                case 0x0A: _prgRegs[2] = value & 0x3F; Sync(); break;
                                case 0x0B: break;   // WRAM enable
                                case 0x0C: Runtime.Mirroring = (value & 3) switch { 0 => 1, 1 => 0, 2 => 3, _ => 4 }; break;
                                case 0x0D:
                                    _irqControl = value;
                                    if ((value & 0x80) == 0) { _irqPending = false; Runtime.IrqPending = false; }
                                    break;
                                case 0x0E: _irqCounter = (_irqCounter & 0xFF00) | value; break;
                                case 0x0F: _irqCounter = (_irqCounter & 0x00FF) | (value << 8); _irqPending = false; Runtime.IrqPending = false; break;
                            }
                    """);
                break;

            case MapperFamily.Vrc2:
            case MapperFamily.Vrc4:
            case MapperFamily.Vrc6:
                EmitVrcWrite();
                break;

            default:
                Raw("            // No bank registers for this mapper.");
                break;
        }
    }

    private void EmitMmc1Write()
    {
        Raw("""
                    if ((value & 0x80) != 0)
                    {
                        _ctrl |= 0x0C; _shift = 0; _count = 0; Sync(); return;
                    }
                    _shift = (_shift >> 1) | ((value & 1) << 4);
                    _count++;
                    if (_count < 5) return;
                    int v = _shift & 0x1F; _shift = 0; _count = 0;
                    if (addr <= 0x9FFF) _ctrl = v;
                    else if (addr <= 0xBFFF) _chr0 = v;
                    else if (addr <= 0xDFFF) _chr1 = v;
                    else _prgReg = v;
                    Sync();
            """);
    }

    private void EmitMmc3Write()
    {
        Raw("""
                    bool odd = (addr & 1) != 0;
                    switch (addr & 0xE000)
                    {
                        case 0x8000:
                            if (!odd) _cmd = value; else _r[_cmd & 7] = value;
                            Sync();
                            break;
                        case 0xA000:
                            if (!odd) Runtime.Mirroring = (value & 1) == 0 ? 1 : 0;
                            break;
                        case 0xC000:
                            if (!odd) _irqLatch = value; else { _irqCounter = 0; _irqReload = true; }
                            break;
                        case 0xE000:
                            if (!odd) { _irqEnabled = false; _irqPending = false; Runtime.IrqPending = false; }
                            else _irqEnabled = true;
                            break;
                    }
            """);
    }

    private void EmitVrcWrite()
    {
        switch (VrcWiringCode())
        {
            case 0: // VRC2a
                Raw("""
                            switch (addr & 0xF000)
                            {
                                case 0x8000: _prgRegs[0] = value & 0x0F; Sync(); break;
                                case 0x9000: Runtime.Mirroring = (value & 1) != 0 ? 0 : 1; break;
                                case 0xA000: _prgRegs[1] = value & 0x0F; Sync(); break;
                                case 0xB000: SetChrPair(0, value >> 1); break;
                                case 0xC000: SetChrPair(2, value >> 1); break;
                                case 0xD000: SetChrPair(4, value >> 1); break;
                                case 0xE000: SetChrPair(6, value >> 1); break;
                            }
                    """);
                break;

            case 1: // VRC4 with A1 as the high bit
            case 2: // VRC4 with A0 as the high bit
                Raw($$"""
                            int index = (addr >> 11) & 0x03;
                            bool high = {{(VrcWiringCode() == 1 ? "(addr & 0x02) != 0;" : "(addr & 0x01) != 0;")}}
                            switch (addr & 0xF000)
                            {
                                case 0x8000:
                                    if (high) _prgRegs[1] = value & 0x1F; else _prgRegs[0] = value & 0x1F;
                                    Sync();
                                    break;
                                case 0x9000:
                                    if (!high) Runtime.Mirroring = (value & 1) != 0 ? 0 : 1;
                                    else { _prgSwap = value & 0x02; Sync(); }
                                    break;
                                case 0xA000: case 0xB000: case 0xC000: case 0xD000: case 0xE000:
                                    _chrRegs[index * 2 + (high ? 1 : 0)] = value;
                                    SyncChr();
                                    break;
                                case 0xF000:
                                    if (!high) _irqLatch = (_irqLatch & 0xF0) | (value & 0x0F);
                                    else _irqLatch = (_irqLatch & 0x0F) | ((value & 0x0F) << 4);
                                    break;
                            }
                    """);
                break;

            default: // VRC6
                Raw("""
                            switch (addr & 0xF000)
                            {
                                case 0x8000: _prgRegs[0] = value & 0x0F; Sync(); break;
                                case 0x9000: _prgRegs[1] = value & 0x0F; Sync(); break;
                                case 0xA000: _prgRegs[2] = value & 0x0F; Sync(); break;
                                case 0xB000:
                                    if ((addr & 3) == 3) { Runtime.Mirroring = (value & 1) != 0 ? 0 : 1; break; }
                                    _chrRegs[addr & 3] = value; SyncChr();
                                    break;
                                case 0xC000: _chrRegs[3 + (addr & 3)] = value; SyncChr(); break;
                                case 0xD000: _chrRegs[4 + (addr & 3)] = value; SyncChr(); break;
                                case 0xE000: _chrRegs[6 + (addr & 3)] = value; SyncChr(); break;
                                case 0xF000:
                                    switch (addr & 3)
                                    {
                                        case 0: _irqLatch = value; break;
                                        case 1: _irqCounter = _irqLatch; _irqPending = false; Runtime.IrqPending = false; break;
                                        case 2: _irqControl = value; _irqPending = false; Runtime.IrqPending = false; break;
                                    }
                                    break;
                            }
                    """);
                break;
        }
    }

    /// <summary>0 = VRC2a, 1 = VRC4 (A1 high bit), 2 = VRC4 (A0 high bit), 3 = VRC6.</summary>
    private int VrcWiringCode()
    {
        return _descriptor.Id switch
        {
            22 => 0,
            21 or 23 or 25 => 1,
            24 or 26 => 3,
            _ => 1
        };
    }

    // =================================================================
    // Per-family state and sync routines
    // =================================================================

    private void EmitFamilyState()
    {
        switch (_descriptor.Family)
        {
            case MapperFamily.Mmc1:
                Raw("""
                            static int _shift, _count, _ctrl = 0x0C, _chr0, _chr1, _prgReg;

                            static void Sync()
                            {
                                int mode = (_ctrl & 3);
                                Runtime.Mirroring = mode == 0 ? 3 : (mode == 1 ? 4 : (mode == 2 ? 1 : 0));
                                int outer = _chr0 & 0x10;
                                int prg = (_prgReg & 0x0F) + outer;
                                switch ((_ctrl >> 2) & 3)
                                {
                                    case 0: case 1:
                                        int baseBank = (_prgReg & 0x0E) + outer;
                                        SetPrg16(0x8000, baseBank); SetPrg16(0xC000, baseBank + 1);
                                        break;
                                    case 2:
                                        SetPrg16(0x8000, outer); SetPrg16(0xC000, prg);
                                        break;
                                    default:
                                        SetPrg16(0x8000, prg); SetPrg16(0xC000, 0x0F + outer);
                                        break;
                                }
                                if ((_ctrl & 0x10) == 0) SetChr8(_chr0 >> 1);
                                else { SetChr4(0x0000, _chr0); SetChr4(0x1000, _chr1); }
                            }

                    """);
                break;

            case MapperFamily.Mmc3:
                Raw("""
                            static int _cmd;

                            // Reset values, not zeros. Hardware leaves the eight bank
                            // registers holding 0,2,4,5,6,7,0,1 — the "sane" CHR layout a
                            // game can rely on before it programs anything. Zeroing them
                            // makes every CHR bank point at bank 0, so a game that starts
                            // rendering before its first $8001 write draws the same tile
                            // pattern everywhere. That was the grey screen with garbled
                            // sprites on Battletoads, Castlevania, Metroid and Kirby.
                            static int[] _r = new int[8] { 0, 2, 4, 5, 6, 7, 0, 1 };
                            static int _irqLatch, _irqCounter;
                            static bool _irqReload, _irqEnabled, _irqPending;

                            static void Sync()
                            {
                                int last = _prgBanks8 - 1;
                                int secondLast = last - 1 < 0 ? 0 : last - 1;
                                if ((_cmd & 0x40) == 0)
                                {
                                    SetPrg8(0x8000, _r[6]); SetPrg8(0xA000, _r[7]); SetPrg8(0xC000, secondLast);
                                }
                                else
                                {
                                    SetPrg8(0x8000, secondLast); SetPrg8(0xA000, _r[7]); SetPrg8(0xC000, _r[6]);
                                }
                                SetPrg8(0xE000, last);
                                if ((_cmd & 0x80) == 0)
                                {
                                    SetChr2(0x0000, _r[0] >> 1); SetChr2(0x0800, _r[1] >> 1);
                                    SetChr1(0x1000, _r[2]); SetChr1(0x1400, _r[3]); SetChr1(0x1800, _r[4]); SetChr1(0x1C00, _r[5]);
                                }
                                else
                                {
                                    SetChr2(0x1000, _r[0] >> 1); SetChr2(0x1800, _r[1] >> 1);
                                    SetChr1(0x0000, _r[2]); SetChr1(0x0400, _r[3]); SetChr1(0x0800, _r[4]); SetChr1(0x0C00, _r[5]);
                                }
                            }

                            /// <summary>
                            /// One scanline tick of the MMC3 IRQ counter.
                            ///
                            /// Mirrors FCEUX's <c>ClockMMC3Counter</c>: the value *before*
                            /// the tick decides whether the counter reloads, and the
                            /// trigger is tested after both branches. Testing only in the
                            /// decrement branch misses the case where a reload happens to
                            /// load zero, which is how games get a spurious extra IRQ.
                            /// </summary>
                            public static void ClockScanline()
                            {
                                int count = _irqCounter;

                                if (count == 0 || _irqReload)
                                {
                                    _irqCounter = _irqLatch;
                                    _irqReload = false;
                                }
                                else
                                {
                                    _irqCounter--;
                                }

                                if (_irqCounter == 0 && _irqEnabled)
                                {
                                    _irqPending = true;
                                    Runtime.IrqPending = true;
                                }
                            }

                    """);
                break;

            case MapperFamily.AxRom:
            case MapperFamily.GxRom:
            case MapperFamily.Camerica:
            case MapperFamily.Nina:
            case MapperFamily.Bnrom:
            case MapperFamily.Cprom:
                Raw("            static int _latch;");
                break;

            case MapperFamily.Quattro:
                Raw("""
                            static int _block, _outer;

                            static void Sync()
                            {
                                int bank = _outer * 4 + _block * 2;
                                SetPrg16(0x8000, bank); SetPrg16(0xC000, bank + 1);
                            }

                    """);
                break;

            case MapperFamily.Mmc2:
                Raw("""
                            static int _prgReg;
                            static int[] _latchRegs = new int[4];

                            static void Sync()
                            {
                                int last = _prgBanks8 - 1;
                                SetPrg8(0x8000, _prgReg);
                                SetPrg8(0xA000, last - 2); SetPrg8(0xC000, last - 1); SetPrg8(0xE000, last);
                                SetChr4(0x0000, _latchRegs[0]); SetChr4(0x1000, _latchRegs[2]);
                            }

                            /// <summary>
                            /// MMC2 latches CHR on pattern fetches of tiles $FD/$FE. The PPU calls
                            /// this from its tile fetch path; the argument is the pattern address.
                            /// </summary>
                            public static void LatchChr(int patternAddr, int tile)
                            {
                                if (patternAddr < 0x1000)
                                {
                                    if (tile == 0xFD) SetChr4(0x0000, _latchRegs[0]);
                                    else if (tile == 0xFE) SetChr4(0x0000, _latchRegs[1]);
                                }
                                else
                                {
                                    if (tile == 0xFD) SetChr4(0x1000, _latchRegs[2]);
                                    else if (tile == 0xFE) SetChr4(0x1000, _latchRegs[3]);
                                }
                            }

                    """);
                break;

            case MapperFamily.Fme7:
                Raw("""
                            static int _cmd;
                            static int[] _chrRegs = new int[8];
                            static int[] _prgRegs = new int[3];
                            static int _irqCounter, _irqControl;
                            static bool _irqPending;

                            static void Sync()
                            {
                                SetPrg8(0x8000, _prgRegs[0]);
                                SetPrg8(0xA000, _prgRegs[1]);
                                SetPrg8(0xC000, _prgRegs[2]);
                                SetPrg8(0xE000, _prgBanks8 - 1);
                                for (int i = 0; i < 8; i++) SetChr1((ushort)(i * 0x400), _chrRegs[i]);
                            }

                            public static void ClockScanline()
                            {
                                if ((_irqControl & 0x80) == 0) return;
                                _irqCounter -= (_irqControl & 1) != 0 ? 1 : 3;
                                if (_irqCounter <= 0) { _irqCounter = 0xFFFF; _irqPending = true; Runtime.IrqPending = true; }
                            }

                    """);
                break;

            case MapperFamily.Vrc2:
            case MapperFamily.Vrc4:
            case MapperFamily.Vrc6:
                EmitVrcState();
                break;

            default:
                Raw("            // Stateless mapper: the page tables are the whole state.");
                break;
        }

        EmitStubMethods();
    }

    private void EmitVrcState()
    {
        Raw("""
                    static int[] _chrRegs = new int[8];
                    static int[] _prgRegs = new int[3];
            """);

        if (VrcWiringCode() != 0)
            Raw("        static int _prgSwap;");

        if (HasIrq)
            Raw("        static int _irqLatch, _irqCounter, _irqControl;");

        if (HasIrq)
            Raw("        static bool _irqPending;");

        Raw("""
                    static void Sync()
                    {
                        int last = _prgBanks8 - 1;
            """);

        if (VrcWiringCode() == 3)
        {
            Raw("""
                            SetPrg8(0x8000, _prgRegs[0]);
                            SetPrg8(0xA000, _prgRegs[1]);
                            SetPrg8(0xC000, _prgRegs[2]);
                            SetPrg8(0xE000, last);
                    """);
        }
        else if (VrcWiringCode() == 0)
        {
            Raw("""
                            SetPrg16(0x8000, _prgRegs[0] >> 1);
                            SetPrg16(0xC000, last / 2);
                    """);
        }
        else
        {
            Raw("""
                            if ((_prgSwap & 0x02) != 0)
                            {
                                SetPrg8(0x8000, last - 1); SetPrg8(0xA000, last);
                                SetPrg8(0xC000, _prgRegs[0]); SetPrg8(0xE000, _prgRegs[1]);
                            }
                            else
                            {
                                SetPrg8(0x8000, _prgRegs[0]); SetPrg8(0xA000, _prgRegs[1]);
                                SetPrg8(0xC000, last - 1); SetPrg8(0xE000, last);
                            }
                    """);
        }

        Raw("""
                        SyncChr();
                    }

                    static void SyncChr()
                    {
                        for (int i = 0; i < 8; i++) SetChr1((ushort)(i * 0x400), _chrRegs[i]);
                    }

            """);

        if (VrcWiringCode() == 0)
        {
            Raw("""
                        static void SetChrPair(int first, int bank)
                        {
                            _chrRegs[first] = bank & 0xFE;
                            _chrRegs[first + 1] = (bank & 0xFE) | 1;
                            SyncChr();
                        }

                """);
        }

        if (HasIrq)
        {
            Raw("""
                        public static void ClockScanline()
                        {
                            if ((_irqControl & 0x02) == 0) return;
                            _irqCounter++;
                            if (_irqCounter > 0xFF) { _irqCounter = _irqLatch; _irqPending = true; Runtime.IrqPending = true; }
                        }

                """);
        }
        else
        {
            // VRC2 has no scanline counter, but the PPU calls this unconditionally.
            Raw("""
                        public static void ClockScanline()
                        {
                        }

                """);
        }
    }

    /// <summary>
    /// The PPU and the interpreter call these unconditionally; the JIT folds
    /// the <c>const bool</c> checks, but the methods must still exist.
    /// </summary>
    private void EmitStubMethods()
    {
        // Families that always emit their own ClockScanline, with or without IRQ.
        bool familyOwnsIrqMethod = _descriptor.Family is
            MapperFamily.Mmc3 or
            MapperFamily.Fme7 or
            MapperFamily.Vrc2 or MapperFamily.Vrc4 or MapperFamily.Vrc6;

        if (!familyOwnsIrqMethod)
        {
            Raw("""
                        public static void ClockScanline()
                        {
                        }

                """);
        }

        if (!HasChrLatch)
        {
            Raw("""
                        public static void LatchChr(int patternAddr, int tile)
                        {
                        }

                """);
        }
    }

    // =================================================================
    // Helpers
    // =================================================================

    /// <summary>
    /// Appends a raw string literal verbatim, normalising line endings to the
    /// host's. Raw literals keep braces, quotes and backslashes exactly as
    /// written, which is the whole point: the emitted code is readable here.
    /// </summary>
    private void Raw(string text)
    {
        _sb.Append(text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
        _sb.AppendLine();
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Escape(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
