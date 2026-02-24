// Port of lib/jxl/quant_weights.h + quant_weights.cc — dequantization matrices

using LibJxl.Bitstream;
using LibJxl.Fields;

namespace LibJxl.Decoder;

/// <summary>
/// Quantization table enumeration.
/// Port of jxl::QuantTable from quant_weights.h.
/// </summary>
public enum QuantTable
{
    DCT = 0,
    IDENTITY = 1,
    DCT2X2 = 2,
    DCT4X4 = 3,
    DCT16X16 = 4,
    DCT32X32 = 5,
    DCT8X16 = 6,
    DCT8X32 = 7,
    DCT16X32 = 8,
    DCT4X8 = 9,
    AFV0 = 10,
    DCT64X64 = 11,
    DCT32X64 = 12,
    DCT128X128 = 13,
    DCT64X128 = 14,
    DCT256X256 = 15,
    DCT128X256 = 16,
}

/// <summary>
/// Mode for encoding quantization weights.
/// Port of jxl::QuantEncoding::Mode.
/// </summary>
public enum QuantEncodingMode
{
    Library = 0,
    Identity = 1,
    DCT2 = 2,
    DCT4 = 3,
    DCT4X8 = 4,
    AFV = 5,
    DCT = 6,
    RAW = 7,
}

/// <summary>
/// DCT distance band weight parameters.
/// Port of jxl::DctQuantWeightParams.
/// </summary>
public class DctQuantWeightParams
{
    public const int kLog2MaxDistanceBands = 4;
    public const int kMaxDistanceBands = 1 + (1 << kLog2MaxDistanceBands); // 17

    public int NumDistanceBands;
    public readonly float[,] DistanceBands = new float[3, kMaxDistanceBands];
}

/// <summary>
/// Encoding specification for a single quantization table.
/// </summary>
public class QuantEncoding
{
    public QuantEncodingMode Mode;
    public int Predefined; // for Library mode

    // Identity weights: [channel][weight] (3×3)
    public readonly float[,] IdWeights = new float[3, 3];

    // DCT2 weights: [channel][weight] (3×6)
    public readonly float[,] Dct2Weights = new float[3, 6];

    // DCT4 multipliers: [channel][mult] (3×2)
    public readonly float[,] Dct4Multipliers = new float[3, 2];

    // DCT4x8 multipliers: [channel] (3)
    public readonly float[] Dct4x8Multipliers = new float[3];

    // AFV weights: [channel][weight] (3×9)
    public readonly float[,] AfvWeights = new float[3, 9];

    // DCT params (for DCT, DCT4, DCT4X8, AFV modes)
    public DctQuantWeightParams DctParams = new();
    public DctQuantWeightParams DctParamsAfv4x4 = new(); // for AFV mode

    public static QuantEncoding DefaultLibrary() => new() { Mode = QuantEncodingMode.Library, Predefined = 0 };
}

/// <summary>
/// Manages dequantization weight matrices.
/// Port of jxl::DequantMatrices from quant_weights.h.
/// </summary>
public class DequantMatrices
{
    public const int kNumQuantTables = 17;
    public const int kLog2NumQuantModes = 3;
    private const int kCeilLog2NumPredefinedTables = 0;

    // DC quantization constants
    public static readonly float[] kDefaultDCQuant = { 1.0f / 4096.0f, 1.0f / 512.0f, 1.0f / 256.0f };
    public static readonly float[] kDefaultInvDCQuant = { 4096.0f, 512.0f, 256.0f };

    // Required block sizes per table (in 8×8 blocks)
    public static readonly int[] RequiredSizeX =
        { 1, 1, 1, 1, 2, 4, 1, 1, 2, 1, 1, 8, 4, 16, 8, 32, 16 };
    public static readonly int[] RequiredSizeY =
        { 1, 1, 1, 1, 2, 4, 2, 4, 4, 1, 1, 8, 8, 16, 16, 32, 32 };

    /// <summary>Maps AcStrategyType to QuantTable.</summary>
    public static readonly QuantTable[] AcStrategyToQuantTable =
    {
        QuantTable.DCT, QuantTable.IDENTITY, QuantTable.DCT2X2,
        QuantTable.DCT4X4, QuantTable.DCT16X16, QuantTable.DCT32X32,
        QuantTable.DCT8X16, QuantTable.DCT8X16, QuantTable.DCT8X32,
        QuantTable.DCT8X32, QuantTable.DCT16X32, QuantTable.DCT16X32,
        QuantTable.DCT4X8, QuantTable.DCT4X8, QuantTable.AFV0,
        QuantTable.AFV0, QuantTable.AFV0, QuantTable.AFV0,
        QuantTable.DCT64X64, QuantTable.DCT32X64, QuantTable.DCT32X64,
        QuantTable.DCT128X128, QuantTable.DCT64X128, QuantTable.DCT64X128,
        QuantTable.DCT256X256, QuantTable.DCT128X256, QuantTable.DCT128X256,
    };

    private readonly float[] _dcQuant = new float[3];
    private readonly float[] _invDcQuant = new float[3];
    private readonly QuantEncoding[] _encodings = new QuantEncoding[kNumQuantTables];
    private float[]? _table;
    private float[]? _invTable;
    private readonly int[] _tableOffsets = new int[kNumQuantTables * 3];
    private uint _computedMask;
    private bool _allDefault = true;

    public DequantMatrices()
    {
        Array.Copy(kDefaultDCQuant, _dcQuant, 3);
        Array.Copy(kDefaultInvDCQuant, _invDcQuant, 3);
        for (int i = 0; i < kNumQuantTables; i++)
            _encodings[i] = QuantEncoding.DefaultLibrary();
    }

    public float DCQuant(int channel) => _dcQuant[channel];
    public float InvDCQuant(int channel) => _invDcQuant[channel];
    public bool AllDefault => _allDefault;

    /// <summary>
    /// Reads dequant matrices from bitstream.
    /// Port of DequantMatrices::Decode from quant_weights.cc.
    /// </summary>
    public bool Decode(BitReader br)
    {
        _allDefault = br.ReadBits(1) != 0;

        if (!_allDefault)
        {
            for (int i = 0; i < kNumQuantTables; i++)
            {
                _encodings[i] = new QuantEncoding();
                if (!DecodeQuantEncoding(br, _encodings[i], RequiredSizeX[i], RequiredSizeY[i]))
                    return false;
            }
        }
        else
        {
            for (int i = 0; i < kNumQuantTables; i++)
                _encodings[i] = QuantEncoding.DefaultLibrary();
        }

        _computedMask = 0;
        return true;
    }

    /// <summary>
    /// Reads DC quantization values from bitstream.
    /// Port of DequantMatrices::DecodeDC.
    /// </summary>
    public bool DecodeDC(BitReader br)
    {
        bool allDefault = br.ReadBits(1) != 0;

        if (!allDefault)
        {
            for (int c = 0; c < 3; c++)
            {
                if (!F16Coder.Read(br, out float dcq))
                    return false;
                _dcQuant[c] = dcq * (1.0f / 128.0f);

                if (_dcQuant[c] < 1e-8f)
                    return false;

                _invDcQuant[c] = 1.0f / _dcQuant[c];
            }
        }

        return true;
    }

    /// <summary>
    /// Gets a dequantization matrix for a strategy and channel.
    /// Returns a span into the table at the correct offset.
    /// If tables haven't been computed yet, returns defaults.
    /// </summary>
    public ReadOnlySpan<float> GetTable(AcStrategyType strategy, int channel)
    {
        var qt = AcStrategyToQuantTable[(int)strategy];
        int tableIdx = (int)qt;
        int offset = _tableOffsets[tableIdx * 3 + channel];
        return _table != null ? _table.AsSpan(offset) : ReadOnlySpan<float>.Empty;
    }

    /// <summary>
    /// Computes weight tables from encodings. For now: sets up default tables
    /// using simple flat weights (full computation requires all library tables).
    /// </summary>
    public bool EnsureComputed(uint acsMask)
    {
        if (_table != null && (_computedMask & acsMask) == acsMask)
            return true;

        // Compute table offsets
        int pos = 0;
        for (int i = 0; i < kNumQuantTables; i++)
        {
            int numBlocks = RequiredSizeX[i] * RequiredSizeY[i];
            int num = numBlocks * FrameConstants.DCTBlockSize;
            for (int c = 0; c < 3; c++)
            {
                _tableOffsets[i * 3 + c] = pos + c * num;
            }
            pos += 3 * num;
        }

        int totalSize = pos;
        _table ??= new float[totalSize];
        _invTable ??= new float[totalSize];

        // For default tables: fill with distance-band weights
        // This is a simplified version; full implementation would
        // compute all predefined library table weights
        for (int t = 0; t < kNumQuantTables; t++)
        {
            if ((_computedMask & (1u << t)) != 0) continue;

            int numBlocks = RequiredSizeX[t] * RequiredSizeY[t];
            int num = numBlocks * FrameConstants.DCTBlockSize;

            for (int c = 0; c < 3; c++)
            {
                int off = _tableOffsets[t * 3 + c];
                // Default: all weights = 1.0 (identity dequant)
                for (int k = 0; k < num; k++)
                {
                    _table[off + k] = 1.0f;
                    _invTable[off + k] = 1.0f;
                }
            }

            _computedMask |= (1u << t);
        }

        return true;
    }

    /// <summary>Decodes a single QuantEncoding from bitstream.</summary>
    private bool DecodeQuantEncoding(BitReader br, QuantEncoding enc, int reqSizeX, int reqSizeY)
    {
        int mode = (int)br.ReadBits(kLog2NumQuantModes);
        enc.Mode = (QuantEncodingMode)mode;

        switch (enc.Mode)
        {
            case QuantEncodingMode.Library:
                enc.Predefined = (kCeilLog2NumPredefinedTables > 0)
                    ? (int)br.ReadBits(kCeilLog2NumPredefinedTables)
                    : 0;
                break;

            case QuantEncodingMode.Identity:
                for (int c = 0; c < 3; c++)
                    for (int i = 0; i < 3; i++)
                    {
                        if (!F16Coder.Read(br, out float idw)) return false;
                        enc.IdWeights[c, i] = idw;
                        if (MathF.Abs(enc.IdWeights[c, i]) < 1e-8f) return false;
                        enc.IdWeights[c, i] *= 64.0f;
                    }
                break;

            case QuantEncodingMode.DCT2:
                for (int c = 0; c < 3; c++)
                    for (int i = 0; i < 6; i++)
                    {
                        if (!F16Coder.Read(br, out float dct2w)) return false;
                        enc.Dct2Weights[c, i] = dct2w;
                        if (MathF.Abs(enc.Dct2Weights[c, i]) < 1e-8f) return false;
                        enc.Dct2Weights[c, i] *= 64.0f;
                    }
                break;

            case QuantEncodingMode.DCT4:
                for (int c = 0; c < 3; c++)
                    for (int i = 0; i < 2; i++)
                    {
                        if (!F16Coder.Read(br, out float dct4m)) return false;
                        enc.Dct4Multipliers[c, i] = dct4m;
                        if (MathF.Abs(enc.Dct4Multipliers[c, i]) < 1e-8f) return false;
                    }
                if (!DecodeDctParams(br, enc.DctParams)) return false;
                break;

            case QuantEncodingMode.DCT4X8:
                for (int c = 0; c < 3; c++)
                {
                    if (!F16Coder.Read(br, out float dct4x8m)) return false;
                    enc.Dct4x8Multipliers[c] = dct4x8m;
                    if (MathF.Abs(enc.Dct4x8Multipliers[c]) < 1e-8f) return false;
                }
                if (!DecodeDctParams(br, enc.DctParams)) return false;
                break;

            case QuantEncodingMode.AFV:
                for (int c = 0; c < 3; c++)
                {
                    for (int i = 0; i < 9; i++)
                    {
                        if (!F16Coder.Read(br, out float afvw)) return false;
                        enc.AfvWeights[c, i] = afvw;
                    }
                    for (int i = 0; i < 6; i++)
                        enc.AfvWeights[c, i] *= 64.0f;
                }
                if (!DecodeDctParams(br, enc.DctParams)) return false;
                if (!DecodeDctParams(br, enc.DctParamsAfv4x4)) return false;
                break;

            case QuantEncodingMode.DCT:
                if (!DecodeDctParams(br, enc.DctParams)) return false;
                break;

            case QuantEncodingMode.RAW:
                // Raw mode requires modular decoding — stub
                return false;
        }

        return true;
    }

    /// <summary>Decodes DCT distance band parameters.</summary>
    private bool DecodeDctParams(BitReader br, DctQuantWeightParams p)
    {
        p.NumDistanceBands = (int)br.ReadBits(DctQuantWeightParams.kLog2MaxDistanceBands) + 1;

        for (int c = 0; c < 3; c++)
        {
            for (int i = 0; i < p.NumDistanceBands; i++)
            {
                if (!F16Coder.Read(br, out float db)) return false;
                p.DistanceBands[c, i] = db;
            }

            if (p.DistanceBands[c, 0] < 1e-8f) return false;
            p.DistanceBands[c, 0] *= 64.0f;
        }

        return true;
    }
}
