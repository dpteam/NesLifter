using System;

namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Base class for all boards.
    /// Holds CartInfo and CartMapping, and prepares base ROM/RAM chips.
    /// </summary>
    public abstract class BoardBase : IBoard
    {
        protected readonly CartInfo Cart;
        protected readonly CartMapping Mapping;

        /// <summary>
        /// PRG RAM / WRAM, if allocated.
        /// Usually mapped as chip 0x10.
        /// </summary>
        protected byte[] WrkRam = Array.Empty<byte>();

        /// <summary>
        /// CHR RAM, if game has no CHR ROM.
        /// Usually mapped as chip 0.
        /// </summary>
        protected byte[] ChrRam = Array.Empty<byte>();

        protected BoardBase(CartInfo cart, CartMapping mapping)
        {
            if (cart == null)
                throw new ArgumentNullException("cart");

            if (mapping == null)
                throw new ArgumentNullException("mapping");

            Cart = cart;
            Mapping = mapping;
        }

        public abstract int MapperId { get; }

        public virtual void Power()
        {
            SetupDefaultCartMemory();
        }

        public virtual void Reset()
        {
        }

        public virtual void Close()
        {
        }

        public virtual void WritePrg(ushort address, byte value)
        {
        }

        public virtual byte ReadLow(ushort address)
        {
            return 0;
        }

        public virtual void WriteLow(ushort address, byte value)
        {
        }

        public virtual void ClockCpu(int cycles)
        {
        }

        public virtual void ClockPpu(int scanline, int cycle)
        {
        }

        /// <summary>
        /// Prepares PRG ROM, optional WRAM, CHR ROM or CHR RAM chips.
        /// Does not set final CPU/PPU banks; board must do that itself.
        /// </summary>
        protected void SetupDefaultCartMemory()
        {
            Mapping.Reset();

            // Chip 0: PRG ROM.
            Mapping.SetupPrgMapping(0, Cart.PrgRom, Cart.PrgRom.Length, false);

            // Chip 0x10: PRG RAM / WRAM.
            int wramSize = Cart.GetPrgRamBytes(8192);

            if (wramSize > 0)
            {
                WrkRam = new byte[wramSize];
                Mapping.SetupPrgMapping(0x10, WrkRam, wramSize, true);

                // Default common WRAM window.
                Mapping.SetPrg8r(0x10, (uint)0x6000, 0);
            }
            else
            {
                WrkRam = Array.Empty<byte>();
            }

            // CHR ROM or CHR RAM.
            if (Cart.ChrRom.Length > 0)
            {
                ChrRam = Array.Empty<byte>();
                Mapping.SetupChrMapping(0, Cart.ChrRom, Cart.ChrRom.Length, false);
            }
            else
            {
                int chrRamSize = Cart.GetChrRamBytes(8192);

                if (chrRamSize > 0)
                {
                    ChrRam = new byte[chrRamSize];
                    Mapping.SetupChrMapping(0, ChrRam, chrRamSize, true);
                }
                else
                {
                    ChrRam = Array.Empty<byte>();
                }
            }
        }

        protected int PrgBankCount16()
        {
            if (Cart.PrgRom.Length <= 0)
                return 0;

            return Cart.PrgRom.Length / 0x4000;
        }

        protected int LastPrgBank16()
        {
            int count = PrgBankCount16();

            if (count <= 0)
                return 0;

            return count - 1;
        }

        protected int ChrBankCount8()
        {
            if (Cart.ChrRom.Length <= 0)
                return 0;

            return Cart.ChrRom.Length / 0x2000;
        }

        protected int LastChrBank8()
        {
            int count = ChrBankCount8();

            if (count <= 0)
                return 0;

            return count - 1;
        }
    }
}