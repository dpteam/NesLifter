namespace NesLifter.Core.Mappers
{
    /// <summary>
    /// Временный интерфейс маппера.
    /// Будет заменён на IBoard в следующей пачке.
    /// </summary>
    public interface IMapper
    {
        int Id { get; }

        void Reset();

        byte ReadPrg(ushort address);
        void WritePrg(ushort address, byte value);

        byte ReadChr(ushort address);
        void WriteChr(ushort address, byte value);
    }
}