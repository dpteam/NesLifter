namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Mapper 69 / Sunsoft FME-7.
    /// A single command/parameter register pair drives eight 1 KB CHR banks,
    /// three 8 KB PRG banks, mirroring and a 16-bit scanline counter.
    /// Reference: NESdev "Sunsoft FME-7".
    /// </summary>
    public sealed class Fme7Board : BoardBase
    {
        private readonly byte[] _chr = new byte[8];
        private readonly byte[] _prg = new byte[3];

        private byte _command;
        private byte _ramControl;
        private int _irqCounter;
        private int _irqCounterEnable;
        private bool _irqEnabled;
        private bool _irqPending;

        public Fme7Board(CartInfo cart, CartMapping mapping)
            : base(cart, mapping)
        {
        }

        public override int MapperId => 69;

        public bool IrqPending => _irqPending;

        public override void Power()
        {
            base.Power();

            for (int i = 0; i < _chr.Length; i++)
                _chr[i] = 0;

            _prg[0] = 0;
            _prg[1] = 1;
            _prg[2] = 2;

            _command = 0;
            _ramControl = 0;
            _irqCounter = 0;
            _irqCounterEnable = 0;
            _irqEnabled = false;
            _irqPending = false;

            Sync();
        }

        public override void Reset()
        {
            _irqPending = false;
            Sync();
        }

        public override void WritePrg(ushort address, byte value)
        {
            if (address >= 0x8000 && address < 0xA000)
            {
                _command = (byte)(value & 0x0F);
                return;
            }

            if (address < 0xA000 || address >= 0xC000)
                return;

            switch (_command)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                    _chr[_command] = value;
                    Mapping.SetChr1((uint)(_command * 0x400), value);
                    break;

                case 8:
                    _prg[0] = (byte)(value & 0x3F);
                    Sync();
                    break;

                case 9:
                    _prg[1] = (byte)(value & 0x3F);
                    Sync();
                    break;

                case 0x0A:
                    _prg[2] = (byte)(value & 0x3F);
                    Sync();
                    break;

                case 0x0B:
                    _ramControl = value;
                    break;

                case 0x0C:
                    SyncMirroring(value);
                    break;

                case 0x0D:
                    _irqEnabled = (value & 0x80) != 0;
                    _irqCounterEnable = value & 0x01;

                    if (!_irqEnabled)
                        _irqPending = false;

                    break;

                case 0x0E:
                    _irqCounter = (_irqCounter & 0xFF00) | value;
                    break;

                case 0x0F:
                    _irqCounter = (_irqCounter & 0x00FF) | (value << 8);
                    _irqPending = false;
                    break;
            }
        }

        public override void ClockPpu(int scanline, int cycle)
        {
            if (cycle != 0 || !_irqEnabled)
                return;

            if (_irqCounterEnable != 0)
            {
                _irqCounter--;
            }
            else
            {
                // CPU-cycle mode: roughly three CPU cycles per scanline.
                _irqCounter -= 3;
            }

            if (_irqCounter <= 0)
            {
                _irqCounter = 0xFFFF;
                _irqPending = true;
            }
        }

        private void SyncMirroring(byte value)
        {
            if ((Cart.Flags6 & 0x08) != 0)
                return;

            Cart.Mirroring = (value & 3) switch
            {
                0 => (int)MirroringMode.Vertical,
                1 => (int)MirroringMode.Horizontal,
                2 => (int)MirroringMode.OneScreenLow,
                _ => (int)MirroringMode.OneScreenHigh
            };
        }

        private void Sync()
        {
            Mapping.SetPrg8((uint)0x8000, _prg[0]);
            Mapping.SetPrg8((uint)0xA000, _prg[1]);
            Mapping.SetPrg8((uint)0xC000, _prg[2]);
            Mapping.SetPrg8((uint)0xE000, (uint)LastPrgBank8());

            for (int i = 0; i < 8; i++)
                Mapping.SetChr1((uint)(i * 0x400), _chr[i]);
        }
    }
}
