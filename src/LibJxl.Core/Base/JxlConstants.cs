// Port of lib/jxl/base/common.h — shared constants and helpers
using System.Runtime.CompilerServices;

namespace LibJxl.Base;

/// <summary>Shared constants and helper functions.</summary>
public static class JxlConstants
{
    public const int BitsPerByte = 8;

    public const double Pi = 3.14159265358979323846264338327950288;

    /// <summary>Multiplier for conversion of log2(x) result to ln(x).</summary>
    public const float InvLog2e = 0.6931471805599453f;

    /// <summary>Default intensity target in nits (cd/m^2).</summary>
    public const float DefaultIntensityTarget = 255f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RoundUpBitsToByteMultiple(int bits) => (bits + 7) & ~7;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RoundUpToBlockDim(int dim) => (dim + 7) & ~7;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DivCeil(int a, int b) => (a + b - 1) / b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long DivCeil(long a, long b) => (a + b - 1) / b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RoundUpTo(int what, int align) => DivCeil(what, align) * align;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Clamp1<T>(T val, T low, T hi) where T : IComparable<T> =>
        val.CompareTo(low) < 0 ? low : val.CompareTo(hi) > 0 ? hi : val;

    public static bool SafeAdd(ulong a, ulong b, out ulong sum)
    {
        sum = a + b;
        return sum >= a;
    }
}
