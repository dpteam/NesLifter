namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Mapper 3: CNROM.
    /// Fixed PRG, switchable 8 KB CHR.
    /// </summary>
    public sealed class CnromBoard : LatchBoard
    {
        public CnromBoard(CartInfo cart, CartMapping mapping)
            : base(cart, mapping, false)
        {
        }

        public override int MapperId
        {
            get { return 3; }
        }

        public override void Power()
        {
            base.Power();
            SetupFixedPrg();
        }

        public override void Reset()
        {
            base.Reset();
            SetupFixedPrg();
        }

        protected override void Sync()
        {
            int bank = SelectChrBank(LatchData);
            Mapping.SetChr8((uint)bank);
        }

        private void SetupFixedPrg()
        {
            int banks16 = PrgBankCount16();

            if (banks16 <= 1)
            {
                Mapping.SetPrg16((uint)0x8000, 0);
                Mapping.SetPrg16((uint)0xC000, 0);
            }
            else
            {
                Mapping.SetPrg32((uint)0x8000, 0);
            }
        }

        private int SelectChrBank(int value)
        {
            int banks = ChrBankCount8();

            if (banks <= 1)
                return 0;

            // If bank count is power of two, mask.
            if ((banks & (banks - 1)) == 0)
                return value & (banks - 1);

            return value % banks;
        }
    }
}