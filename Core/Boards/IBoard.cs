namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Новый интерфейс борда/маппера.
    /// Заменяет старый IMapper.
    /// </summary>
    public interface IBoard
    {
        int MapperId { get; }

        /// <summary>
        /// Power-on state.
        /// Board should map initial PRG/CHR here.
        /// </summary>
        void Power();

        /// <summary>
        /// Reset button state.
        /// </summary>
        void Reset();

        /// <summary>
        /// Close/unload board.
        /// </summary>
        void Close();

        /// <summary>
        /// Write to $8000-$FFFF, usually mapper register write.
        /// </summary>
        void WritePrg(ushort address, byte value);

        /// <summary>
        /// Optional low read hook, e.g. $4020-$5FFF.
        /// </summary>
        byte ReadLow(ushort address);

        /// <summary>
        /// Optional low write hook, e.g. $4020-$5FFF.
        /// </summary>
        void WriteLow(ushort address, byte value);

        /// <summary>
        /// Optional CPU clock hook for IRQ counters.
        /// </summary>
        void ClockCpu(int cycles);

        /// <summary>
        /// Optional PPU clock hook for scanline/cycle-based IRQs.
        /// </summary>
        void ClockPpu(int scanline, int cycle);
    }
}