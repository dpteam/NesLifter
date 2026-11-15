using System;
using System.Collections.Generic;
using System.IO;

namespace NesLifter.Common;

/// <summary>
/// Builds small, fully synthetic iNES images so the toolchain can be tested
/// end-to-end without shipping any copyrighted ROM.
///
/// Every image contains the same hand-assembled 6502 program at PRG offset 0,
/// which is mapped to $8000 at power-on for every supported mapper. The
/// program sets up the stack, programs a backdrop colour, and then spins in a
/// loop incrementing a zero-page counter, so a headless run must reach the
/// requested frame count without trapping.
/// </summary>
public static class SyntheticRom
{
    /// <summary>Description of a synthetic test cartridge.</summary>
    public sealed class Spec
    {
        public Spec(string name, int mapper, int prgSize, int chrSize, int mirroring = 1)
        {
            Name = name;
            Mapper = mapper;
            PrgSize = prgSize;
            ChrSize = chrSize;
            Mirroring = mirroring;
        }

        public string Name { get; }
        public int Mapper { get; }
        public int PrgSize { get; }
        public int ChrSize { get; }
        public int Mirroring { get; }

        public string FileName => Name + ".nes";
    }

    /// <summary>The default mapper matrix used by <c>--selftest</c>.</summary>
    public static IReadOnlyList<Spec> DefaultMatrix { get; } = new[]
    {
        new Spec("nrom-32k", 0, 0x8000, 0x2000),
        new Spec("nrom-16k-chrram", 0, 0x4000, 0),
        new Spec("uxrom-128k", 2, 0x20000, 0),
        new Spec("cnrom-32k-chr", 3, 0x8000, 0x8000),
        new Spec("mmc1-256k-chrram", 1, 0x40000, 0),
        new Spec("mmc3-256k-chr", 4, 0x40000, 0x20000),
        new Spec("axrom-128k", 7, 0x20000, 0),
        new Spec("mmc2-128k-chr", 9, 0x20000, 0x20000),
        new Spec("gxrom-32k-chr", 66, 0x20000, 0x2000),
        new Spec("vrc4-128k-chr", 21, 0x20000, 0x20000),
        new Spec("vrc2-128k-chr", 22, 0x20000, 0x20000),
        new Spec("vrc6-128k-chr", 24, 0x20000, 0x20000),
        new Spec("fme7-128k", 69, 0x20000, 0x2000),
        new Spec("camerica-128k", 71, 0x20000, 0),
        new Spec("nina-79-32k-chr", 79, 0x8000, 0x8000),
        new Spec("quattro-256k", 232, 0x40000, 0)
    };

    public static byte[] Build(Spec spec)
    {
        if (spec == null)
            throw new ArgumentNullException(nameof(spec));

        if (spec.PrgSize < 0x4000 || spec.PrgSize % 0x4000 != 0)
            throw new ArgumentException("PRG size must be a multiple of 16 KB.", nameof(spec));

        byte[] rom = new byte[16 + spec.PrgSize + spec.ChrSize];

        // ---- iNES header ----
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = (byte)(spec.PrgSize / 0x4000);
        rom[5] = (byte)(spec.ChrSize / 0x2000);
        rom[6] = (byte)(((spec.Mapper & 0x0F) << 4) | (spec.Mirroring & 0x01));
        rom[7] = (byte)(spec.Mapper & 0xF0);
        rom[8] = 0;
        rom[9] = 0;
        rom[10] = 0;
        rom[11] = 0;
        rom[12] = 0;
        rom[13] = 0;
        rom[14] = 0;
        rom[15] = 0;

        // ---- PRG ROM ----
        byte[] prg = new byte[spec.PrgSize];

        // Unused space is filled with BRK (0x00), which terminates a linear
        // sweep. A NOP or $FF fill would let recursive descent walk the whole
        // cartridge and turn the self-test into a code-size stress test.
        for (int i = 0; i < prg.Length; i++)
            prg[i] = 0x00;

        byte[] program = AssembleProgram();
        Array.Copy(program, 0, prg, 0, program.Length);

        // NMI handler: increment $0001 and return.
        prg[0x0100] = 0xEE;         // INC $0001
        prg[0x0101] = 0x01;
        prg[0x0102] = 0x00;
        prg[0x0103] = 0x40;         // RTI

        // IRQ handler: just return.
        prg[0x0110] = 0x40;         // RTI

        // Vectors are mirrored at the end of every 16 KB bank. Cartridges whose
        // whole window switches (AxROM, Quattro) need a table in bank 0, while
        // mappers with a fixed last bank need it in the final bank. Writing all
        // of them keeps the synthetic image valid for every supported mapper.
        for (int bank = 0; bank < prg.Length / 0x4000; bank++)
        {
            int vectors = (bank + 1) * 0x4000 - 6;
            WriteVector(prg, vectors, 0x8100);      // NMI
            WriteVector(prg, vectors + 2, 0x8000);  // RESET
            WriteVector(prg, vectors + 4, 0x8110);  // IRQ
        }

        Array.Copy(prg, 0, rom, 16, prg.Length);

        // ---- CHR ROM ----
        if (spec.ChrSize > 0)
        {
            int chrBase = 16 + prg.Length;
            int tiles = spec.ChrSize / 16;

            for (int tile = 0; tile < tiles; tile++)
            {
                for (int row = 0; row < 8; row++)
                {
                    // A simple diagonal ramp so the PPU has visible structure.
                    byte low = (byte)(0x80 >> (row & 7));
                    byte high = (byte)((row & 1) != 0 ? 0x18 : 0x00);

                    if (tile % 7 == 0)
                    {
                        low = 0xFF;
                        high = 0x00;
                    }

                    rom[chrBase + tile * 16 + row] = low;
                    rom[chrBase + tile * 16 + row + 8] = high;
                }
            }
        }

        return rom;
    }

    public static string Write(Spec spec, string directory)
    {
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, spec.FileName);
        File.WriteAllBytes(path, Build(spec));
        return path;
    }

    /// <summary>
    /// Hand-assembled program placed at $8000.
    /// </summary>
    private static byte[] AssembleProgram()
    {
        return new byte[]
        {
            0x78,                   // $8000  SEI
            0xD8,                   // $8001  CLD
            0xA2, 0xFF,             // $8002  LDX #$FF
            0x9A,                   // $8004  TXS
            0xA9, 0x00,             // $8005  LDA #$00
            0x8D, 0x00, 0x20,       // $8007  STA $2000   ; PPUCTRL
            0x8D, 0x01, 0x20,       // $800A  STA $2001   ; PPUMASK
            0x8D, 0x15, 0x40,       // $800D  STA $4015   ; APU status
            0xA9, 0x3F,             // $8010  LDA #$3F
            0x8D, 0x06, 0x20,       // $8012  STA $2006
            0xA9, 0x00,             // $8015  LDA #$00
            0x8D, 0x06, 0x20,       // $8017  STA $2006
            0xA9, 0x30,             // $801A  LDA #$30
            0x8D, 0x07, 0x20,       // $801C  STA $2007   ; backdrop
            0xA9, 0x80,             // $801F  LDA #$80
            0x8D, 0x00, 0x20,       // $8021  STA $2000   ; NMI on
            0xA9, 0x1E,             // $8024  LDA #$1E
            0x8D, 0x01, 0x20,       // $8026  STA $2001   ; BG + sprites
            0xEE, 0x00, 0x00,       // $8029  INC $0000
            0x4C, 0x29, 0x80        // $802C  JMP $8029
        };
    }

    private static void WriteVector(byte[] prg, int offset, ushort target)
    {
        prg[offset] = (byte)(target & 0xFF);
        prg[offset + 1] = (byte)(target >> 8);
    }
}
