namespace NesLifter.Core;

/// <summary>
/// fceumm-подобные значения mirroring.
/// </summary>
public enum MirroringMode
{
    Horizontal = 0,
    Vertical = 1,
    FourScreen = 2,

    // Эти значения используются в базах/корректорах,
    // как в fceumm: 0x10 / 0x11.
    OneScreenLow = 0x10,
    OneScreenHigh = 0x11
}

/// <summary>
/// Информация о картридже.
/// Максимально близко к смыслу CartInfo из fceumm,
/// но без C-указателей и лишних C-деталей.
/// </summary>
public sealed class CartInfo
{
    /// <summary>
    /// true, если это iNES 2.0 header.
    /// </summary>
    public bool INes2;

    /// <summary>
    /// Mapper number.
    /// </summary>
    public int Mapper;

    /// <summary>
    /// Submapper number, актуально для iNES 2.0.
    /// </summary>
    public int Submapper;

    /// <summary>
    /// Mirroring в стиле fceumm:
    /// 0 = Horizontal
    /// 1 = Vertical
    /// 2 = FourScreen
    /// 0x10 / 0x11 = one-screen variants
    /// </summary>
    public int Mirroring;

    /// <summary>
    /// Battery-backed save present.
    /// </summary>
    public bool Battery;

    /// <summary>
    /// Trainer present in file.
    /// </summary>
    public bool TrainerPresent;

    /// <summary>
    /// Region / TV system:
    /// 0 = NTSC
    /// 1 = PAL
    /// 2 = Multi
    /// 3 = Dendy
    /// </summary>
    public int Region;

    /// <summary>
    /// Raw iNES flags, useful for debug and old compatibility.
    /// </summary>
    public byte Flags6;
    public byte Flags7;

    /// <summary>
    /// PRG ROM data.
    /// </summary>
    public byte[] PrgRom = System.Array.Empty<byte>();

    /// <summary>
    /// CHR ROM data. Empty if game uses CHR RAM.
    /// </summary>
    public byte[] ChrRom = System.Array.Empty<byte>();

    /// <summary>
    /// Trainer data, 512 bytes if present.
    /// </summary>
    public byte[] Trainer = System.Array.Empty<byte>();

    /// <summary>
    /// Misc ROM data, iNES 2.0 specific.
    /// </summary>
    public byte[] MiscRom = System.Array.Empty<byte>();

    /// <summary>
    /// Real PRG ROM size in bytes.
    /// </summary>
    public int PrgRomSize;

    /// <summary>
    /// Real CHR ROM size in bytes.
    /// </summary>
    public int ChrRomSize;

    /// <summary>
    /// PRG RAM size in bytes, volatile part.
    /// </summary>
    public int PrgRamSize;

    /// <summary>
    /// PRG RAM save size in bytes, battery-backed part.
    /// </summary>
    public int PrgRamSaveSize;

    /// <summary>
    /// CHR RAM size in bytes, volatile part.
    /// </summary>
    public int ChrRamSize;

    /// <summary>
    /// CHR RAM save size in bytes, battery-backed part.
    /// </summary>
    public int ChrRamSaveSize;

    /// <summary>
    /// Misc ROM size in bytes.
    /// </summary>
    public int MiscRomSize;

    /// <summary>
    /// Misc ROM count from iNES 2.0.
    /// </summary>
    public int MiscRomNumber;

    /// <summary>
    /// Full input file size.
    /// </summary>
    public ulong TotalFileSize;

    /// <summary>
    /// CRC32 of PRG ROM.
    /// </summary>
    public uint PrgCrc32;

    /// <summary>
    /// CRC32 of CHR ROM.
    /// </summary>
    public uint ChrCrc32;

    /// <summary>
    /// Combined PRG+CHR CRC32.
    /// </summary>
    public uint Crc32;

    /// <summary>
    /// MD5 placeholder.
    /// Может быть заполнено позже, сейчас не критично.
    /// </summary>
    public byte[] Md5 = new byte[16];

    /// <summary>
    /// Аналог CartInfo_PRGRAM_bytes из cart.h.
    /// </summary>
    public int GetPrgRamBytes(int defaultBytes)
    {
        if (INes2)
            return PrgRamSize + PrgRamSaveSize;

        return defaultBytes;
    }

    /// <summary>
    /// Аналог CartInfo_CHRRAM_bytes из cart.h.
    /// </summary>
    public int GetChrRamBytes(int defaultBytes)
    {
        if (INes2)
            return ChrRamSize + ChrRamSaveSize;

        return defaultBytes;
    }
}