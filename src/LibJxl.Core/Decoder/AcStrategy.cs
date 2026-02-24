// Port of lib/jxl/ac_strategy.h — AC strategy types and block dimensions

namespace LibJxl.Decoder;

/// <summary>
/// AC strategy types — which DCT variant is used for a block.
/// Port of jxl::AcStrategyType from ac_strategy.h.
/// </summary>
public enum AcStrategyType : uint
{
    DCT = 0,
    IDENTITY = 1,
    DCT2X2 = 2,
    DCT4X4 = 3,
    DCT16X16 = 4,
    DCT32X32 = 5,
    DCT16X8 = 6,
    DCT8X16 = 7,
    DCT32X8 = 8,
    DCT8X32 = 9,
    DCT32X16 = 10,
    DCT16X32 = 11,
    DCT4X8 = 12,
    DCT8X4 = 13,
    AFV0 = 14,
    AFV1 = 15,
    AFV2 = 16,
    AFV3 = 17,
    DCT64X64 = 18,
    DCT64X32 = 19,
    DCT32X64 = 20,
    DCT128X128 = 21,
    DCT128X64 = 22,
    DCT64X128 = 23,
    DCT256X256 = 24,
    DCT256X128 = 25,
    DCT128X256 = 26,
}

/// <summary>
/// AC strategy information: block dimensions and coefficient counts.
/// Port of jxl::AcStrategy from ac_strategy.h.
/// </summary>
public static class AcStrategy
{
    public const int kNumValidStrategies = 27;
    public const int kMaxCoeffBlocks = 32;
    public const int kMaxBlockDim = FrameConstants.BlockDim * kMaxCoeffBlocks; // 256
    public const int kMaxCoeffArea = kMaxBlockDim * kMaxBlockDim; // 65536

    /// <summary>Number of 8×8 blocks covered in X direction per strategy.</summary>
    public static readonly int[] CoveredBlocksX =
    {
        1, 1, 1, 1, 2, 4, 1, 2, 1, 4, 2, 4, 1, 1, 1, 1, 1, 1, 8, 4, 8, 16, 8, 16, 32, 16, 32,
    };

    /// <summary>Number of 8×8 blocks covered in Y direction per strategy.</summary>
    public static readonly int[] CoveredBlocksY =
    {
        1, 1, 1, 1, 2, 4, 2, 1, 4, 1, 4, 2, 1, 1, 1, 1, 1, 1, 8, 8, 4, 16, 16, 8, 32, 32, 16,
    };

    /// <summary>Log2 of total covered blocks per strategy.</summary>
    public static readonly int[] Log2CoveredBlocks =
    {
        0, 0, 0, 0, 2, 4, 1, 1, 2, 2, 3, 3, 0, 0, 0, 0, 0, 0, 6, 5, 5, 8, 7, 7, 10, 9, 9,
    };

    /// <summary>Maps AcStrategyType to coefficient order bucket (0..12).</summary>
    public static readonly int[] StrategyOrder =
    {
        0, 1, 1, 1, 2, 3, 4, 4, 5, 5, 6, 6, 1, 1, 1, 1, 1, 1, 7, 8, 8, 9, 10, 10, 11, 12, 12,
    };

    /// <summary>Total number of coefficient order buckets.</summary>
    public const int kNumOrders = 13;

    /// <summary>Gets the total number of DCT coefficients for a strategy.</summary>
    public static int CoeffCount(AcStrategyType type)
    {
        int idx = (int)type;
        return CoveredBlocksX[idx] * CoveredBlocksY[idx] * FrameConstants.DCTBlockSize;
    }

    /// <summary>Gets the covered blocks count for a strategy.</summary>
    public static int CoveredBlocks(AcStrategyType type)
    {
        int idx = (int)type;
        return CoveredBlocksX[idx] * CoveredBlocksY[idx];
    }
}
