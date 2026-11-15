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
/// </summary>
public sealed class MapperRuntimeEmitter
{
    private readonly CartInfo _cart;
    private readonly MapperDescriptor _descriptor;
    private readonly StringBuilder _sb;

    public MapperRuntimeEmitter(CartInfo cart)
    {
        _cart = cart ?? throw new ArgumentNullException(nameof(cart));
        _descriptor = MapperCatalog.Get(cart.Mapper);
        _sb = new StringBuilder();
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

        Line(0, "static class MapperStub");
        Line(0, "{");
        Line(1, "public const int MapperId = " + _descriptor.Id + ";");
        Line(1, "public const string MapperName = \"" + Escape(_descriptor.Name) + "\";");
        Line(1, "public const bool HasIrq = " + (HasIrq ? "true" : "false") + ";");
        Line(1, "public const bool HasChrLatch = " + (HasChrLatch ? "true" : "false") + ";");
        Line(0, "");

        EmitPageTables();
        EmitHelpers();
        EmitResetAndPower();
        EmitMemoryInterface();
        EmitFamily();

        Line(0, "}");
        Line(0, "");

        return _sb.ToString();
    }

    // =================================================================
    // Page tables
    // =================================================================

    private void EmitPageTables()
    {
        Line(1, "// 2 KB PRG pages cover $0000-$FFFF; only $6000-$FFFF is ever mapped.");
        Line(1, "public static byte[][] PrgPageMem = new byte[32][];");
        Line(1, "public static int[] PrgPageBase = new int[32];");
        Line(1, "public static bool[] PrgPageIsRam = new bool[32];");
        Line(1, "");
        Line(1, "// 1 KB CHR pages cover $0000-$1FFF.");
        Line(1, "public static byte[][] ChrPageMem = new byte[8][];");
        Line(1, "public static int[] ChrPageBase = new int[8];");
        Line(1, "public static bool[] ChrPageIsRam = new bool[8];");
        Line(1, "");
        Line(1, "static byte[] _prg = Runtime.PrgRom;");
        Line(1, "static byte[] _chr = Runtime.ChrRom.Length > 0 ? Runtime.ChrRom : Runtime.ChrRam;");
        Line(1, "static int _prgBanks8 = Math.Max(1, Runtime.PrgRom.Length / 0x2000);");
        Line(1, "static int _prgBanks16 = Math.Max(1, Runtime.PrgRom.Length / 0x4000);");
        Line(1, "static int _prgBanks32 = Math.Max(1, Runtime.PrgRom.Length / 0x8000);");
        Line(1, "static int _chrBanks1 = Math.Max(1, _chr.Length / 0x400);");
        Line(1, "static int _chrBanks2 = Math.Max(1, _chr.Length / 0x800);");
        Line(1, "static int _chrBanks4 = Math.Max(1, _chr.Length / 0x1000);");
        Line(1, "static int _chrBanks8 = Math.Max(1, _chr.Length / 0x2000);");
        Line(1, "");
    }

    private void EmitHelpers()
    {
        Line(1, "// === PRG banking ===");
        Line(1, "public static void SetPrg8(ushort addr, int bank)");
        Line(1, "{");
        Line(2, "bank = Wrap(bank, _prgBanks8);");
        Line(2, "int page = (addr >> 11) & 31;");
        Line(2, "int baseOff = bank * 0x2000 - addr;");
        Line(2, "for (int i = 0; i < 4; i++)");
        Line(2, "{");
        Line(3, "int p = page + i;");
        Line(3, "if (p >= 32) break;");
        Line(3, "PrgPageMem[p] = _prg; PrgPageBase[p] = baseOff; PrgPageIsRam[p] = false;");
        Line(2, "}");
        Line(1, "}");
        Line(1, "");
        Line(1, "public static void SetPrg16(ushort addr, int bank)");
        Line(1, "{");
        Line(2, "bank = Wrap(bank, _prgBanks16);");
        Line(2, "int page = (addr >> 11) & 31;");
        Line(2, "int baseOff = bank * 0x4000 - addr;");
        Line(2, "for (int i = 0; i < 8; i++)");
        Line(2, "{");
        Line(3, "int p = page + i;");
        Line(3, "if (p >= 32) break;");
        Line(3, "PrgPageMem[p] = _prg; PrgPageBase[p] = baseOff; PrgPageIsRam[p] = false;");
        Line(2, "}");
        Line(1, "}");
        Line(1, "");
        Line(1, "public static void SetPrg32(ushort addr, int bank)");
        Line(1, "{");
        Line(2, "bank = Wrap(bank, _prgBanks32);");
        Line(2, "int page = (addr >> 11) & 31;");
        Line(2, "int baseOff = bank * 0x8000 - addr;");
        Line(2, "for (int i = 0; i < 16; i++)");
        Line(2, "{");
        Line(3, "int p = page + i;");
        Line(3, "if (p >= 32) break;");
        Line(3, "PrgPageMem[p] = _prg; PrgPageBase[p] = baseOff; PrgPageIsRam[p] = false;");
        Line(2, "}");
        Line(1, "}");
        Line(1, "");
        Line(1, "/// <summary>Maps WRAM into $6000-$7FFF. bank is in 8 KB units.</summary>");
        Line(1, "public static void SetWram8(int bank)");
        Line(1, "{");
        Line(2, "if (Runtime.Wram.Length == 0) return;");
        Line(2, "bank = Wrap(bank, Math.Max(1, Runtime.Wram.Length / 0x2000));");
        Line(2, "int baseOff = bank * 0x2000 - 0x6000;");
        Line(2, "for (int i = 0; i < 4; i++)");
        Line(2, "{");
        Line(3, "int p = 12 + i;");
        Line(3, "PrgPageMem[p] = Runtime.Wram; PrgPageBase[p] = baseOff; PrgPageIsRam[p] = true;");
        Line(2, "}");
        Line(1, "}");
        Line(1, "");
        Line(1, "// === CHR banking ===");
        Line(1, "public static void SetChr1(ushort addr, int bank)");
        Line(1, "{");
        Line(2, "bank = Wrap(bank, _chrBanks1);");
        Line(2, "int page = (addr >> 10) & 7;");
        Line(2, "ChrPageMem[page] = _chr; ChrPageBase[page] = bank * 0x400 - addr; ChrPageIsRam[page] = Runtime.ChrRom.Length == 0;");
        Line(1, "}");
        Line(1, "");
        Line(1, "public static void SetChr2(ushort addr, int bank)");
        Line(1, "{");
        Line(2, "bank = Wrap(bank, _chrBanks2);");
        Line(2, "int page = (addr >> 10) & 7;");
        Line(2, "int baseOff = bank * 0x800 - addr;");
        Line(2, "for (int i = 0; i < 2; i++)");
        Line(2, "{");
        Line(3, "int p = page + i;");
        Line(3, "if (p >= 8) break;");
        Line(3, "ChrPageMem[p] = _chr; ChrPageBase[p] = baseOff; ChrPageIsRam[p] = Runtime.ChrRom.Length == 0;");
        Line(2, "}");
        Line(1, "}");
        Line(1, "");
        Line(1, "public static void SetChr4(ushort addr, int bank)");
        Line(1, "{");
        Line(2, "bank = Wrap(bank, _chrBanks4);");
        Line(2, "int page = (addr >> 10) & 7;");
        Line(2, "int baseOff = bank * 0x1000 - addr;");
        Line(2, "for (int i = 0; i < 4; i++)");
        Line(2, "{");
        Line(3, "int p = page + i;");
        Line(3, "if (p >= 8) break;");
        Line(3, "ChrPageMem[p] = _chr; ChrPageBase[p] = baseOff; ChrPageIsRam[p] = Runtime.ChrRom.Length == 0;");
        Line(2, "}");
        Line(1, "}");
        Line(1, "");
        Line(1, "public static void SetChr8(int bank)");
        Line(1, "{");
        Line(2, "bank = Wrap(bank, _chrBanks8);");
        Line(2, "for (int i = 0; i < 8; i++)");
        Line(2, "{");
        Line(3, "ChrPageMem[i] = _chr; ChrPageBase[i] = bank * 0x2000; ChrPageIsRam[i] = Runtime.ChrRom.Length == 0;");
        Line(2, "}");
        Line(1, "}");
        Line(1, "");
        Line(1, "static int Wrap(int bank, int banks)");
        Line(1, "{");
        Line(2, "if (banks <= 1) return 0;");
        Line(2, "if ((banks & (banks - 1)) == 0) return bank & (banks - 1);");
        Line(2, "bank %= banks;");
        Line(2, "return bank < 0 ? bank + banks : bank;");
        Line(1, "}");
        Line(1, "");
        Line(1, "public static byte ReadChrByte(int addr)");
        Line(1, "{");
        Line(2, "int page = (addr >> 10) & 7;");
        Line(2, "byte[] mem = ChrPageMem[page];");
        Line(2, "if (mem == null) return 0;");
        Line(2, "int off = ChrPageBase[page] + addr;");
        Line(2, "if ((uint)off >= (uint)mem.Length) return 0;");
        Line(2, "return mem[off];");
        Line(1, "}");
        Line(1, "");
        Line(1, "public static void WriteChrByte(int addr, byte value)");
        Line(1, "{");
        Line(2, "int page = (addr >> 10) & 7;");
        Line(2, "if (!ChrPageIsRam[page]) return;");
        Line(2, "byte[] mem = ChrPageMem[page];");
        Line(2, "if (mem == null) return;");
        Line(2, "int off = ChrPageBase[page] + addr;");
        Line(2, "if ((uint)off >= (uint)mem.Length) return;");
        Line(2, "mem[off] = value;");
        Line(1, "}");
        Line(1, "");
        Line(1, "public static byte ReadPrg(ushort addr)");
        Line(1, "{");
        Line(2, "int page = (addr >> 11) & 31;");
        Line(2, "byte[] mem = PrgPageMem[page];");
        Line(2, "if (mem == null) return 0;");
        Line(2, "int off = PrgPageBase[page] + addr;");
        Line(2, "if ((uint)off >= (uint)mem.Length) return 0;");
        Line(2, "return mem[off];");
        Line(1, "}");
        Line(1, "");
        Line(1, "static void WritePrgPage(ushort addr, byte value)");
        Line(1, "{");
        Line(2, "int page = (addr >> 11) & 31;");
        Line(2, "if (!PrgPageIsRam[page]) return;");
        Line(2, "byte[] mem = PrgPageMem[page];");
        Line(2, "if (mem == null) return;");
        Line(2, "int off = PrgPageBase[page] + addr;");
        Line(2, "if ((uint)off >= (uint)mem.Length) return;");
        Line(2, "mem[off] = value;");
        Line(1, "}");
        Line(1, "");
    }

    private void EmitResetAndPower()
    {
        Line(1, "public static void Reset()");
        Line(1, "{");
        Line(2, "for (int i = 0; i < 32; i++) { PrgPageMem[i] = null; PrgPageBase[i] = 0; PrgPageIsRam[i] = false; }");
        Line(2, "for (int i = 0; i < 8; i++) { ChrPageMem[i] = null; ChrPageBase[i] = 0; ChrPageIsRam[i] = false; }");
        Line(2, "Array.Clear(Runtime.Wram, 0, Runtime.Wram.Length);");
        Line(2, "Power();");
        Line(1, "}");
        Line(1, "");
        Line(1, "public static void Power()");
        Line(1, "{");
        Line(2, "SetWram8(0);");
        EmitPowerBody();
        Line(1, "}");
        Line(1, "");
    }

    private void EmitMemoryInterface()
    {
        Line(1, "/// <summary>CPU read for $4020-$FFFF.</summary>");
        Line(1, "public static byte Read(ushort addr)");
        Line(1, "{");
        Line(2, "if (addr < 0x8000)");
        Line(2, "{");
        Line(3, "if (addr >= 0x6000) return ReadPrg(addr);");
        Line(3, "return ReadLow(addr);");
        Line(2, "}");
        Line(2, "return ReadPrg(addr);");
        Line(1, "}");
        Line(1, "");
        Line(1, "/// <summary>CPU write for $4020-$FFFF.</summary>");
        Line(1, "public static void Write(ushort addr, byte value)");
        Line(1, "{");
        Line(2, "if (addr < 0x8000)");
        Line(2, "{");
        Line(3, "if (addr >= 0x6000) { WriteWram(addr, value); return; }");
        Line(3, "WriteLow(addr, value);");
        Line(3, "return;");
        Line(2, "}");
        EmitWriteBody();
        Line(1, "}");
        Line(1, "");
        EmitReadLow();
        EmitWriteLow();
        EmitWriteWram();
    }

    private void EmitWriteWram()
    {
        Line(1, "static void WriteWram(ushort addr, byte value)");
        Line(1, "{");

        if (_descriptor.Family == MapperFamily.Mmc1)
        {
            // MMC1 bit 4 of the PRG register disables WRAM writes.
            Line(2, "if ((_prgReg & 0x10) != 0) return;");
        }

        Line(2, "WritePrgPage(addr, value);");
        Line(1, "}");
        Line(1, "");
    }

    private void EmitReadLow()
    {
        Line(1, "static byte ReadLow(ushort addr)");
        Line(1, "{");

        if (_descriptor.Family == MapperFamily.Mmc1)
        {
            // MMC1 returns the high address byte when PRG RAM is disabled,
            // which some games use to detect the WRAM state.
            Line(2, "if (Runtime.Wram.Length > 0 && (_prgReg & 0x10) != 0) return (byte)(addr >> 8);");
        }

        Line(2, "return 0;");
        Line(1, "}");
        Line(1, "");
    }

    private void EmitWriteLow()
    {
        Line(1, "static void WriteLow(ushort addr, byte value)");
        Line(1, "{");

        switch (_descriptor.Family)
        {
            case MapperFamily.Bnrom:
                // NINA-001 maps its CHR register into the $7FFD-$7FFF hole.
                Line(2, "if (addr >= 0x7FFD) { SetChr4(0x0000, value); return; }");
                break;

            case MapperFamily.Fme7:
                Line(2, "// FME-7 exposes $4020-$5FFF as cartridge registers.");
                break;

            default:
                Line(2, "// No low-range registers on this board.");
                break;
        }

        Line(1, "}");
        Line(1, "");
    }

    // =================================================================
    // Per-family code
    // =================================================================

    private void EmitPowerBody()
    {
        switch (_descriptor.Family)
        {
            case MapperFamily.Nrom:
                Line(2, "if (_prgBanks16 <= 1) { SetPrg16(0x8000, 0); SetPrg16(0xC000, 0); }");
                Line(2, "else SetPrg32(0x8000, 0);");
                Line(2, "SetChr8(0);");
                break;

            case MapperFamily.Cprom:
                Line(2, "if (_prgBanks16 <= 1) { SetPrg16(0x8000, 0); SetPrg16(0xC000, 0); }");
                Line(2, "else SetPrg32(0x8000, 0);");
                Line(2, "_latch = 0; Sync();");
                break;

            case MapperFamily.UxRom:
                Line(2, "SetPrg16(0x8000, 0);");
                Line(2, "SetPrg16(0xC000, _prgBanks16 - 1);");
                Line(2, "SetChr8(0);");
                break;

            case MapperFamily.Cnrom:
                Line(2, "if (_prgBanks16 <= 1) { SetPrg16(0x8000, 0); SetPrg16(0xC000, 0); }");
                Line(2, "else SetPrg32(0x8000, 0);");
                Line(2, "SetChr8(0);");
                break;

            case MapperFamily.Mmc1:
                Line(2, "_shift = 0; _count = 0; _ctrl = 0x0C; _chr0 = 0; _chr1 = 0; _prgReg = 0;");
                Line(2, "Sync();");
                break;

            case MapperFamily.Mmc3:
                Line(2, "_cmd = 0; for (int i = 0; i < 8; i++) _r[i] = 0;");
                Line(2, "_irqLatch = 0; _irqCounter = 0; _irqReload = false; _irqEnabled = false; _irqPending = false;");
                Line(2, "Sync();");
                break;

            case MapperFamily.AxRom:
                Line(2, "_latch = 0;");
                Line(2, "SetPrg32(0x8000, 0);");
                Line(2, "SetChr8(0);");
                Line(2, "Runtime.Mirroring = 3;");
                break;

            case MapperFamily.GxRom:
                Line(2, "_latch = 0;");
                Line(2, "SetPrg32(0x8000, 0);");
                Line(2, "SetChr8(0);");
                break;

            case MapperFamily.Camerica:
                Line(2, "_latch = 0;");
                Line(2, "SetPrg16(0x8000, 0);");
                Line(2, "SetPrg16(0xC000, _prgBanks16 - 1);");
                Line(2, "SetChr8(0);");
                break;

            case MapperFamily.Nina:
                Line(2, "_latch = 0;");
                Line(2, "if (_prgBanks16 <= 1) { SetPrg16(0x8000, 0); SetPrg16(0xC000, 0); }");
                Line(2, "else SetPrg32(0x8000, 0);");
                Line(2, "SetChr8(0);");
                break;

            case MapperFamily.Bnrom:
                Line(2, "_latch = 0;");
                Line(2, "SetPrg32(0x8000, 0);");
                Line(2, "SetChr8(0);");
                break;

            case MapperFamily.Quattro:
                Line(2, "_block = 0; _outer = 0;");
                Line(2, "Sync();");
                break;

            case MapperFamily.Mmc2:
                Line(2, "_prgReg = 0; for (int i = 0; i < 4; i++) _latchRegs[i] = 0;");
                Line(2, "Sync();");
                break;

            case MapperFamily.Fme7:
                Line(2, "_cmd = 0; for (int i = 0; i < 8; i++) _chrRegs[i] = 0;");
                Line(2, "_prgRegs[0] = 0; _prgRegs[1] = 1; _prgRegs[2] = 2;");
                Line(2, "_irqCounter = 0; _irqControl = 0; _irqPending = false;");
                Line(2, "Sync();");
                break;

            case MapperFamily.Vrc2:
            case MapperFamily.Vrc4:
            case MapperFamily.Vrc6:
                Line(2, "for (int i = 0; i < 8; i++) _chrRegs[i] = 0;");
                Line(2, "_prgRegs[0] = 0; _prgRegs[1] = 1; _prgRegs[2] = 2;");

                if (VrcWiringCode() != 0)
                    Line(2, "_prgSwap = 0;");

                if (HasIrq)
                    Line(2, "_irqLatch = 0; _irqCounter = 0; _irqControl = 0;");

                Line(2, "Sync();");
                break;

            default:
                Line(2, "SetPrg32(0x8000, 0);");
                Line(2, "SetChr8(0);");
                break;
        }
    }

    private void EmitWriteBody()
    {
        switch (_descriptor.Family)
        {
            case MapperFamily.Nrom:
                Line(2, "// NROM has no bank registers.");
                break;

            case MapperFamily.Cprom:
                Line(2, "value &= ReadPrg(addr);   // bus conflict");
                Line(2, "_latch = value; Sync();");
                break;

            case MapperFamily.UxRom:
                Line(2, "SetPrg16(0x8000, value);");
                break;

            case MapperFamily.Cnrom:
                Line(2, "SetChr8(value);");
                break;

            case MapperFamily.Mmc1:
                EmitMmc1Write();
                break;

            case MapperFamily.Mmc3:
                EmitMmc3Write();
                break;

            case MapperFamily.AxRom:
                Line(2, "_latch = value;");
                Line(2, "SetPrg32(0x8000, value & 0x07);");
                Line(2, "Runtime.Mirroring = (value & 0x10) != 0 ? 4 : 3;");
                break;

            case MapperFamily.GxRom:
                Line(2, "value &= ReadPrg(addr);   // bus conflict");
                Line(2, "_latch = value;");
                Line(2, "SetPrg32(0x8000, (value >> 4) & 0x03);");
                Line(2, "SetChr8(value & 0x03);");
                break;

            case MapperFamily.Camerica:
                Line(2, "value &= ReadPrg(addr);   // bus conflict");
                Line(2, "_latch = value;");
                Line(2, "SetPrg16(0x8000, value & 0x0F);");
                Line(2, "Runtime.Mirroring = (value & 0x10) != 0 ? 4 : 3;");
                break;

            case MapperFamily.Nina:
                Line(2, "value &= ReadPrg(addr);   // bus conflict");
                Line(2, "_latch = value;");
                Line(2, "SetChr8(value);");
                if (_descriptor.Id != 87)
                    Line(2, "Runtime.Mirroring = (value & 0x08) != 0 ? 1 : 0;");
                break;

            case MapperFamily.Bnrom:
                Line(2, "_latch = value;");
                Line(2, "SetPrg32(0x8000, value & 0x0F);");
                break;

            case MapperFamily.Quattro:
                Line(2, "if (addr >= 0xC000) _outer = (value >> 2) & 0x07;");
                Line(2, "else _block = (value >> 3) & 0x03;");
                Line(2, "Sync();");
                break;

            case MapperFamily.Mmc2:
                EmitMmc2Write();
                break;

            case MapperFamily.Fme7:
                EmitFme7Write();
                break;

            case MapperFamily.Vrc2:
            case MapperFamily.Vrc4:
            case MapperFamily.Vrc6:
                EmitVrcWrite();
                break;

            default:
                Line(2, "// No bank registers for this mapper.");
                break;
        }
    }

    private void EmitMmc1Write()
    {
        Line(2, "if ((value & 0x80) != 0)");
        Line(2, "{");
        Line(3, "_ctrl |= 0x0C; _shift = 0; _count = 0; Sync(); return;");
        Line(2, "}");
        Line(2, "_shift = (_shift >> 1) | ((value & 1) << 4);");
        Line(2, "_count++;");
        Line(2, "if (_count < 5) return;");
        Line(2, "int v = _shift & 0x1F; _shift = 0; _count = 0;");
        Line(2, "if (addr <= 0x9FFF) _ctrl = v;");
        Line(2, "else if (addr <= 0xBFFF) _chr0 = v;");
        Line(2, "else if (addr <= 0xDFFF) _chr1 = v;");
        Line(2, "else _prgReg = v;");
        Line(2, "Sync();");
    }

    private void EmitMmc1Sync()
    {
        Line(1, "static void Sync()");
        Line(1, "{");
        Line(2, "int mode = (_ctrl & 3);");
        Line(2, "Runtime.Mirroring = mode == 0 ? 3 : (mode == 1 ? 4 : (mode == 2 ? 1 : 0));");
        Line(2, "int outer = _chr0 & 0x10;");
        Line(2, "int prg = (_prgReg & 0x0F) + outer;");
        Line(2, "switch ((_ctrl >> 2) & 3)");
        Line(2, "{");
        Line(3, "case 0: case 1:");
        Line(4, "int baseBank = (_prgReg & 0x0E) + outer;");
        Line(4, "SetPrg16(0x8000, baseBank); SetPrg16(0xC000, baseBank + 1);");
        Line(4, "break;");
        Line(3, "case 2:");
        Line(4, "SetPrg16(0x8000, outer); SetPrg16(0xC000, prg);");
        Line(4, "break;");
        Line(3, "default:");
        Line(4, "SetPrg16(0x8000, prg); SetPrg16(0xC000, 0x0F + outer);");
        Line(4, "break;");
        Line(2, "}");
        Line(2, "if ((_ctrl & 0x10) == 0) SetChr8(_chr0 >> 1);");
        Line(2, "else { SetChr4(0x0000, _chr0); SetChr4(0x1000, _chr1); }");
        Line(1, "}");
        Line(1, "");
    }

    private void EmitMmc3Write()
    {
        Line(2, "bool odd = (addr & 1) != 0;");
        Line(2, "switch (addr & 0xE000)");
        Line(2, "{");
        Line(3, "case 0x8000:");
        Line(4, "if (!odd) _cmd = value; else _r[_cmd & 7] = value;");
        Line(4, "Sync();");
        Line(4, "break;");
        Line(3, "case 0xA000:");
        Line(4, "if (!odd) Runtime.Mirroring = (value & 1) == 0 ? 1 : 0;");
        Line(4, "break;");
        Line(3, "case 0xC000:");
        Line(4, "if (!odd) _irqLatch = value; else { _irqCounter = 0; _irqReload = true; }");
        Line(4, "break;");
        Line(3, "case 0xE000:");
        Line(4, "if (!odd) { _irqEnabled = false; _irqPending = false; Runtime.IrqPending = false; }");
        Line(4, "else _irqEnabled = true;");
        Line(4, "break;");
        Line(2, "}");
    }

    private void EmitMmc3Sync()
    {
        Line(1, "static void Sync()");
        Line(1, "{");
        Line(2, "int last = _prgBanks8 - 1;");
        Line(2, "int secondLast = last - 1 < 0 ? 0 : last - 1;");
        Line(2, "if ((_cmd & 0x40) == 0)");
        Line(2, "{");
        Line(3, "SetPrg8(0x8000, _r[6]); SetPrg8(0xA000, _r[7]); SetPrg8(0xC000, secondLast);");
        Line(2, "}");
        Line(2, "else");
        Line(2, "{");
        Line(3, "SetPrg8(0x8000, secondLast); SetPrg8(0xA000, _r[7]); SetPrg8(0xC000, _r[6]);");
        Line(2, "}");
        Line(2, "SetPrg8(0xE000, last);");
        Line(2, "if ((_cmd & 0x80) == 0)");
        Line(2, "{");
        Line(3, "SetChr2(0x0000, _r[0] >> 1); SetChr2(0x0800, _r[1] >> 1);");
        Line(3, "SetChr1(0x1000, _r[2]); SetChr1(0x1400, _r[3]); SetChr1(0x1800, _r[4]); SetChr1(0x1C00, _r[5]);");
        Line(2, "}");
        Line(2, "else");
        Line(2, "{");
        Line(3, "SetChr2(0x1000, _r[0] >> 1); SetChr2(0x1800, _r[1] >> 1);");
        Line(3, "SetChr1(0x0000, _r[2]); SetChr1(0x0400, _r[3]); SetChr1(0x0800, _r[4]); SetChr1(0x0C00, _r[5]);");
        Line(2, "}");
        Line(1, "}");
        Line(1, "");
    }

    private void EmitMmc3Irq()
    {
        Line(1, "public static void ClockScanline()");
        Line(1, "{");
        Line(2, "if (_irqCounter == 0 || _irqReload)");
        Line(2, "{");
        Line(3, "_irqCounter = _irqLatch; _irqReload = false;");
        Line(2, "}");
        Line(2, "else");
        Line(2, "{");
        Line(3, "_irqCounter--;");
        Line(3, "if (_irqCounter == 0 && _irqEnabled) { _irqPending = true; Runtime.IrqPending = true; }");
        Line(2, "}");
        Line(1, "}");
        Line(1, "");
    }

    private void EmitMmc2Write()
    {
        Line(2, "switch (addr & 0xF000)");
        Line(2, "{");
        Line(3, "case 0xA000: _prgReg = value; SetPrg8(0x8000, value); break;");
        Line(3, "case 0xB000: _latchRegs[0] = value; SetChr4(0x0000, value); break;");
        Line(3, "case 0xC000: _latchRegs[1] = value; SetChr4(0x0000, value); break;");
        Line(3, "case 0xD000: _latchRegs[2] = value; SetChr4(0x1000, value); break;");
        Line(3, "case 0xE000: _latchRegs[3] = value; SetChr4(0x1000, value); break;");
        Line(3, "case 0xF000: Runtime.Mirroring = (value & 1) != 0 ? 0 : 1; break;");
        Line(2, "}");
    }

    private void EmitMmc2Sync()
    {
        Line(1, "static void Sync()");
        Line(1, "{");
        Line(2, "int last = _prgBanks8 - 1;");
        Line(2, "SetPrg8(0x8000, _prgReg);");
        Line(2, "SetPrg8(0xA000, last - 2); SetPrg8(0xC000, last - 1); SetPrg8(0xE000, last);");
        Line(2, "SetChr4(0x0000, _latchRegs[0]); SetChr4(0x1000, _latchRegs[2]);");
        Line(1, "}");
        Line(1, "");
        Line(1, "/// <summary>");
        Line(1, "/// MMC2 latches CHR on pattern fetches of tiles $FD/$FE. The PPU calls");
        Line(1, "/// this from its tile fetch path; the argument is the pattern address.");
        Line(1, "/// </summary>");
        Line(1, "public static void LatchChr(int patternAddr, int tile)");
        Line(1, "{");
        Line(2, "if (patternAddr < 0x1000)");
        Line(2, "{");
        Line(3, "if (tile == 0xFD) SetChr4(0x0000, _latchRegs[0]);");
        Line(3, "else if (tile == 0xFE) SetChr4(0x0000, _latchRegs[1]);");
        Line(2, "}");
        Line(2, "else");
        Line(2, "{");
        Line(3, "if (tile == 0xFD) SetChr4(0x1000, _latchRegs[2]);");
        Line(3, "else if (tile == 0xFE) SetChr4(0x1000, _latchRegs[3]);");
        Line(2, "}");
        Line(1, "}");
        Line(1, "");
    }

    private void EmitFme7Write()
    {
        Line(2, "if (addr < 0xA000) { _cmd = value & 0x0F; return; }");
        Line(2, "switch (_cmd)");
        Line(2, "{");
        Line(3, "case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7:");
        Line(4, "_chrRegs[_cmd] = value; SetChr1((ushort)(_cmd * 0x400), value);");
        Line(4, "break;");
        Line(3, "case 8: _prgRegs[0] = value & 0x3F; Sync(); break;");
        Line(3, "case 9: _prgRegs[1] = value & 0x3F; Sync(); break;");
        Line(3, "case 0x0A: _prgRegs[2] = value & 0x3F; Sync(); break;");
        Line(3, "case 0x0B: break;   // WRAM enable");
        Line(3, "case 0x0C: Runtime.Mirroring = (value & 3) switch { 0 => 1, 1 => 0, 2 => 3, _ => 4 }; break;");
        Line(3, "case 0x0D:");
        Line(4, "_irqControl = value;");
        Line(4, "if ((value & 0x80) == 0) { _irqPending = false; Runtime.IrqPending = false; }");
        Line(4, "break;");
        Line(3, "case 0x0E: _irqCounter = (_irqCounter & 0xFF00) | value; break;");
        Line(3, "case 0x0F: _irqCounter = (_irqCounter & 0x00FF) | (value << 8); _irqPending = false; Runtime.IrqPending = false; break;");
        Line(2, "}");
    }

    private void EmitFme7Sync()
    {
        Line(1, "static void Sync()");
        Line(1, "{");
        Line(2, "SetPrg8(0x8000, _prgRegs[0]);");
        Line(2, "SetPrg8(0xA000, _prgRegs[1]);");
        Line(2, "SetPrg8(0xC000, _prgRegs[2]);");
        Line(2, "SetPrg8(0xE000, _prgBanks8 - 1);");
        Line(2, "for (int i = 0; i < 8; i++) SetChr1((ushort)(i * 0x400), _chrRegs[i]);");
        Line(1, "}");
        Line(1, "");
        Line(1, "public static void ClockScanline()");
        Line(1, "{");
        Line(2, "if ((_irqControl & 0x80) == 0) return;");
        Line(2, "_irqCounter -= (_irqControl & 1) != 0 ? 1 : 3;");
        Line(2, "if (_irqCounter <= 0) { _irqCounter = 0xFFFF; _irqPending = true; Runtime.IrqPending = true; }");
        Line(1, "}");
        Line(1, "");
    }

    private void EmitVrcWrite()
    {
        int wiring = VrcWiringCode();

        switch (wiring)
        {
            case 0: // VRC2a
                Line(2, "switch (addr & 0xF000)");
                Line(2, "{");
                Line(3, "case 0x8000: _prgRegs[0] = value & 0x0F; Sync(); break;");
                Line(3, "case 0x9000: Runtime.Mirroring = (value & 1) != 0 ? 0 : 1; break;");
                Line(3, "case 0xA000: _prgRegs[1] = value & 0x0F; Sync(); break;");
                Line(3, "case 0xB000: SetChrPair(0, value >> 1); break;");
                Line(3, "case 0xC000: SetChrPair(2, value >> 1); break;");
                Line(3, "case 0xD000: SetChrPair(4, value >> 1); break;");
                Line(3, "case 0xE000: SetChrPair(6, value >> 1); break;");
                Line(2, "}");
                break;

            case 1: // VRC4 with A1 as the high bit
            case 2: // VRC4 with A0 as the high bit
                Line(2, "int index = (addr >> 11) & 0x03;");
                Line(2, "bool high = " + (wiring == 1 ? "(addr & 0x02) != 0;" : "(addr & 0x01) != 0;"));
                Line(2, "switch (addr & 0xF000)");
                Line(2, "{");
                Line(3, "case 0x8000:");
                Line(4, "if (high) _prgRegs[1] = value & 0x1F; else _prgRegs[0] = value & 0x1F;");
                Line(4, "Sync();");
                Line(4, "break;");
                Line(3, "case 0x9000:");
                Line(4, "if (!high) Runtime.Mirroring = (value & 1) != 0 ? 0 : 1;");
                Line(4, "else { _prgSwap = value & 0x02; Sync(); }");
                Line(4, "break;");
                Line(3, "case 0xA000: case 0xB000: case 0xC000: case 0xD000: case 0xE000:");
                Line(4, "_chrRegs[index * 2 + (high ? 1 : 0)] = value;");
                Line(4, "SyncChr();");
                Line(4, "break;");
                Line(3, "case 0xF000:");
                Line(4, "if (!high) _irqLatch = (_irqLatch & 0xF0) | (value & 0x0F);");
                Line(4, "else _irqLatch = (_irqLatch & 0x0F) | ((value & 0x0F) << 4);");
                Line(4, "break;");
                Line(2, "}");
                break;

            default: // VRC6
                Line(2, "switch (addr & 0xF000)");
                Line(2, "{");
                Line(3, "case 0x8000: _prgRegs[0] = value & 0x0F; Sync(); break;");
                Line(3, "case 0x9000: _prgRegs[1] = value & 0x0F; Sync(); break;");
                Line(3, "case 0xA000: _prgRegs[2] = value & 0x0F; Sync(); break;");
                Line(3, "case 0xB000:");
                Line(4, "if ((addr & 3) == 3) { Runtime.Mirroring = (value & 1) != 0 ? 0 : 1; break; }");
                Line(4, "_chrRegs[addr & 3] = value; SyncChr();");
                Line(4, "break;");
                Line(3, "case 0xC000: _chrRegs[3 + (addr & 3)] = value; SyncChr(); break;");
                Line(3, "case 0xD000: _chrRegs[4 + (addr & 3)] = value; SyncChr(); break;");
                Line(3, "case 0xE000: _chrRegs[6 + (addr & 3)] = value; SyncChr(); break;");
                Line(3, "case 0xF000:");
                Line(4, "switch (addr & 3)");
                Line(4, "{");
                Line(5, "case 0: _irqLatch = value; break;");
                Line(5, "case 1: _irqCounter = _irqLatch; _irqPending = false; Runtime.IrqPending = false; break;");
                Line(5, "case 2: _irqControl = value; _irqPending = false; Runtime.IrqPending = false; break;");
                Line(4, "}");
                Line(4, "break;");
                Line(2, "}");
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

    private void EmitVrcHelpers()
    {
        Line(1, "static void Sync()");
        Line(1, "{");
        Line(2, "int last = _prgBanks8 - 1;");

        if (VrcWiringCode() == 3)
        {
            Line(2, "SetPrg8(0x8000, _prgRegs[0]);");
            Line(2, "SetPrg8(0xA000, _prgRegs[1]);");
            Line(2, "SetPrg8(0xC000, _prgRegs[2]);");
            Line(2, "SetPrg8(0xE000, last);");
        }
        else if (VrcWiringCode() == 0)
        {
            Line(2, "SetPrg16(0x8000, _prgRegs[0] >> 1);");
            Line(2, "SetPrg16(0xC000, last / 2);");
        }
        else
        {
            Line(2, "if ((_prgSwap & 0x02) != 0)");
            Line(2, "{");
            Line(3, "SetPrg8(0x8000, last - 1); SetPrg8(0xA000, last);");
            Line(3, "SetPrg8(0xC000, _prgRegs[0]); SetPrg8(0xE000, _prgRegs[1]);");
            Line(2, "}");
            Line(2, "else");
            Line(2, "{");
            Line(3, "SetPrg8(0x8000, _prgRegs[0]); SetPrg8(0xA000, _prgRegs[1]);");
            Line(3, "SetPrg8(0xC000, last - 1); SetPrg8(0xE000, last);");
            Line(2, "}");
        }

        Line(2, "SyncChr();");
        Line(1, "}");
        Line(1, "");
        Line(1, "static void SyncChr()");
        Line(1, "{");
        Line(2, "for (int i = 0; i < 8; i++) SetChr1((ushort)(i * 0x400), _chrRegs[i]);");
        Line(1, "}");
        Line(1, "");

        if (VrcWiringCode() == 0)
        {
            Line(1, "static void SetChrPair(int first, int bank)");
            Line(1, "{");
            Line(2, "_chrRegs[first] = bank & 0xFE;");
            Line(2, "_chrRegs[first + 1] = (bank & 0xFE) | 1;");
            Line(2, "SyncChr();");
            Line(1, "}");
            Line(1, "");
        }
    }

    private void EmitVrcIrq()
    {
        Line(1, "public static void ClockScanline()");
        Line(1, "{");
        Line(2, "if ((_irqControl & 0x02) == 0) return;");
        Line(2, "_irqCounter++;");
        Line(2, "if (_irqCounter > 0xFF) { _irqCounter = _irqLatch; _irqPending = true; Runtime.IrqPending = true; }");
        Line(1, "}");
        Line(1, "");
    }

    private void EmitFamily()
    {
        switch (_descriptor.Family)
        {
            case MapperFamily.Mmc1:
                Line(1, "static int _shift, _count, _ctrl = 0x0C, _chr0, _chr1, _prgReg;");
                Line(1, "");
                EmitMmc1Sync();
                break;

            case MapperFamily.Mmc3:
                Line(1, "static int _cmd;");
                Line(1, "static int[] _r = new int[8];");
                Line(1, "static int _irqLatch, _irqCounter;");
                Line(1, "static bool _irqReload, _irqEnabled, _irqPending;");
                Line(1, "");
                EmitMmc3Sync();
                EmitMmc3Irq();
                break;

            case MapperFamily.AxRom:
            case MapperFamily.GxRom:
            case MapperFamily.Camerica:
            case MapperFamily.Nina:
            case MapperFamily.Bnrom:
                Line(1, "static int _latch;");
                break;

            case MapperFamily.Quattro:
                Line(1, "static int _block, _outer;");
                Line(1, "");
                Line(1, "static void Sync()");
                Line(1, "{");
                Line(2, "int bank = _outer * 4 + _block * 2;");
                Line(2, "SetPrg16(0x8000, bank); SetPrg16(0xC000, bank + 1);");
                Line(1, "}");
                Line(1, "");
                break;

            case MapperFamily.Mmc2:
                Line(1, "static int _prgReg;");
                Line(1, "static int[] _latchRegs = new int[4];");
                Line(1, "");
                EmitMmc2Sync();
                break;

            case MapperFamily.Fme7:
                Line(1, "static int _cmd;");
                Line(1, "static int[] _chrRegs = new int[8];");
                Line(1, "static int[] _prgRegs = new int[3];");
                Line(1, "static int _irqCounter, _irqControl;");
                Line(1, "static bool _irqPending;");
                Line(1, "");
                EmitFme7Sync();
                break;

            case MapperFamily.Vrc2:
            case MapperFamily.Vrc4:
            case MapperFamily.Vrc6:
                Line(1, "static int[] _chrRegs = new int[8];");
                Line(1, "static int[] _prgRegs = new int[3];");

                if (VrcWiringCode() != 0)
                    Line(1, "static int _prgSwap;");

                if (HasIrq)
                {
                    Line(1, "static int _irqLatch, _irqCounter, _irqControl;");
                    Line(1, "static bool _irqPending;");
                }

                Line(1, "");
                EmitVrcHelpers();

                if (HasIrq)
                    EmitVrcIrq();

                break;

            case MapperFamily.Cprom:
                Line(1, "static int _latch;");
                Line(1, "");
                Line(1, "static void Sync()");
                Line(1, "{");
                Line(2, "SetChr4(0x0000, 0);");
                Line(2, "SetChr4(0x1000, _latch & 0x03);");
                Line(1, "}");
                Line(1, "");
                break;

            default:
                Line(1, "// Stateless mapper: the page tables are the whole state.");
                break;
        }

        if (!HasIrq)
        {
            // Keep the call site in the PPU unconditional: the JIT folds the
            // const HasIrq check, but the method must still exist.
            Line(1, "public static void ClockScanline()");
            Line(1, "{");
            Line(1, "}");
            Line(1, "");
        }

        if (!HasChrLatch)
        {
            Line(1, "public static void LatchChr(int patternAddr, int tile)");
            Line(1, "{");
            Line(1, "}");
            Line(1, "");
        }

        Line(1, "");
    }

    // =================================================================
    // Helpers
    // =================================================================

    private void Line(int indent, string text)
    {
        _sb.Append(' ', indent * 4).AppendLine(text);
    }

    private static string Escape(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
