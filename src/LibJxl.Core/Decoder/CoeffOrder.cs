// Port of lib/jxl/coeff_order.h + coeff_order.cc — Coefficient ordering

using LibJxl.Bitstream;
using LibJxl.Entropy;

namespace LibJxl.Decoder;

/// <summary>
/// Coefficient ordering utilities — maps logical coefficient indices to
/// physical positions in the bitstream (zigzag/custom order).
/// Port of jxl/coeff_order.h.
/// </summary>
public static class CoeffOrder
{
    /// <summary>Max offset index for coefficient order tables.</summary>
    public const int kCoeffOrderLimit = 6156;

    /// <summary>Max total size = kCoeffOrderLimit × kDCTBlockSize.</summary>
    public const int kCoeffOrderMaxSize = kCoeffOrderLimit * FrameConstants.DCTBlockSize;

    /// <summary>
    /// Offset table: maps (order_bucket, channel) to coefficient order offset.
    /// kCoeffOrderOffset[3 * order + channel] × kDCTBlockSize = byte offset into order array.
    /// Port of kCoeffOrderOffset from coeff_order.h.
    /// </summary>
    public static readonly int[] kCoeffOrderOffset =
    {
        0, 1, 2, 3, 4, 5, 6, 10, 14, 18,
        34, 50, 66, 68, 70, 72, 76, 80, 84, 92,
        100, 108, 172, 236, 300, 332, 364, 396, 652, 908,
        1164, 1292, 1420, 1548, 2572, 3596, 4620, 5132, 5644, 6156,
    };

    /// <summary>
    /// Gets the offset into the coefficient order array for (order_bucket, channel).
    /// </summary>
    public static int GetOffset(int orderBucket, int channel)
    {
        return kCoeffOrderOffset[3 * orderBucket + channel] * FrameConstants.DCTBlockSize;
    }

    /// <summary>
    /// Gets the number of coefficients for (order_bucket, channel).
    /// </summary>
    public static int GetSize(int orderBucket, int channel)
    {
        int idx = 3 * orderBucket + channel;
        return (kCoeffOrderOffset[idx + 1] - kCoeffOrderOffset[idx]) * FrameConstants.DCTBlockSize;
    }

    /// <summary>
    /// Computes natural (zigzag) coefficient order for a given block size.
    /// Port of SetDefaultOrder from coeff_order.cc.
    /// </summary>
    public static void SetNaturalOrder(int sizeX, int sizeY, Span<int> order)
    {
        int n = sizeX * sizeY;
        // Simple row-major order as default
        for (int i = 0; i < n; i++)
            order[i] = i;

        // For 8×8 DCT, use standard zigzag
        if (sizeX == 8 && sizeY == 8)
        {
            SetZigzagOrder8x8(order);
        }
    }

    /// <summary>Standard 8×8 zigzag scanning order.</summary>
    private static void SetZigzagOrder8x8(Span<int> order)
    {
        int idx = 0;
        for (int s = 0; s < 15; s++)
        {
            if (s % 2 == 0)
            {
                for (int y = Math.Min(s, 7); y >= Math.Max(0, s - 7); y--)
                {
                    int x = s - y;
                    order[idx++] = y * 8 + x;
                }
            }
            else
            {
                for (int x = Math.Min(s, 7); x >= Math.Max(0, s - 7); x--)
                {
                    int y = s - x;
                    order[idx++] = y * 8 + x;
                }
            }
        }
    }

    /// <summary>
    /// Decodes coefficient orders from bitstream.
    /// Port of DecodeCoeffOrders from coeff_order.cc.
    /// Returns an array of size kCoeffOrderMaxSize.
    /// </summary>
    public static int[] DecodeCoeffOrders(ushort usedOrders, BitReader br)
    {
        var order = new int[kCoeffOrderMaxSize];

        // Initialize with natural order for all buckets
        for (int o = 0; o < AcStrategy.kNumOrders; o++)
        {
            for (int c = 0; c < 3; c++)
            {
                int offset = GetOffset(o, c);
                int size = GetSize(o, c);
                for (int i = 0; i < size; i++)
                    order[offset + i] = i;
            }
        }

        // Decode permutations for used orders
        for (int o = 0; o < AcStrategy.kNumOrders; o++)
        {
            if ((usedOrders & (1 << o)) == 0) continue;

            for (int c = 0; c < 3; c++)
            {
                int offset = GetOffset(o, c);
                int size = GetSize(o, c);
                if (size <= 1) continue;

                // Decode permutation using Lehmer codes into temp array
                int[] temp = new int[size];
                var status = PermutationDecoder.DecodePermutation(0, size, temp, br);
                if (!status) return order; // return partial on error

                // Copy permuted order back at offset
                Array.Copy(temp, 0, order, offset, size);
            }
        }

        return order;
    }
}
