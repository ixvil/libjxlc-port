// Port of lib/jxl/base/bits.h — bit manipulation utilities
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace LibJxl.Base;

/// <summary>Specialized instructions for processing register-size bit arrays.</summary>
public static class BitOps
{
    /// <summary>Count leading zeros for uint32. Undefined for x == 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Num0BitsAboveMS1Bit_Nonzero(uint x)
    {
        Debug.Assert(x != 0);
        return BitOperations.LeadingZeroCount(x);
    }

    /// <summary>Count leading zeros for uint64. Undefined for x == 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Num0BitsAboveMS1Bit_Nonzero(ulong x)
    {
        Debug.Assert(x != 0);
        return BitOperations.LeadingZeroCount(x);
    }

    /// <summary>Count trailing zeros for uint32. Undefined for x == 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Num0BitsBelowLS1Bit_Nonzero(uint x)
    {
        Debug.Assert(x != 0);
        return BitOperations.TrailingZeroCount(x);
    }

    /// <summary>Count trailing zeros for uint64. Undefined for x == 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Num0BitsBelowLS1Bit_Nonzero(ulong x)
    {
        Debug.Assert(x != 0);
        return BitOperations.TrailingZeroCount(x);
    }

    /// <summary>Returns bit width for x == 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Num0BitsAboveMS1Bit(uint x) =>
        x == 0 ? 32 : Num0BitsAboveMS1Bit_Nonzero(x);

    /// <summary>Returns bit width for x == 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Num0BitsAboveMS1Bit(ulong x) =>
        x == 0 ? 64 : Num0BitsAboveMS1Bit_Nonzero(x);

    /// <summary>Returns bit width for x == 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Num0BitsBelowLS1Bit(uint x) =>
        x == 0 ? 32 : Num0BitsBelowLS1Bit_Nonzero(x);

    /// <summary>Returns bit width for x == 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Num0BitsBelowLS1Bit(ulong x) =>
        x == 0 ? 64 : Num0BitsBelowLS1Bit_Nonzero(x);

    /// <summary>Base-2 logarithm, rounded down. x must be nonzero.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FloorLog2Nonzero(uint x) => 31 ^ Num0BitsAboveMS1Bit_Nonzero(x);

    /// <summary>Base-2 logarithm, rounded down. x must be nonzero.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FloorLog2Nonzero(ulong x) => 63 ^ Num0BitsAboveMS1Bit_Nonzero(x);

    /// <summary>Base-2 logarithm, rounded up. x must be nonzero.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CeilLog2Nonzero(uint x)
    {
        int floor = FloorLog2Nonzero(x);
        return (x & (x - 1)) == 0 ? floor : floor + 1;
    }

    /// <summary>Base-2 logarithm, rounded up. x must be nonzero.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CeilLog2Nonzero(ulong x)
    {
        int floor = FloorLog2Nonzero(x);
        return (x & (x - 1)) == 0 ? floor : floor + 1;
    }
}
