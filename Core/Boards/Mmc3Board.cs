namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Mapper 4 / MMC3.
    /// 8 KB PRG and 1-2 KB CHR banking, four mirroring modes and a
    /// scanline counter that drives the CPU IRQ line.
    /// Reference: NESdev "MMC3", FCEUmm asic_mmc3.c.
    /// </summary>
    public sealed class Mmc3Board : BoardBase
    {
        private readonly byte[] _regs = new byte[8];

        private byte _bankSelect;
        private byte _prgRamProtect;

        private int _irqLatch;
        private int _irqCounter;
        private bool _irqReload;
        private bool _irqEnabled;
        private bool _irqPending;

        public Mmc3Board(CartInfo cart, CartMapping mapping)
            : base(cart, mapping)
        {
        }

        public override int MapperId => 4;

        /// <summary>MMC3 IRQ is asserted to the CPU until acknowledged.</summary>
        public bool IrqPending => _irqPending;

        public override void Power()
        {
            base.Power();

            for (int i = 0; i < _regs.Length; i++)
                _regs[i] = 0;

            _bankSelect = 0;
            _prgRamProtect = 0;
            _irqLatch = 0;
            _irqCounter = 0;
            _irqReload = false;
            _irqEnabled = false;
            _irqPending = false;

            Sync();
        }

        public override void Reset()
        {
            _irqPending = false;
            _irqCounter = 0;
            _irqReload = false;
            Sync();
        }

        public override void WritePrg(ushort address, byte value)
        {
            bool odd = (address & 1) != 0;

            switch (address & 0xE000)
            {
                case 0x8000:
                    if (!odd)
                        _bankSelect = value;
                    else
                        _regs[_bankSelect & 7] = value;
                    Sync();
                    break;

                case 0xA000:
                    if (!odd)
                        SyncMirroring(value);
                    else
                        _prgRamProtect = value;
                    break;

                case 0xC000:
                    if (!odd)
                        _irqLatch = value;
                    else
                        _irqCounter = 0;
                    break;

                case 0xE000:
                    if (!odd)
                    {
                        _irqEnabled = false;
                        _irqPending = false;
                    }
                    else
                    {
                        _irqEnabled = true;
                    }

                    break;
            }
        }

        public override void ClockPpu(int scanline, int cycle)
        {
            // The real MMC3 counts rising edges of PPU A12, which happens once
            // per visible scanline. Clocking once per scanline is an accurate
            // approximation for every licensed game.
            if (cycle != 0)
                return;

            if (_irqCounter == 0 || _irqReload)
            {
                _irqCounter = _irqLatch;
                _irqReload = false;
            }
            else
            {
                _irqCounter--;
                if (_irqCounter == 0 && _irqEnabled)
                    _irqPending = true;
            }
        }

        private void SyncMirroring(byte value)
        {
            if ((Cart.Flags6 & 0x08) != 0)
                return;

            Cart.Mirroring = (value & 1) == 0
                ? (int)MirroringMode.Vertical
                : (int)MirroringMode.Horizontal;
        }

        private void Sync()
        {
            int last = LastPrgBank8();
            int secondLast = last - 1 < 0 ? 0 : last - 1;

            bool prgSwap = (_bankSelect & 0x40) != 0;

            if (!prgSwap)
            {
                Mapping.SetPrg8((uint)0x8000, _regs[6]);
                Mapping.SetPrg8((uint)0xA000, _regs[7]);
                Mapping.SetPrg8((uint)0xC000, (uint)secondLast);
            }
            else
            {
                Mapping.SetPrg8((uint)0x8000, (uint)secondLast);
                Mapping.SetPrg8((uint)0xA000, _regs[7]);
                Mapping.SetPrg8((uint)0xC000, _regs[6]);
            }

            Mapping.SetPrg8((uint)0xE000, (uint)last);

            bool chrSwap = (_bankSelect & 0x80) != 0;

            if (!chrSwap)
            {
                Mapping.SetChr2(0x0000, (uint)(_regs[0] >> 1));
                Mapping.SetChr2(0x0800, (uint)(_regs[1] >> 1));
                Mapping.SetChr1(0x1000, _regs[2]);
                Mapping.SetChr1(0x1400, _regs[3]);
                Mapping.SetChr1(0x1800, _regs[4]);
                Mapping.SetChr1(0x1C00, _regs[5]);
            }
            else
            {
                Mapping.SetChr2(0x1000, (uint)(_regs[0] >> 1));
                Mapping.SetChr2(0x1800, (uint)(_regs[1] >> 1));
                Mapping.SetChr1(0x0000, _regs[2]);
                Mapping.SetChr1(0x0400, _regs[3]);
                Mapping.SetChr1(0x0800, _regs[4]);
                Mapping.SetChr1(0x0C00, _regs[5]);
            }
        }
    }
}
