// Port of lib/jxl/jpeg/jpeg_data.h + jpeg_data.cc — JPEG data structures and serialization

namespace LibJxl.Jpeg;

/// <summary>JPEG constants.</summary>
public static class JpegConstants
{
    public const int kMaxComponents = 4;
    public const int kMaxQuantTables = 4;
    public const int kMaxHuffmanTables = 4;
    public const int kJpegHuffmanMaxBitLength = 16;
    public const int kJpegHuffmanAlphabetSize = 256;
    public const int kJpegDCAlphabetSize = 12;
    public const int kMaxDHTMarkers = 512;
    public const int kMaxDimPixels = 65535;
    public const int kDCTBlockSize = 64;
    public const byte kApp1 = 0xE1;
    public const byte kApp2 = 0xE2;

    public static readonly byte[] kIccProfileTag =
        { (byte)'I', (byte)'C', (byte)'C', (byte)'_',
          (byte)'P', (byte)'R', (byte)'O', (byte)'F',
          (byte)'I', (byte)'L', (byte)'E', 0 };

    public static readonly byte[] kExifTag =
        { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 };

    public static readonly byte[] kXMPTag = System.Text.Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");

    /// <summary>Zig-zag to natural order mapping (80 entries, with safety padding).</summary>
    public static readonly uint[] kJPEGNaturalOrder =
    {
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
        // extra entries for safety in decoder
        63, 63, 63, 63, 63, 63, 63, 63,
        63, 63, 63, 63, 63, 63, 63, 63
    };

    /// <summary>Natural to zig-zag order mapping.</summary>
    public static readonly uint[] kJPEGZigZagOrder =
    {
         0,  1,  5,  6, 14, 15, 27, 28,
         2,  4,  7, 13, 16, 26, 29, 42,
         3,  8, 12, 17, 25, 30, 41, 43,
         9, 11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63
    };
}

/// <summary>Quantization values for an 8x8 pixel block.</summary>
public class JPEGQuantTable
{
    public int[] Values = new int[JpegConstants.kDCTBlockSize];
    public uint Precision;
    public uint Index;
    public bool IsLast = true;
}

/// <summary>Huffman code and decoding lookup table.</summary>
public class JPEGHuffmanCode
{
    public uint[] Counts = new uint[JpegConstants.kJpegHuffmanMaxBitLength + 1];
    public uint[] Values = new uint[JpegConstants.kJpegHuffmanAlphabetSize + 1];
    public int SlotId;
    public bool IsLast = true;
}

/// <summary>Huffman table indexes used for one component of one scan.</summary>
public struct JPEGComponentScanInfo
{
    public uint CompIdx;
    public uint DcTblIdx;
    public uint AcTblIdx;
}

/// <summary>Extra zero run info for bit-precise reconstruction.</summary>
public struct ExtraZeroRunInfo
{
    public uint BlockIdx;
    public uint NumExtraZeroRuns;
}

/// <summary>Contains information for one JPEG scan.</summary>
public class JPEGScanInfo
{
    public uint Ss;
    public uint Se;
    public uint Ah;
    public uint Al;
    public uint NumComponents;
    public JPEGComponentScanInfo[] Components = new JPEGComponentScanInfo[4];
    public uint LastNeededPass;

    // Extra information for bit-precise JPEG reconstruction
    public List<uint> ResetPoints = new();
    public List<ExtraZeroRunInfo> ExtraZeroRuns = new();
}

/// <summary>Represents one component of a JPEG file.</summary>
public class JPEGComponent
{
    public uint Id;
    public int HSampFactor = 1;
    public int VSampFactor = 1;
    public uint QuantIdx;
    public uint WidthInBlocks;
    public uint HeightInBlocks;
    /// <summary>DCT coefficients laid out block-by-block.</summary>
    public short[] Coeffs = Array.Empty<short>();
}

/// <summary>APP marker type for identifying known marker formats.</summary>
public enum AppMarkerType : uint
{
    Unknown = 0,
    ICC = 1,
    Exif = 2,
    XMP = 3,
}

/// <summary>
/// Represents a parsed JPEG file.
/// Port of jxl::jpeg::JPEGData.
/// </summary>
public class JPEGData
{
    public int Width;
    public int Height;
    public uint RestartInterval;
    public List<byte[]> AppData = new();
    public List<AppMarkerType> AppMarkerType = new();
    public List<byte[]> ComData = new();
    public List<JPEGQuantTable> Quant = new();
    public List<JPEGHuffmanCode> HuffmanCode = new();
    public List<JPEGComponent> Components = new();
    public List<JPEGScanInfo> ScanInfo = new();
    public List<byte> MarkerOrder = new();
    public List<byte[]> InterMarkerData = new();
    public byte[] TailData = Array.Empty<byte>();

    // Extra information required for bit-precise JPEG file reconstruction
    public bool HasZeroPaddingBit;
    public List<byte> PaddingBits = new();

    /// <summary>
    /// Calculate MCU dimensions for a scan.
    /// Port of JPEGData::CalculateMcuSize.
    /// </summary>
    public void CalculateMcuSize(JPEGScanInfo scan, out int mcusPerRow, out int mcuRows)
    {
        bool isInterleaved = scan.NumComponents > 1;
        var baseComponent = Components[(int)scan.Components[0].CompIdx];
        int hGroup = isInterleaved ? 1 : baseComponent.HSampFactor;
        int vGroup = isInterleaved ? 1 : baseComponent.VSampFactor;

        int maxHSampFactor = 1;
        int maxVSampFactor = 1;
        foreach (var c in Components)
        {
            maxHSampFactor = Math.Max(c.HSampFactor, maxHSampFactor);
            maxVSampFactor = Math.Max(c.VSampFactor, maxVSampFactor);
        }

        mcusPerRow = DivCeil(Width * hGroup, 8 * maxHSampFactor);
        mcuRows = DivCeil(Height * vGroup, 8 * maxVSampFactor);
    }

    private static int DivCeil(int a, int b) => (a + b - 1) / b;
}
