namespace Chatter {
    /// <summary>
    /// x86 CLR object layout used by the memory reader, separate from game fields.
    /// Offsets include the object's method-table word.
    /// </summary>
    internal static class Clr32Layout {
        internal const int WordSize = 4;
        internal const uint MinimumAddress = 0x10000;
        internal const uint AddressLimit = 0x7fff0000;
        internal const uint MethodTable = 0;
        internal const uint ListArray = 4;
        internal const uint ListCount = 12;
        internal const uint ListVersion = 16;
        internal const uint ArrayLength = 4;
        internal const uint ArrayData = 8;
        internal const uint StringLength = 4;
        internal const uint StringData = 8;
        internal const int CharacterSize = 2;
        internal const int DateTimeSize = 8;
        internal const ulong DateTimeTicksMask = 0x3fffffffffffffffUL;
    }
}
