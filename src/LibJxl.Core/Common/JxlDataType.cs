// Port of lib/include/jxl/types.h — data types
namespace LibJxl.Common;

/// <summary>Data type for the sample values per channel per pixel.</summary>
public enum JxlDataType
{
    /// <summary>32-bit single-precision float, range 0.0-1.0 (within gamut).</summary>
    Float = 0,

    /// <summary>uint8 type. May clip wide color gamut.</summary>
    Uint8 = 2,

    /// <summary>uint16 type. May clip wide color gamut.</summary>
    Uint16 = 3,

    /// <summary>16-bit IEEE 754 half-precision float.</summary>
    Float16 = 5,
}

/// <summary>Ordering of multi-byte data.</summary>
public enum JxlEndianness
{
    /// <summary>Use system endianness.</summary>
    NativeEndian = 0,

    /// <summary>Force little endian.</summary>
    LittleEndian = 1,

    /// <summary>Force big endian.</summary>
    BigEndian = 2,
}

/// <summary>Bit depth type for input/output buffers.</summary>
public enum JxlBitDepthType
{
    /// <summary>Full range of the pixel format data type.</summary>
    FromPixelFormat = 0,

    /// <summary>Range defined by bits_per_sample in basic info.</summary>
    FromCodestream = 1,

    /// <summary>Custom range (decoder only).</summary>
    Custom = 2,
}

/// <summary>Data type for describing bit depth interpretation.</summary>
public struct JxlBitDepth
{
    public JxlBitDepthType Type;
    public uint BitsPerSample;
    public uint ExponentBitsPerSample;
}

/// <summary>Pixel format for input/output buffers.</summary>
public struct JxlPixelFormat
{
    /// <summary>Number of channels (1=gray, 2=gray+alpha, 3=RGB, 4=RGBA).</summary>
    public uint NumChannels;

    /// <summary>Data type of each channel.</summary>
    public JxlDataType DataType;

    /// <summary>Endianness for multi-byte types.</summary>
    public JxlEndianness Endianness;

    /// <summary>Scanline alignment in bytes (0 or 1 = no alignment).</summary>
    public int Align;
}
