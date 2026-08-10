namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Mapper 0: NROM.
    /// </summary>
    public sealed class NromBoard : BoardBase
    {
        public NromBoard(CartInfo cart, CartMapping mapping)
            : base(cart, mapping)
        {
        }

        public override int MapperId
        {
            get { return 0; }
        }

        public override void Power()
        {
            base.Power();
            SetupFixedBanks();
        }

        public override void Reset()
        {
            base.Reset();
            SetupFixedBanks();
        }

        private void SetupFixedBanks()
        {
            int banks16 = PrgBankCount16();

            if (banks16 <= 1)
            {
                // NROM-128:
                // $8000-$BFFF and $C000-$FFFF both point to same 16 KB.
                Mapping.SetPrg16((uint)0x8000, 0);
                Mapping.SetPrg16((uint)0xC000, 0);
            }
            else
            {
                // NROM-256:
                // linear 32 KB.
                Mapping.SetPrg32((uint)0x8000, 0);
            }

            // CHR bank 0.
            // If CHR ROM is absent, this maps CHR RAM page 0 if present.
            Mapping.SetChr8(0);
        }
    }
}