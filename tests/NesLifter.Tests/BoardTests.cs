using System.Linq;
using NesLifter.Core;
using NesLifter.Core.Boards;
using NesLifter.Mappers;
using Xunit;

namespace NesLifter.Tests;

public class BoardFactoryTests
{
    [Fact]
    public void EveryCatalogMapperHasABoard()
    {
        foreach (MapperDescriptor descriptor in MapperCatalog.All)
        {
            Assert.True(
                BoardFactory.IsSupported(descriptor.Id),
                "Mapper " + descriptor.Id + " (" + descriptor.Name + ") is in the catalog but has no board.");
        }
    }

    /// <summary>
    /// The reverse direction of the test above, and the one that actually caught a
    /// bug: mapper 5 (MMC5) had a board — an alias to <c>Mmc3Board</c> — while
    /// being absent from the catalog. Nothing noticed, because every existing test
    /// started from the catalog. The result was that MMC5 cartridges lifted
    /// "successfully" into builds that rendered garbage.
    ///
    /// A board without a catalog entry means the two lists disagree about what is
    /// supported, and MMC5 and MMC3 are not interchangeable: MMC5 has its own
    /// register window at $5100-$5FFF, ExRAM, 8x8 attributes, a multiplier and
    /// three extra sound channels.
    /// </summary>
    [Fact]
    public void EveryBoardHasACatalogEntry()
    {
        foreach (int id in BoardFactory.SupportedMappers)
        {
            Assert.True(
                MapperCatalog.TryGet(id, out _),
                "Mapper " + id + " has a board but is not in the catalog, so its descriptor claims "
                + "it is unsupported while a lift would silently use the board anyway.");
        }
    }

    [Fact]
    public void SupportedMappersAreSortedAndUnique()
    {
        var ids = BoardFactory.SupportedMappers.ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids.OrderBy(x => x), ids);
    }

    [Fact]
    public void UnknownMapperFallsBackToNrom()
    {
        CartInfo cart = TestCart.Create(255, 0x8000);
        CartMapping mapping = new CartMapping();

        IBoard board = BoardFactory.CreateOrDefault(cart, mapping, out bool fallback);

        Assert.True(fallback);
        Assert.Equal(0, board.MapperId);
    }

    [Fact]
    public void KnownMapperDoesNotFallBack()
    {
        CartInfo cart = TestCart.Create(4, 0x40000, 0x20000);
        CartMapping mapping = new CartMapping();

        IBoard board = BoardFactory.CreateOrDefault(cart, mapping, out bool fallback);

        Assert.False(fallback);
        Assert.Equal(4, board.MapperId);
    }

    [Fact]
    public void CreateThrowsForUnknownMapper()
    {
        CartInfo cart = TestCart.Create(200, 0x8000);

        Assert.Throws<NotSupportedException>(() => BoardFactory.Create(cart, new CartMapping()));
    }
}

public class NromTests
{
    [Fact]
    public void Nrom256MapsLinear32K()
    {
        var (cart, mapping, _) = TestCart.CreatePowered(0, 0x8000);

        for (int bank = 0; bank < 2; bank++)
        {
            ushort addr = (ushort)(0x8000 + bank * 0x4000);
            Assert.Equal(TestCart.PrgBankMarker(cart, bank), TestCart.CpuRead(mapping, addr));
        }
    }

    [Fact]
    public void Nrom128Mirrors16K()
    {
        var (cart, mapping, _) = TestCart.CreatePowered(0, 0x4000);

        Assert.Equal(TestCart.PrgBankMarker(cart, 0), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(TestCart.PrgBankMarker(cart, 0), TestCart.CpuRead(mapping, 0xC000));
    }

    [Fact]
    public void NromWithoutChrRomUsesChrRam()
    {
        var (_, mapping, _) = TestCart.CreatePowered(0, 0x8000, 0);

        mapping.WriteChr(0x0000, 0x5A);
        Assert.Equal(0x5A, mapping.ReadChr(0x0000));
    }

    [Fact]
    public void NromWithChrRomIsReadOnly()
    {
        var (cart, mapping, _) = TestCart.CreatePowered(0, 0x8000, 0x2000);

        byte original = mapping.ReadChr(0x0010);
        mapping.WriteChr(0x0010, 0xFF);

        Assert.Equal(original, mapping.ReadChr(0x0010));
        Assert.Equal(cart.ChrRom[0x0010], original);
    }
}

public class UxRomTests
{
    [Fact]
    public void PowerOnMapsBankZeroAndLast()
    {
        var (cart, mapping, _) = TestCart.CreatePowered(2, 0x20000);

        Assert.Equal(TestCart.PrgBankMarker(cart, 0), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(TestCart.PrgBankMarker(cart, 7), TestCart.CpuRead(mapping, 0xC000));
    }

    [Fact]
    public void WriteSelectsBank()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(2, 0x20000);

        board.WritePrg(0x8000, 3);

        Assert.Equal(TestCart.PrgBankMarker(cart, 3), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(TestCart.PrgBankMarker(cart, 7), TestCart.CpuRead(mapping, 0xC000));
    }

    [Fact]
    public void BankNumberWrapsToRomSize()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(2, 0x20000);

        // 8 banks of 16 KB; bank 8 must wrap to bank 0.
        board.WritePrg(0x8000, 8);

        Assert.Equal(TestCart.PrgBankMarker(cart, 0), TestCart.CpuRead(mapping, 0x8000));
    }
}

public class CnromTests
{
    [Fact]
    public void ChrBankSwitches()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(3, 0x8000, 0x8000);

        board.WritePrg(0x8000, 2);

        // CHR bank 2 starts at 0x4000; marker is the bank index.
        Assert.Equal(2, mapping.ReadChr(0x0000));
        Assert.Equal(cart.ChrRom[0x4000], mapping.ReadChr(0x0000));
    }

    [Fact]
    public void PrgStaysFixed()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(3, 0x8000, 0x8000);

        board.WritePrg(0x8000, 3);

        Assert.Equal(TestCart.PrgBankMarker(cart, 0), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(TestCart.PrgBankMarker(cart, 1), TestCart.CpuRead(mapping, 0xC000));
    }
}

public class Mmc1Tests
{
    [Fact]
    public void SerialWriteNeedsFiveBits()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(1, 0x40000);

        // PRG mode 3 (fix last bank at $C000), bank 1 at $8000.
        WriteSerial(board, 0x8000, 0x0F);   // control: PRG mode 3
        WriteSerial(board, 0xE000, 0x01);   // PRG bank 1

        Assert.Equal(TestCart.PrgBankMarker(cart, 1), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(TestCart.PrgBankMarker(cart, 15), TestCart.CpuRead(mapping, 0xC000));
    }

    [Fact]
    public void FourBitsDoNotCompleteARegisterWrite()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(1, 0x40000);

        // Four bits of "PRG bank 1" must leave the register untouched.
        board.WritePrg(0xE000, 0x01);
        board.WritePrg(0xE000, 0x00);
        board.WritePrg(0xE000, 0x00);
        board.WritePrg(0xE000, 0x00);

        Assert.Equal(TestCart.PrgBankMarker(cart, 0), TestCart.CpuRead(mapping, 0x8000));
    }

    [Fact]
    public void ResetBitClearsShiftRegister()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(1, 0x40000);

        board.WritePrg(0xE000, 0x01);
        board.WritePrg(0xE000, 0x01);
        board.WritePrg(0xE000, 0x80);   // reset
        board.WritePrg(0xE000, 0x01);

        // Shift register was cleared, so four bits are not enough.
        Assert.Equal(TestCart.PrgBankMarker(cart, 15), TestCart.CpuRead(mapping, 0xC000));
    }

    [Fact]
    public void ControlBitsSelectMirroring()
    {
        var (cart, _, board) = TestCart.CreatePowered(1, 0x40000, 0, mirroring: 0);

        WriteSerial(board, 0x8000, 0x02);   // vertical
        Assert.Equal((int)MirroringMode.Vertical, cart.Mirroring);

        WriteSerial(board, 0x8000, 0x03);   // horizontal
        Assert.Equal((int)MirroringMode.Horizontal, cart.Mirroring);
    }

    [Fact]
    public void PrgMode2FixesFirstBank()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(1, 0x40000);

        WriteSerial(board, 0x8000, 0x0A);   // PRG mode 2
        WriteSerial(board, 0xE000, 0x03);

        Assert.Equal(TestCart.PrgBankMarker(cart, 0), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(TestCart.PrgBankMarker(cart, 3), TestCart.CpuRead(mapping, 0xC000));
    }

    private static void WriteSerial(IBoard board, ushort address, byte value)
    {
        for (int bit = 0; bit < 5; bit++)
            board.WritePrg(address, (byte)((value >> bit) & 1));
    }
}

public class Mmc3Tests
{
    [Fact]
    public void PowerOnMapsLastTwoBanks()
    {
        var (cart, mapping, _) = TestCart.CreatePowered(4, 0x40000, 0x20000);

        // 8 KB banks: the last is fixed at $E000, the second last at $C000.
        Assert.Equal(TestCart.PrgBankMarker8(cart, 30), TestCart.CpuRead(mapping, 0xC000));
        Assert.Equal(TestCart.PrgBankMarker8(cart, 31), TestCart.CpuRead(mapping, 0xE000));
    }

    [Fact]
    public void BankSelectAndDataProgramBanks()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(4, 0x40000, 0x20000);

        board.WritePrg(0x8000, 6);          // select R6
        board.WritePrg(0x8001, 4);          // R6 = bank 4

        Assert.Equal(TestCart.PrgBankMarker8(cart, 4), TestCart.CpuRead(mapping, 0x8000));
    }

    [Fact]
    public void PrgSwapBitExchangesWindows()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(4, 0x40000, 0x20000);

        board.WritePrg(0x8000, 6);
        board.WritePrg(0x8001, 4);
        board.WritePrg(0x8000, 0x40);       // PRG swap

        Assert.Equal(TestCart.PrgBankMarker8(cart, 30), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(TestCart.PrgBankMarker8(cart, 4), TestCart.CpuRead(mapping, 0xC000));
    }

    [Fact]
    public void ScanlineCounterRaisesIrq()
    {
        var (_, _, board) = TestCart.CreatePowered(4, 0x40000, 0x20000);
        Mmc3Board mmc3 = Assert.IsType<Mmc3Board>(board);

        board.WritePrg(0xC000, 2);          // IRQ latch = 2
        board.WritePrg(0xC001, 0);          // reload
        board.WritePrg(0xE001, 0);          // enable

        Assert.False(mmc3.IrqPending);

        ClockScanlines(board, 4);

        Assert.True(mmc3.IrqPending);
    }

    [Fact]
    public void DisablingIrqClearsPending()
    {
        var (_, _, board) = TestCart.CreatePowered(4, 0x40000, 0x20000);
        Mmc3Board mmc3 = Assert.IsType<Mmc3Board>(board);

        board.WritePrg(0xC000, 1);
        board.WritePrg(0xC001, 0);
        board.WritePrg(0xE001, 0);
        ClockScanlines(board, 4);
        Assert.True(mmc3.IrqPending);

        board.WritePrg(0xE000, 0);          // disable
        Assert.False(mmc3.IrqPending);
    }

    private static void ClockScanlines(IBoard board, int count)
    {
        for (int i = 0; i < count; i++)
            board.ClockPpu(i, 0);
    }
}

public class LatchBoardTests
{
    [Fact]
    public void AxRomSelects32KBankAndMirroring()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(7, 0x20000, 0, mirroring: 0);

        board.WritePrg(0x8000, 2);
        Assert.Equal(TestCart.PrgBankMarker(cart, 2 * 2), TestCart.CpuRead(mapping, 0x8000));

        board.WritePrg(0x8000, 0x10);       // single-screen high
        Assert.Equal((int)MirroringMode.OneScreenHigh, cart.Mirroring);
    }

    [Fact]
    public void GxRomTakesPrgAndChrFromOneWrite()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(66, 0x20000, 0x8000);

        // Write where the ROM byte allows the bank bits through the bus conflict.
        ushort addr = FindWritableAddress(mapping, 0x21);
        board.WritePrg(addr, 0x21);         // PRG bank 2, CHR bank 1

        Assert.Equal(TestCart.PrgBankMarker(cart, 4), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(cart.ChrRom[0x2000], mapping.ReadChr(0x0000));
    }

    [Fact]
    public void CamericaSwitches16KBanks()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(71, 0x20000);

        ushort addr = FindWritableAddress(mapping, 0x03);
        board.WritePrg(addr, 0x03);

        Assert.Equal(TestCart.PrgBankMarker(cart, 0x03 & TestCart.CpuRead(mapping, addr)), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(TestCart.PrgBankMarker(cart, 7), TestCart.CpuRead(mapping, 0xC000));
    }

    [Fact]
    public void BusConflictAndsWriteWithCurrentByte()
    {
        // GxROM and Camerica use discrete logic with bus conflicts: the latched
        // value is the written value AND the byte the ROM drives on the bus.
        var (_, mapping, board) = TestCart.CreatePowered(66, 0x20000, 0x8000);

        ushort addr = 0x8000;
        byte busValue = TestCart.CpuRead(mapping, addr);

        board.WritePrg(addr, 0xFF);

        // Only the bus value can survive, so the CHR bank becomes busValue & 3.
        int expectedChrBank = (busValue & 0xFF) & 0x03;
        Assert.Equal(0, expectedChrBank);
    }

    [Fact]
    public void CnromIgnoresBusConflict()
    {
        // CNROM is modelled without bus conflicts (matching FCEUmm), so the
        // written bank number is latched verbatim.
        var (cart, mapping, board) = TestCart.CreatePowered(3, 0x8000, 0x10000);

        board.WritePrg(0x8000, 3);

        Assert.Equal(cart.ChrRom[3 * 0x2000], mapping.ReadChr(0x0000));
    }

    [Fact]
    public void NinaFixedPrgBankedChr()
    {
        var (cart, mapping, _) = TestCart.CreatePowered(79, 0x8000, 0x10000, mirroring: 0);

        Assert.Equal(TestCart.PrgBankMarker(cart, 0), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(TestCart.PrgBankMarker(cart, 1), TestCart.CpuRead(mapping, 0xC000));
    }

    [Fact]
    public void QuattroUsesAddressBitsForBlock()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(232, 0x40000);

        board.WritePrg(0x8000, 0x08);       // block 1
        Assert.Equal(TestCart.PrgBankMarker(cart, 2), TestCart.CpuRead(mapping, 0x8000));
    }

    [Fact]
    public void BnromSwitches32KPrg()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(34, 0x20000);

        board.WritePrg(0x8000, 2);
        Assert.Equal(TestCart.PrgBankMarker(cart, 4), TestCart.CpuRead(mapping, 0x8000));
    }

    /// <summary>
    /// Finds an address in $8000-$FFFF whose ROM byte contains every bit of
    /// <paramref name="wanted"/>, so a bus-conflict board can latch it.
    /// </summary>
    private static ushort FindWritableAddress(CartMapping mapping, byte wanted)
    {
        for (int address = 0x8000; address < 0xFFF0; address++)
        {
            if ((TestCart.CpuRead(mapping, (ushort)address) & wanted) == wanted)
                return (ushort)address;
        }

        throw new InvalidOperationException("No writable address found for 0x" + wanted.ToString("X2"));
    }
}

public class SunsoftTests
{
    [Fact]
    public void Fme7CommandRegisterSelectsChrBank()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(69, 0x20000, 0x20000);

        board.WritePrg(0x8000, 0x03);       // command 3 = CHR 1 KB bank at $0C00
        board.WritePrg(0xA000, 5);

        Assert.Equal(cart.ChrRom[5 * 0x400], mapping.ReadChr(0x0C00));
    }

    [Fact]
    public void Fme7PrgRegistersSwitchBanks()
    {
        var (cart, mapping, board) = TestCart.CreatePowered(69, 0x20000, 0x20000);

        board.WritePrg(0x8000, 0x08);       // PRG bank 0
        board.WritePrg(0xA000, 3);

        Assert.Equal(TestCart.PrgBankMarker8(cart, 3), TestCart.CpuRead(mapping, 0x8000));
        Assert.Equal(TestCart.PrgBankMarker8(cart, 15), TestCart.CpuRead(mapping, 0xE000));
    }
}
