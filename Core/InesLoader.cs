using System;
using System.IO;

namespace NesLifter.Core;

/// <summary>
/// Raw iNES / NES 2.0 header.
/// </summary>
public sealed class InesHeader
{
    public byte[] Id = new byte[4];

    public byte PrgRomBanks;
    public byte ChrRomBanks;

    public byte Flags6;
    public byte Flags7;
    public byte RomType3;

    public byte UpperPrgChrSize;
    public byte PrgRamSize;
    public byte ChrRamSize;

    public byte Region;
    public byte VsHardware;
    public byte MiscRoms;
    public byte ExpDevice;
}

/// <summary>
/// iNES / iNES 2.0 loader.
/// Сделан по мотивам ines.c из fceumm, но без лишней эмуляторной обвязки.
/// </summary>
public static class InesLoader
{
    private const int HeaderSize = 16;
    private const int TrainerSize = 512;

    // Практический потолок, чтобы не пытаться allocating гигабайты из битого header.
    private const int MaxRomSize = 0x40000000; // 1 GiB

    public static CartInfo Load(string path)
    {
        using FileStream fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        if (fs.Length < HeaderSize)
            throw new InvalidDataException("File is smaller than iNES header.");

        byte[] h = new byte[HeaderSize];
        ReadExactOrThrow(fs, h, HeaderSize);

        if (h[0] != 0x4E || h[1] != 0x45 || h[2] != 0x53 || h[3] != 0x1A)
            throw new InvalidDataException("Missing iNES signature (NES\\x1A).");

        SanitizeHeader(h);

        CartInfo cart = new CartInfo();

        cart.TotalFileSize = (ulong)fs.Length;
        cart.Flags6 = h[6];
        cart.Flags7 = h[7];

        bool ines2 = (h[7] & 0x0C) == 0x08;
        cart.INes2 = ines2;

        cart.Mapper = GetMapperId(h);
        cart.Mirroring = (h[6] & 0x08) != 0 ? 2 : (h[6] & 0x01);
        cart.Battery = (h[6] & 0x02) != 0;
        cart.TrainerPresent = (h[6] & 0x04) != 0;

        int prgBankCount = h[4];
        int chrBankCount = h[5];

        if (ines2)
        {
            prgBankCount |= (h[9] & 0x0F) << 8;
            chrBankCount |= ((h[9] >> 4) & 0x0F) << 8;

            cart.Submapper = (h[8] >> 4) & 0x0F;
            cart.Region = h[12] & 0x03;

            if ((h[10] & 0x0F) != 0)
                cart.PrgRamSize = 64 << (h[10] & 0x0F);

            if ((h[10] & 0xF0) != 0)
                cart.PrgRamSaveSize = 64 << ((h[10] >> 4) & 0x0F);

            if ((h[11] & 0x0F) != 0)
                cart.ChrRamSize = 64 << (h[11] & 0x0F);

            if ((h[11] & 0xF0) != 0)
                cart.ChrRamSaveSize = 64 << ((h[11] >> 4) & 0x0F);

            cart.MiscRomNumber = h[14];
        }
        else
        {
            cart.Submapper = 0;
            cart.Region = 0;
            cart.PrgRamSize = 0;
            cart.PrgRamSaveSize = 0;
            cart.ChrRamSize = 0;
            cart.ChrRamSaveSize = 0;
            cart.MiscRomNumber = 0;
        }

        int prgSize;
        int chrSize;

        if (ines2 && prgBankCount >= 0xF00)
            prgSize = DecodeNes2ExponentSize(h[4]);
        else
            prgSize = prgBankCount * 0x4000;

        if (ines2 && chrBankCount >= 0xF00)
            chrSize = DecodeNes2ExponentSize(h[5]);
        else
            chrSize = chrBankCount * 0x2000;

        if (prgSize <= 0)
            throw new InvalidDataException("Header reports zero PRG ROM size.");

        if (prgSize > MaxRomSize)
            prgSize = MaxRomSize;

        if (chrSize > MaxRomSize)
            chrSize = MaxRomSize;

        cart.PrgRomSize = prgSize;
        cart.ChrRomSize = chrSize;

        if (cart.TrainerPresent)
        {
            cart.Trainer = ReadBlockFillFF(fs, TrainerSize);
        }
        else
        {
            cart.Trainer = Array.Empty<byte>();
        }

        cart.PrgRom = ReadBlockFillFF(fs, prgSize);

        if (chrSize > 0)
            cart.ChrRom = ReadBlockFillFF(fs, chrSize);
        else
            cart.ChrRom = Array.Empty<byte>();

        if (cart.INes2 && cart.MiscRomNumber > 0)
        {
            long miscSize =
                (long)fs.Length
                - HeaderSize
                - (cart.TrainerPresent ? TrainerSize : 0)
                - cart.PrgRomSize
                - cart.ChrRomSize;

            if (miscSize > 0 && miscSize <= 0x8000000)
            {
                cart.MiscRomSize = (int)miscSize;
                cart.MiscRom = ReadBlockFillFF(fs, cart.MiscRomSize);
            }
            else
            {
                cart.MiscRomSize = 0;
                cart.MiscRom = Array.Empty<byte>();
            }
        }
        else
        {
            cart.MiscRomSize = 0;
            cart.MiscRom = Array.Empty<byte>();
        }

        cart.PrgCrc32 = Crc32.Compute(0, cart.PrgRom);
        cart.ChrCrc32 = Crc32.Compute(0, cart.ChrRom);
        cart.Crc32 = Crc32.Compute(cart.PrgCrc32, cart.ChrRom);

        return cart;
    }

    private static int GetMapperId(byte[] h)
    {
        byte romType = h[6];
        byte romType2 = h[7];
        byte romType3 = h[8];

        switch (romType2 & 0x0C)
        {
            case 0x08:
                // NES 2.0
                return ((romType3 << 8) & 0xF00) | (romType2 & 0xF0) | (romType >> 4);

            case 0x00:
                // iNES
                return (romType2 & 0xF0) | (romType >> 4);

            default:
                // Archaic iNES
                return romType >> 4;
        }
    }

    private static int DecodeNes2ExponentSize(byte lowByte)
    {
        int exponent = lowByte >> 2;
        int multiplier = ((lowByte & 3) * 2) + 1;

        if (exponent > 30)
            exponent = 30;

        long size = (1L << exponent) * multiplier;

        if (size > MaxRomSize)
            size = MaxRomSize;

        return (int)size;
    }

    private static void SanitizeHeader(byte[] h)
    {
        // Известные старые хреновые заголовки из fceumm:
        // "DiskDude", "demiforce", "Ni03".
        if (AsciiEquals(h, 7, "DiskDude"))
            Array.Clear(h, 7, 9);

        if (AsciiEquals(h, 7, "demiforce"))
            Array.Clear(h, 7, 9);

        if (AsciiEquals(h, 10, "Ni03"))
        {
            if (AsciiEquals(h, 7, "Dis"))
                Array.Clear(h, 7, 9);
            else
                Array.Clear(h, 10, 6);
        }
    }

    private static bool AsciiEquals(byte[] data, int offset, string text)
    {
        if (offset < 0 || offset + text.Length > data.Length)
            return false;

        for (int i = 0; i < text.Length; i++)
        {
            if (data[offset + i] != (byte)text[i])
                return false;
        }

        return true;
    }

    private static void ReadExactOrThrow(Stream stream, byte[] buffer, int count)
    {
        int total = 0;

        while (total < count)
        {
            int n = stream.Read(buffer, total, count - total);
            if (n <= 0)
                throw new EndOfStreamException("Unexpected end of ROM file.");

            total += n;
        }
    }

    private static byte[] ReadBlockFillFF(Stream stream, int size)
    {
        if (size <= 0)
            return Array.Empty<byte>();

        byte[] data = new byte[size];

        int total = 0;

        while (total < size)
        {
            int n = stream.Read(data, total, size - total);
            if (n <= 0)
                break;

            total += n;
        }

        if (total < size)
        {
            for (int i = total; i < size; i++)
                data[i] = 0xFF;
        }

        return data;
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];

            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;

                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320u;
                    else
                        crc >>= 1;
                }

                table[i] = crc;
            }

            return table;
        }

        public static uint Compute(uint seed, byte[] data)
        {
            if (data == null || data.Length == 0)
                return 0;

            uint crc = seed ^ 0xFFFFFFFFu;

            for (int i = 0; i < data.Length; i++)
            {
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }
    }
}