// Minimal field encoding utilities for reading U32 fields from bitstream.
// Port of relevant parts of lib/jxl/field_encodings.h and fields.cc.
using System.Runtime.CompilerServices;
using LibJxl.Bitstream;

namespace LibJxl.Entropy;

/// <summary>
/// Distribution descriptor for U32 fields. Represents either a direct value
/// or a bits+offset range. Port of jxl::U32Distr.
/// </summary>
public readonly struct U32Distr
{
    private readonly uint _d;
    private const uint KDirect = 0x80000000u;

    private U32Distr(uint d) => _d = d;

    public bool IsDirect => (_d & KDirect) != 0;
    public uint DirectValue => _d & (KDirect - 1);
    public int ExtraBits => (int)(_d & 0x1F) + 1;
    public uint Offset => (_d >> 5) & 0x3FFFFFF;

    /// <summary>A direct-coded value (no extra bits).</summary>
    public static U32Distr Val(uint value) => new(value | KDirect);

    /// <summary>Value = ReadBits(bits) + offset.</summary>
    public static U32Distr BitsOffset(uint bits, uint offset) =>
        new(((bits - 1) & 0x1F) + ((offset & 0x3FFFFFF) << 5));

    /// <summary>Value = ReadBits(bits).</summary>
    public static U32Distr Bits(uint bits) => BitsOffset(bits, 0);
}

/// <summary>Minimal field reader for reading U32 and Bool fields from bitstream.</summary>
public static class FieldReader
{
    /// <summary>Reads a U32 field with 4 distributions (2-bit selector).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadU32(BitReader br, U32Distr d0, U32Distr d1, U32Distr d2, U32Distr d3)
    {
        uint selector = (uint)br.ReadFixedBits(2);
        var d = selector switch
        {
            0 => d0,
            1 => d1,
            2 => d2,
            _ => d3
        };

        if (d.IsDirect) return d.DirectValue;
        return (uint)br.ReadBits(d.ExtraBits) + d.Offset;
    }

    /// <summary>Reads a Bool field with default value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadBool(BitReader br)
    {
        return br.ReadFixedBits(1) != 0;
    }
}
