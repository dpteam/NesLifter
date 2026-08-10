using System;

namespace NesLifter.Core.Boards
{
    /// <summary>
    /// Creates board instance from CartInfo.
    /// Later this replaces MapperFactory.
    /// </summary>
    public static class BoardFactory
    {
        public static IBoard Create(CartInfo cart, CartMapping mapping)
        {
            if (cart == null)
                throw new ArgumentNullException("cart");

            if (mapping == null)
                throw new ArgumentNullException("mapping");

            switch (cart.Mapper)
            {
                case 0:
                    return new NromBoard(cart, mapping);

                case 1:
                    return new Mmc1Board(cart, mapping);

                case 2:
                    return new UxRomBoard(cart, mapping);

                case 3:
                    return new CnromBoard(cart, mapping);

                default:
                    throw new NotSupportedException(
                        "Board for mapper " + cart.Mapper + " is not implemented yet.");
            }
        }
    }
}
