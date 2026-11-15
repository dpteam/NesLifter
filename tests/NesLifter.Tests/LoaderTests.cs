using System;
using System.IO;
using NesLifter.Common;
using NesLifter.Core;
using Xunit;

namespace NesLifter.Tests;

public class InesLoaderTests
{
    private static string WriteTemp(byte[] rom)
    {
        string dir = Path.Combine(Path.GetTempPath(), "neslifter-tests");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".nes");
        File.WriteAllBytes(path, rom);
        return path;
    }

    [Fact]
    public void LoadsSyntheticNrom()
    {
        SyntheticRom.Spec spec = new SyntheticRom.Spec("nrom", 0, 0x8000, 0x2000);
        string path = WriteTemp(SyntheticRom.Build(spec));

        try
        {
            CartInfo cart = InesLoader.Load(path);

            Assert.Equal(0, cart.Mapper);
            Assert.Equal(0x8000, cart.PrgRomSize);
            Assert.Equal(0x2000, cart.ChrRomSize);
            Assert.False(cart.INes2);
            Assert.Equal(1, cart.Mirroring);
            Assert.Equal(0x8000, cart.PrgRom.Length);
            Assert.Equal(0x2000, cart.ChrRom.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(1, 0x40000)]
    [InlineData(4, 0x40000)]
    [InlineData(69, 0x20000)]
    [InlineData(232, 0x40000)]
    public void LoadsHighMapperNumbers(int mapper, int prgSize)
    {
        SyntheticRom.Spec spec = new SyntheticRom.Spec("m" + mapper, mapper, prgSize, 0);
        string path = WriteTemp(SyntheticRom.Build(spec));

        try
        {
            CartInfo cart = InesLoader.Load(path);

            Assert.Equal(mapper, cart.Mapper);
            Assert.Equal(prgSize, cart.PrgRomSize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsMissingSignature()
    {
        byte[] rom = new byte[64];

        Assert.Throws<InvalidDataException>(() =>
        {
            string path = WriteTemp(rom);
            try
            {
                InesLoader.Load(path);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void RejectsTruncatedHeader()
    {
        Assert.Throws<InvalidDataException>(() =>
        {
            string path = WriteTemp(new byte[8]);
            try
            {
                InesLoader.Load(path);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void ComputesStableCrc32()
    {
        SyntheticRom.Spec spec = new SyntheticRom.Spec("crc", 0, 0x4000, 0);
        string path = WriteTemp(SyntheticRom.Build(spec));

        try
        {
            CartInfo first = InesLoader.Load(path);
            CartInfo second = InesLoader.Load(path);

            Assert.Equal(first.PrgCrc32, second.PrgCrc32);
            Assert.NotEqual(0u, first.PrgCrc32);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TruncatedPrgIsPaddedWithFF()
    {
        byte[] rom = new byte[16 + 0x100];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 1;   // one 16 KB PRG bank declared

        string path = WriteTemp(rom);

        try
        {
            CartInfo cart = InesLoader.Load(path);

            Assert.Equal(0x4000, cart.PrgRom.Length);
            Assert.Equal(0xFF, cart.PrgRom[cart.PrgRom.Length - 1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class SyntheticRomTests
{
    [Fact]
    public void VectorsPointInsidePrg()
    {
        foreach (SyntheticRom.Spec spec in SyntheticRom.DefaultMatrix)
        {
            byte[] rom = SyntheticRom.Build(spec);
            Assert.True(rom.Length > 16, spec.Name);

            CartInfo cart = new CartInfo
            {
                Mapper = spec.Mapper,
                PrgRom = new byte[spec.PrgSize],
                PrgRomSize = spec.PrgSize,
                ChrRomSize = spec.ChrSize
            };

            Array.Copy(rom, 16, cart.PrgRom, 0, spec.PrgSize);

            NesRom nes = NesRomFrom(cart);
            ushort reset = nes.ReadVector(0xFFFC);

            Assert.InRange(reset, 0x8000, 0xFFF9);
        }
    }

    [Fact]
    public void RejectsInvalidPrgSize()
    {
        Assert.Throws<ArgumentException>(() =>
            SyntheticRom.Build(new SyntheticRom.Spec("bad", 0, 0x2000, 0)));
    }

    private static NesRom NesRomFrom(CartInfo cart)
    {
        // NesRom is a thin wrapper with a private constructor; go through the
        // loader to keep the test honest about the real path.
        string dir = Path.Combine(Path.GetTempPath(), "neslifter-tests");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".nes");

        byte[] rom = new byte[16 + cart.PrgRom.Length];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = (byte)(cart.PrgRom.Length / 0x4000);
        rom[6] = (byte)(((cart.Mapper & 0x0F) << 4) | 1);
        rom[7] = (byte)(cart.Mapper & 0xF0);
        Array.Copy(cart.PrgRom, 0, rom, 16, cart.PrgRom.Length);
        File.WriteAllBytes(path, rom);

        try
        {
            return NesRom.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class DisassemblerTests
{
    [Fact]
    public void FollowsResetVectorAndFindsBasicBlock()
    {
        SyntheticRom.Spec spec = new SyntheticRom.Spec("dis", 0, 0x8000, 0);
        string path = WriteTestRom(spec);

        try
        {
            NesRom rom = NesRom.Load(path);
            Disassembler dis = new Disassembler(rom);
            AnalysisResult result = dis.Analyze();

            Assert.Equal(0x8000, result.Entry);
            Assert.True(result.Instructions.Count > 0, "expected instructions");

            // Blocks are keyed by PRG offset: $8000 is offset 0 on NROM.
            Assert.Equal(0, result.EntryOffset);
            Assert.Contains(0, result.Instructions.Keys);

            // The NMI handler sits at $8100, which is offset 0x100.
            Assert.Contains(0x100, result.Functions);
            Assert.Empty(result.UnknownOpcodes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ForcedAddressesAreDisassembled()
    {
        SyntheticRom.Spec spec = new SyntheticRom.Spec("forced", 0, 0x8000, 0);
        string path = WriteTestRom(spec);

        try
        {
            NesRom rom = NesRom.Load(path);
            Disassembler dis = new Disassembler(rom);
            dis.ForcedAddresses.Add(0x9000);

            AnalysisResult result = dis.Analyze();

            // $9000 is PRG offset 0x1000 on a 32 KB NROM image.
            Assert.Contains(0x1000, result.Instructions.Keys);
            Assert.Contains((ushort)0x9000, result.DynamicTargets);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnalysesEveryBankOfABankedCartridge()
    {
        // UxROM with 128 KB PRG: 8 switchable 16 KB banks. The analyser must
        // discover code in banks the power-on layout does not map at $8000.
        SyntheticRom.Spec spec = new SyntheticRom.Spec("banks", 2, 0x20000, 0);
        string path = WriteTestRom(spec);

        try
        {
            NesRom rom = NesRom.Load(path);

            Disassembler powerOnOnly = new Disassembler(rom) { AnalyseAllBanks = false };
            AnalysisResult single = powerOnOnly.Analyze();

            Disassembler allBanks = new Disassembler(rom) { AnalyseAllBanks = true };
            AnalysisResult full = allBanks.Analyze();

            Assert.True(
                full.Instructions.Count >= single.Instructions.Count,
                "bank-aware analysis must not lose instructions");

            Assert.True(full.BankViews > 1, "expected more than one bank view, got " + full.BankViews);

            // Every decoded offset must land inside the PRG image.
            foreach (int offset in full.Instructions.Keys)
                Assert.InRange(offset, 0, rom.PrgRom.Length - 1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EveryInstructionStaysInsidePrg()
    {
        SyntheticRom.Spec spec = new SyntheticRom.Spec("bounds", 2, 0x20000, 0);
        string path = WriteTestRom(spec);

        try
        {
            NesRom rom = NesRom.Load(path);
            AnalysisResult result = new Disassembler(rom).Analyze();

            foreach (Instruction inst in result.Instructions.Values)
            {
                Assert.InRange(inst.Address, (ushort)0x8000, (ushort)0xFFF9);
                Assert.InRange(inst.Length, 1, 3);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTestRom(SyntheticRom.Spec spec)
    {
        string dir = Path.Combine(Path.GetTempPath(), "neslifter-tests");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".nes");
        File.WriteAllBytes(path, SyntheticRom.Build(spec));
        return path;
    }
}

public class ProgramImageTests
{
    [Fact]
    public void MapperAwareImageMatchesBoardLayout()
    {
        SyntheticRom.Spec spec = new SyntheticRom.Spec("img", 2, 0x20000, 0);
        string path = Path.Combine(Path.GetTempPath(), "neslifter-tests", Guid.NewGuid().ToString("N") + ".nes");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, SyntheticRom.Build(spec));

        try
        {
            NesRom rom = NesRom.Load(path);
            ProgramImage image = ProgramImage.Create(rom, out _, out bool fallback);

            Assert.False(fallback);
            Assert.True(image.MapperAware);

            // UxROM: bank 0 at $8000, last bank at $C000.
            int bank0Offset = image.ToOffset(0x8000);
            int lastOffset = image.ToOffset(0xC000);

            Assert.True(bank0Offset >= 0);
            Assert.True(lastOffset >= 0);
            Assert.NotEqual(bank0Offset, lastOffset);

            // UxROM powers on with bank 0 at $8000 and the last bank at $C000.
            Assert.Equal(0, bank0Offset);
            Assert.Equal(rom.PrgRom.Length - 0x4000, lastOffset);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RamAndIoAreNotCodeSpace()
    {
        SyntheticRom.Spec spec = new SyntheticRom.Spec("ram", 0, 0x8000, 0);
        string path = Path.Combine(Path.GetTempPath(), "neslifter-tests", Guid.NewGuid().ToString("N") + ".nes");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, SyntheticRom.Build(spec));

        try
        {
            NesRom rom = NesRom.Load(path);
            ProgramImage image = ProgramImage.Create(rom, out _, out _);

            Assert.False(image.IsCodeSpace(0x0000));
            Assert.False(image.IsCodeSpace(0x6000));
            Assert.False(image.IsCodeSpace(0x2000));
            Assert.True(image.IsCodeSpace(0x8000));
            Assert.True(image.IsCodeSpace(0xC000));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
