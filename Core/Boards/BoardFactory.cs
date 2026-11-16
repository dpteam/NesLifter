using System;
using System.Collections.Generic;

namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Creates the board implementation for a cartridge.
    /// Every mapper listed in <see cref="NesLifter.Mappers.MapperCatalog"/>
    /// has exactly one board here, so analysis and runtime code generation
    /// always agree on the bank layout.
    /// </summary>
    public static class BoardFactory
    {
        private static readonly Dictionary<int, Func<CartInfo, CartMapping, IBoard>> Builders =
            new Dictionary<int, Func<CartInfo, CartMapping, IBoard>>
            {
                [0] = (c, m) => new NromBoard(c, m),
                [1] = (c, m) => new Mmc1Board(c, m),
                [2] = (c, m) => new UxRomBoard(c, m),
                [3] = (c, m) => new CnromBoard(c, m),
                [4] = (c, m) => new Mmc3Board(c, m),
                // Mapper 5 is MMC5, which is *not* MMC3-compatible: it has its own
                // register window at $5100-$5FFF, ExRAM, 8x8 attribute mode, a
                // hardware multiplier and three extra sound channels. Mapping it
                // onto Mmc3Board — as an earlier version did — produced a build
                // that reported success and rendered garbage. It is deliberately
                // absent so the pipeline refuses the cartridge instead.
                [7] = (c, m) => new AxRomBoard(c, m),
                [9] = (c, m) => new Mmc2Board(c, m),
                [11] = (c, m) => new GxRomBoard(c, m, 11),
                [13] = (c, m) => new CpromBoard(c, m),
                [21] = (c, m) => new VrcBoard(c, m, 21, VrcBoard.VrcWiring.Vrc4a),
                [22] = (c, m) => new VrcBoard(c, m, 22, VrcBoard.VrcWiring.Vrc2a),
                [23] = (c, m) => new VrcBoard(c, m, 23, VrcBoard.VrcWiring.Vrc4e),
                [24] = (c, m) => new VrcBoard(c, m, 24, VrcBoard.VrcWiring.Vrc6a),
                [25] = (c, m) => new VrcBoard(c, m, 25, VrcBoard.VrcWiring.Vrc4b),
                [26] = (c, m) => new VrcBoard(c, m, 26, VrcBoard.VrcWiring.Vrc6b),
                [34] = (c, m) => new BnromBoard(c, m),
                [66] = (c, m) => new GxRomBoard(c, m, 66),
                [69] = (c, m) => new Fme7Board(c, m),
                [71] = (c, m) => new CamericaBoard(c, m),
                [79] = (c, m) => new NinaBoard(c, m, 79, true, 0),
                [87] = (c, m) => new NinaBoard(c, m, 87, false, 1),
                [88] = (c, m) => new NinaBoard(c, m, 88, true, 0),
                [206] = (c, m) => new Mmc3Board(c, m),
                [232] = (c, m) => new QuattroBoard(c, m)
            };

        public static bool IsSupported(int mapperId)
        {
            return Builders.ContainsKey(mapperId);
        }

        public static IReadOnlyCollection<int> SupportedMappers
        {
            get
            {
                List<int> ids = new List<int>(Builders.Keys);
                ids.Sort();
                return ids;
            }
        }

        public static IBoard Create(CartInfo cart, CartMapping mapping)
        {
            if (cart == null)
                throw new ArgumentNullException(nameof(cart));

            if (mapping == null)
                throw new ArgumentNullException(nameof(mapping));

            if (!Builders.TryGetValue(cart.Mapper, out Func<CartInfo, CartMapping, IBoard>? builder))
            {
                throw new NotSupportedException(
                    "Mapper " + cart.Mapper + " is not implemented. Supported: " +
                    string.Join(", ", SupportedMappers));
            }

            return builder(cart, mapping);
        }

        /// <summary>
        /// Creates a board or a best-effort fallback for unknown mappers.
        /// The fallback maps PRG linearly, which is correct for the many
        /// unlicensed "mapper 0 in disguise" dumps and harmless otherwise.
        /// </summary>
        public static IBoard CreateOrDefault(CartInfo cart, CartMapping mapping, out bool fallback)
        {
            try
            {
                IBoard board = Create(cart, mapping);
                fallback = false;
                return board;
            }
            catch (NotSupportedException)
            {
                fallback = true;
                return new NromBoard(cart, mapping);
            }
        }
    }
}
