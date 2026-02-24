// Port of field codecs from lib/jxl/fields.cc — U64, F16 reading and enum utilities
using System.Runtime.CompilerServices;
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;

namespace LibJxl.Fields;

/// <summary>U64 variable-length coder. Port of jxl::U64Coder.</summary>
public static class U64Coder
{
    public static ulong Read(BitReader br)
    {
        ulong selector = br.ReadFixedBits(2);
        if (selector == 0) return 0;
        if (selector == 1) return 1 + br.ReadFixedBits(4);
        if (selector == 2) return 17 + br.ReadFixedBits(8);

        // selector == 3: variable-length
        ulong value = br.ReadFixedBits(12);
        int shift = 12;
        while (br.ReadFixedBits(1) != 0)
        {
            if (shift >= 60)
            {
                value |= br.ReadFixedBits(4) << shift;
                break;
            }
            value |= br.ReadFixedBits(8) << shift;
            shift += 8;
        }
        return value;
    }
}

/// <summary>IEEE 754 half-precision float coder. Port of jxl::F16Coder.</summary>
public static class F16Coder
{
    public static JxlStatus Read(BitReader br, out float value)
    {
        uint bits16 = (uint)br.ReadFixedBits(16);
        uint sign = bits16 >> 15;
        uint biasedExp = (bits16 >> 10) & 0x1F;
        uint mantissa = bits16 & 0x3FF;

        if (biasedExp == 31)
        {
            value = 0;
            return false; // Infinity or NaN not allowed
        }

        if (biasedExp == 0)
        {
            // Denormalized or zero
            value = (1.0f / 16384) * (mantissa * (1.0f / 1024));
            if (sign != 0) value = -value;
            return true;
        }

        // Normalized
        int exp = (int)biasedExp - 15;
        value = MathF.ScaleB(1.0f + (mantissa * (1.0f / 1024)), exp);
        if (sign != 0) value = -value;
        return true;
    }
}

/// <summary>Signed integer packing for field encoding.</summary>
public static class SignedPack
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint PackSigned(int value)
    {
        return (uint)((value < 0) ? (2 * (uint)(-value) - 1) : (2 * (uint)value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int UnpackSigned(uint value)
    {
        return (value & 1) != 0 ? -((int)(value + 1) / 2) : (int)(value / 2);
    }
}

/// <summary>
/// Extension mechanism for forward compatibility.
/// Reads extensions bit field and can skip unknown extension data.
/// </summary>
public static class ExtensionsReader
{
    public static JxlStatus BeginExtensions(BitReader br, out ulong extensions)
    {
        extensions = U64Coder.Read(br);
        return true;
    }

    public static JxlStatus EndExtensions(BitReader br, ulong extensions, long extensionBitsStart)
    {
        if (extensions == 0) return true;

        // Skip unknown extension bits
        // In the actual format, each extension has its size stored
        // For now, just skip them if any
        // TODO: implement proper extension size reading
        return true;
    }
}

/// <summary>Helper for reading enum values from bitstream.</summary>
public static class EnumReader
{
    /// <summary>Reads an enum value encoded as an index into valid values.</summary>
    public static T ReadEnum<T>(BitReader br, T[] validValues) where T : struct
    {
        if (validValues.Length <= 1) return validValues.Length > 0 ? validValues[0] : default;

        int nbits = BitOps.CeilLog2Nonzero((uint)validValues.Length);
        int index = (int)br.ReadBits(nbits);
        if (index >= validValues.Length) index = 0;
        return validValues[index];
    }
}
