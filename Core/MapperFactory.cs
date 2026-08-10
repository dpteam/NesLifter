using System;
using NesLifter.Core.Mappers;
using NesLifter.Mappers;

namespace NesLifter.Core
{
    public static class MapperFactory
    {
        public static IMapper Create(int mapperId, NesRom rom)
        {
            switch (mapperId)
            {
                case 0:
                    return new Nrom(rom);

                case 1:
                    return new MMC1(rom);

                case 2:
                    return new UxROM(rom);

                case 3:
                    return new CNROM(rom);

                default:
                    throw new NotSupportedException(
                        "Mapper " + mapperId + " is not implemented yet.");
            }
        }
    }
}