// Port of lib/jxl/ac_context.h — Block context mapping for AC decoding

namespace LibJxl.Decoder;

/// <summary>
/// Maps block properties (DC value, quant field, strategy order, channel) to context indices.
/// Port of jxl::BlockCtxMap from ac_context.h.
/// </summary>
public class BlockCtxMap
{
    public const int kNonZeroBuckets = 37;
    public const int kZeroDensityContextCount = 458;
    public const int kZeroDensityContextLimit = 474;

    /// <summary>DC thresholds per channel (3 channels).</summary>
    public int[][] DcThresholds = [[], [], []];

    /// <summary>Quantization field thresholds.</summary>
    public int[] QfThresholds = [];

    /// <summary>Context mapping table.</summary>
    public byte[] CtxMap;

    /// <summary>Number of contexts.</summary>
    public int NumCtxs;

    /// <summary>Number of DC contexts.</summary>
    public int NumDcCtxs;

    /// <summary>Default context map (3 channels × kNumOrders).</summary>
    public static readonly byte[] kDefaultCtxMap =
    {
         0,  1,  2,  2,  3,  3,  4,  5,  6,  6,  6,  6,  6,
         7,  8,  9,  9, 10, 11, 12, 13, 14, 14, 14, 14, 14,
         7,  8,  9,  9, 10, 11, 12, 13, 14, 14, 14, 14, 14,
    };

    public BlockCtxMap()
    {
        // Default: no thresholds → 1 DC bucket, 1 QF bucket, default ctx map
        CtxMap = new byte[kDefaultCtxMap.Length];
        Array.Copy(kDefaultCtxMap, CtxMap, kDefaultCtxMap.Length);
        NumCtxs = 15; // max value in kDefaultCtxMap + 1
        NumDcCtxs = 1 * 1; // (dc_buckets) * (qf_buckets)
    }

    /// <summary>
    /// Returns context index for a block.
    /// </summary>
    public int Context(int dcIdx, int qfIdx, int ord, int channel)
    {
        int ctxIdx = dcIdx * (QfThresholds.Length + 1) + qfIdx;
        int mapIdx = ctxIdx * (3 * AcStrategy.kNumOrders) + channel * AcStrategy.kNumOrders + ord;
        if (mapIdx >= CtxMap.Length) return 0;
        return CtxMap[mapIdx];
    }

    /// <summary>Gets the zero-density contexts offset for a block context.</summary>
    public int ZeroDensityContextsOffset(int blockCtx)
    {
        return blockCtx * kZeroDensityContextCount;
    }

    /// <summary>Total number of AC contexts.</summary>
    public int NumACContexts()
    {
        return NumCtxs * (kZeroDensityContextCount + kNonZeroBuckets);
    }

    /// <summary>Non-zero coefficient context.</summary>
    public int NonZeroContext(int nonZeros, int blockCtx)
    {
        int bucket;
        if (nonZeros == 0) bucket = 0;
        else if (nonZeros < 8) bucket = 1 + nonZeros;
        else if (nonZeros < 64) bucket = 9 + (nonZeros >> 3);
        else bucket = 17 + Math.Min(nonZeros >> 6, kNonZeroBuckets - 18);

        return blockCtx * kNonZeroBuckets + bucket;
    }
}
