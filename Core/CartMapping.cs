#nullable disable

using System;

namespace NesLifter.Core;

/// <summary>
/// Аналог page-table модели из cart.c.
/// Не копирует C-указатели, а хранит память и adjustment для C#.
/// </summary>
public sealed class CartMapping
{
    public const int PrgPageCount = 32;
    public const int ChrPageCount = 8;

    public const int PrgPageSize = 0x800;  // 2 KB
    public const int ChrPageSize = 0x400;  // 1 KB

    private readonly byte[][] _prgPageMem = new byte[PrgPageCount][];
    private readonly int[] _prgPageAdjust = new int[PrgPageCount];
    private readonly bool[] _prgPageIsRam = new bool[PrgPageCount];

    private readonly byte[][] _chrPageMem = new byte[ChrPageCount][];
    private readonly int[] _chrPageAdjust = new int[ChrPageCount];
    private readonly bool[] _chrPageIsRam = new bool[ChrPageCount];

    private readonly ChipInfo[] _prgChips = new ChipInfo[32];
    private readonly ChipInfo[] _chrChips = new ChipInfo[32];

    public void Reset()
    {
        for (int i = 0; i < PrgPageCount; i++)
        {
            _prgPageMem[i] = null;
            _prgPageAdjust[i] = 0;
            _prgPageIsRam[i] = false;
        }

        for (int i = 0; i < ChrPageCount; i++)
        {
            _chrPageMem[i] = null;
            _chrPageAdjust[i] = 0;
            _chrPageIsRam[i] = false;
        }

        for (int i = 0; i < 32; i++)
        {
            _prgChips[i] = null;
            _chrChips[i] = null;
        }
    }

    public void SetupPrgMapping(int chip, byte[] mem, int size, bool ram)
    {
        if ((uint)chip >= 32)
            return;

        ChipInfo info = new ChipInfo
        {
            Mem = mem ?? Array.Empty<byte>(),
            Size = size,
            Ram = ram
        };

        info.Mask2 = MakeMask(size, 11);
        info.Mask4 = MakeMask(size, 12);
        info.Mask8 = MakeMask(size, 13);
        info.Mask16 = MakeMask(size, 14);
        info.Mask32 = MakeMask(size, 15);

        _prgChips[chip] = info;
    }

    public void SetupChrMapping(int chip, byte[] mem, int size, bool ram)
    {
        if ((uint)chip >= 32)
            return;

        ChipInfo info = new ChipInfo
        {
            Mem = mem ?? Array.Empty<byte>(),
            Size = size,
            Ram = ram
        };

        info.Mask1 = MakeMask(size, 10);
        info.Mask2 = MakeMask(size, 11);
        info.Mask4 = MakeMask(size, 12);
        info.Mask8 = MakeMask(size, 13);

        _chrChips[chip] = info;
    }

    public void SetPrg2(uint address, uint bank) => SetPrg2r(0, address, bank);
    public void SetPrg4(uint address, uint bank) => SetPrg4r(0, address, bank);
    public void SetPrg8(uint address, uint bank) => SetPrg8r(0, address, bank);
    public void SetPrg16(uint address, uint bank) => SetPrg16r(0, address, bank);
    public void SetPrg32(uint address, uint bank) => SetPrg32r(0, address, bank);

    public void SetChr1(uint address, uint bank) => SetChr1r(0, address, bank);
    public void SetChr2(uint address, uint bank) => SetChr2r(0, address, bank);
    public void SetChr4(uint address, uint bank) => SetChr4r(0, address, bank);
    public void SetChr8(uint bank) => SetChr8r(0, bank);

    public void SetPrg2r(int chip, uint address, uint bank)
    {
        ChipInfo c = GetPrgChip(chip);

        if (c == null || c.Size < 2048)
        {
            SetPrgPages(2, address, null, 0, false);
            return;
        }

        bank &= c.Mask2;
        SetPrgPages(2, address, c.Mem, (int)(bank << 11), c.Ram);
    }

    public void SetPrg4r(int chip, uint address, uint bank)
    {
        ChipInfo c = GetPrgChip(chip);

        if (c == null || c.Size < 4096)
        {
            if (c != null && c.Size >= 2048)
            {
                uint va = bank << 1;

                for (int x = 0; x < 2; x++)
                    SetPrg2r(chip, address + (uint)(x << 11), va + (uint)x);
            }
            else
            {
                SetPrgPages(2, address, null, 0, false);
                SetPrgPages(2, address + 0x800, null, 0, false);
            }

            return;
        }

        bank &= c.Mask4;
        SetPrgPages(4, address, c.Mem, (int)(bank << 12), c.Ram);
    }

    public void SetPrg8r(int chip, uint address, uint bank)
    {
        ChipInfo c = GetPrgChip(chip);

        if (c == null)
        {
            for (int x = 0; x < 4; x++)
                SetPrgPages(2, address + (uint)(x << 11), null, 0, false);

            return;
        }

        if (c.Size >= 8192)
        {
            bank &= c.Mask8;
            SetPrgPages(8, address, c.Mem, (int)(bank << 13), c.Ram);
        }
        else
        {
            uint va = bank << 2;

            for (int x = 0; x < 4; x++)
                SetPrg2r(chip, address + (uint)(x << 11), va + (uint)x);
        }
    }

    public void SetPrg16r(int chip, uint address, uint bank)
    {
        ChipInfo c = GetPrgChip(chip);

        if (c == null)
        {
            for (int x = 0; x < 8; x++)
                SetPrgPages(2, address + (uint)(x << 11), null, 0, false);

            return;
        }

        if (c.Size >= 16384)
        {
            bank &= c.Mask16;
            SetPrgPages(16, address, c.Mem, (int)(bank << 14), c.Ram);
        }
        else
        {
            uint va = bank << 3;

            for (int x = 0; x < 8; x++)
                SetPrg2r(chip, address + (uint)(x << 11), va + (uint)x);
        }
    }

    public void SetPrg32r(int chip, uint address, uint bank)
    {
        ChipInfo c = GetPrgChip(chip);

        if (c == null)
        {
            for (int x = 0; x < 16; x++)
                SetPrgPages(2, address + (uint)(x << 11), null, 0, false);

            return;
        }

        if (c.Size >= 32768)
        {
            bank &= c.Mask32;
            SetPrgPages(32, address, c.Mem, (int)(bank << 15), c.Ram);
        }
        else
        {
            uint va = bank << 4;

            for (int x = 0; x < 16; x++)
                SetPrg2r(chip, address + (uint)(x << 11), va + (uint)x);
        }
    }

    public void SetChr1r(int chip, uint address, uint bank)
    {
        ChipInfo c = GetChrChip(chip);

        if (c == null || c.Size < 1024)
            return;

        bank &= c.Mask1;
        SetChrPages(1, address, c.Mem, (int)(bank << 10), c.Ram);
    }

    public void SetChr2r(int chip, uint address, uint bank)
    {
        ChipInfo c = GetChrChip(chip);

        if (c == null || c.Size < 2048)
            return;

        bank &= c.Mask2;
        SetChrPages(2, address, c.Mem, (int)(bank << 11), c.Ram);
    }

    public void SetChr4r(int chip, uint address, uint bank)
    {
        ChipInfo c = GetChrChip(chip);

        if (c == null || c.Size < 4096)
            return;

        bank &= c.Mask4;
        SetChrPages(4, address, c.Mem, (int)(bank << 12), c.Ram);
    }

    public void SetChr8r(int chip, uint bank)
    {
        ChipInfo c = GetChrChip(chip);

        if (c == null)
            return;

        if (c.Size < 8192)
        {
            bank = 0;
        }
        else
        {
            bank &= c.Mask8;
        }

        SetChrPages(8, 0, c.Mem, (int)(bank << 13), c.Ram);
    }

    public byte ReadPrg(ushort address)
    {
        return ReadPrg((int)address);
    }

    public byte ReadPrg(int address)
    {
        int page = (address >> 11) & 31;

        byte[] mem = _prgPageMem[page];
        if (mem == null)
            return 0;

        int index = _prgPageAdjust[page] + address;

        if (index < 0 || index >= mem.Length)
            return 0;

        return mem[index];
    }

    public void WritePrg(ushort address, byte value)
    {
        WritePrg((int)address, value);
    }

    public void WritePrg(int address, byte value)
    {
        int page = (address >> 11) & 31;

        if (!_prgPageIsRam[page])
            return;

        byte[] mem = _prgPageMem[page];
        if (mem == null)
            return;

        int index = _prgPageAdjust[page] + address;

        if (index < 0 || index >= mem.Length)
            return;

        mem[index] = value;
    }

    public byte ReadChr(ushort address)
    {
        return ReadChr((int)address);
    }

    public byte ReadChr(int address)
    {
        int page = (address >> 10) & 7;

        byte[] mem = _chrPageMem[page];
        if (mem == null)
            return 0;

        int index = _chrPageAdjust[page] + address;

        if (index < 0 || index >= mem.Length)
            return 0;

        return mem[index];
    }

    public void WriteChr(ushort address, byte value)
    {
        WriteChr((int)address, value);
    }

    public void WriteChr(int address, byte value)
    {
        int page = (address >> 10) & 7;

        if (!_chrPageIsRam[page])
            return;

        byte[] mem = _chrPageMem[page];
        if (mem == null)
            return;

        int index = _chrPageAdjust[page] + address;

        if (index < 0 || index >= mem.Length)
            return;

        mem[index] = value;
    }

    private ChipInfo GetPrgChip(int chip)
    {
        if ((uint)chip >= 32)
            return null;

        return _prgChips[chip];
    }

    private ChipInfo GetChrChip(int chip)
    {
        if ((uint)chip >= 32)
            return null;

        return _chrChips[chip];
    }

    private void SetPrgPages(int sizeKb, uint address, byte[] mem, int offset, bool ram)
    {
        int basePage = (int)(address >> 11);
        int count = sizeKb >> 1;

        int adjust = mem == null ? 0 : offset - (int)address;

        for (int x = count - 1; x >= 0; x--)
        {
            int page = basePage + x;

            if ((uint)page >= PrgPageCount)
                continue;

            _prgPageMem[page] = mem;
            _prgPageAdjust[page] = adjust;
            _prgPageIsRam[page] = mem != null && ram;
        }
    }

    private void SetChrPages(int sizeKb, uint address, byte[] mem, int offset, bool ram)
    {
        int basePage = (int)(address >> 10);
        int count = sizeKb;

        int adjust = mem == null ? 0 : offset - (int)address;

        for (int x = count - 1; x >= 0; x--)
        {
            int page = basePage + x;

            if ((uint)page >= ChrPageCount)
                continue;

            _chrPageMem[page] = mem;
            _chrPageAdjust[page] = adjust;
            _chrPageIsRam[page] = mem != null && ram;
        }
    }

    private static uint MakeMask(int size, int shift)
    {
        if (size <= 0)
            return 0;

        int pages = size >> shift;

        if (pages <= 0)
            return 0;

        return (uint)(pages - 1);
    }

    private sealed class ChipInfo
    {
        public byte[] Mem = Array.Empty<byte>();
        public int Size;
        public bool Ram;

        public uint Mask1;
        public uint Mask2;
        public uint Mask4;
        public uint Mask8;
        public uint Mask16;
        public uint Mask32;
    }
}