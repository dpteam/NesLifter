namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Mapper 2: UxROM.
    /// Switchable 16 KB PRG at $8000, fixed last 16 KB at $C000.
    /// Usually CHR RAM.
    /// </summary>
    public sealed class UxRomBoard : LatchBoard
    {
        public UxRomBoard(CartInfo cart, CartMapping mapping)
            : base(cart, mapping, false)
        {
        }

        public override int MapperId
        {
            get { return 2; }
        }

        protected override void Sync()
        {
            Mapping.SetPrg16((uint)0x8000, LatchData);
            Mapping.SetPrg16((uint)0xC000, (uint)LastPrgBank16());

            // UxROM normally uses CHR RAM.
            Mapping.SetChr8(0);
        }
    }
}