namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Base class for simple latch-based boards.
    /// Similar to latch.c from fceumm.
    /// </summary>
    public abstract class LatchBoard : BoardBase
    {
        protected ushort LatchAddr;
        protected byte LatchData;

        private readonly bool _busConflict;

        protected LatchBoard(CartInfo cart, CartMapping mapping, bool busConflict)
            : base(cart, mapping)
        {
            _busConflict = busConflict;
        }

        public override void Power()
        {
            base.Power();

            LatchAddr = 0;
            LatchData = 0;

            Sync();
        }

        public override void Reset()
        {
            LatchAddr = 0;
            LatchData = 0;

            Sync();
        }

        public override void WritePrg(ushort address, byte value)
        {
            if (address < 0x8000)
                return;

            if (_busConflict)
            {
                // Bus conflict behavior:
                // value is ANDed with current PRG data at the same address.
                value &= Mapping.ReadPrg(address);
            }

            LatchAddr = address;
            LatchData = value;

            Sync();
        }

        /// <summary>
        /// Board-specific bank synchronization.
        /// </summary>
        protected abstract void Sync();
    }
}