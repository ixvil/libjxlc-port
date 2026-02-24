// Port of lib/jxl/dct-inl.h — Inverse DCT transforms (scalar implementation)

namespace LibJxl.Decoder;

/// <summary>
/// Inverse DCT transforms for all block sizes used in JXL VarDCT.
/// Port of jxl::idct (scalar fallback path) from dct-inl.h.
/// </summary>
public static class InverseDct
{
    private const float kSqrt2 = 1.41421356237f;
    private const float kSqrt0_5 = 0.70710678118f;

    // DCT-II cosine basis constants
    private const float C1_8 = 0.98078528040323043f; // cos(1π/16)
    private const float S1_8 = 0.19509032201612825f; // sin(1π/16)
    private const float C3_8 = 0.83146961230254524f; // cos(3π/16)
    private const float S3_8 = 0.55557023301960218f; // sin(3π/16)
    private const float C1_4 = 0.70710678118654752f; // cos(π/4) = 1/√2
    private const float C1_16 = 0.99518472667219693f;
    private const float S1_16 = 0.09801714032956060f;
    private const float C3_16 = 0.95694033573220894f;
    private const float S3_16 = 0.29028467725446233f;
    private const float C5_16 = 0.88192126434835505f;
    private const float S5_16 = 0.47139673682599764f;
    private const float C7_16 = 0.77301045336273699f;
    private const float S7_16 = 0.63439328416364549f;

    /// <summary>
    /// Dispatches inverse DCT based on AC strategy type.
    /// Converts DCT coefficients to pixels in-place.
    /// </summary>
    public static void TransformToPixels(AcStrategyType strategy, float[] coeffs, int coeffOffset,
        float[] pixels, int pixelOffset, int pixelStride)
    {
        int idx = (int)strategy;
        int bx = AcStrategy.CoveredBlocksX[idx];
        int by = AcStrategy.CoveredBlocksY[idx];
        int sizeX = bx * 8;
        int sizeY = by * 8;

        switch (strategy)
        {
            case AcStrategyType.DCT:
                IDCT8x8(coeffs, coeffOffset, pixels, pixelOffset, pixelStride);
                break;

            case AcStrategyType.IDENTITY:
                IdentityTransform(coeffs, coeffOffset, pixels, pixelOffset, pixelStride);
                break;

            case AcStrategyType.DCT2X2:
                DCT2x2Transform(coeffs, coeffOffset, pixels, pixelOffset, pixelStride);
                break;

            case AcStrategyType.DCT4X4:
                IDCT4x4In8x8(coeffs, coeffOffset, pixels, pixelOffset, pixelStride);
                break;

            default:
                // For larger transforms, use generic IDCT
                GenericIDCT(coeffs, coeffOffset, pixels, pixelOffset, pixelStride, sizeX, sizeY);
                break;
        }
    }

    /// <summary>
    /// 8×8 inverse DCT (Type-II).
    /// Currently uses generic O(N²) implementation for correctness.
    /// TODO: optimize with butterfly-based fast IDCT.
    /// </summary>
    public static void IDCT8x8(float[] coeffs, int cOff, float[] pixels, int pOff, int stride)
    {
        GenericIDCT(coeffs, cOff, pixels, pOff, stride, 8, 8);
    }

    /// <summary>
    /// 1D 8-point IDCT (row operation: input stride=1, output stored transposed).
    /// </summary>
    private static void IDCT8_1D(float[] input, int inOff, float[] output, int outCol, int outStride)
    {
        float v0 = input[inOff + 0];
        float v1 = input[inOff + 1];
        float v2 = input[inOff + 2];
        float v3 = input[inOff + 3];
        float v4 = input[inOff + 4];
        float v5 = input[inOff + 5];
        float v6 = input[inOff + 6];
        float v7 = input[inOff + 7];

        // Stage 1: Butterfly
        float t0 = v0 + v4;
        float t1 = v0 - v4;
        float t2 = v2 * C1_4 + v6 * C1_4;  // Actually needs proper rotation
        float t3 = v2 * C1_4 - v6 * C1_4;

        // Use proper 8-pt IDCT via even/odd decomposition
        // Even part
        float e0 = v0;
        float e1 = v4;
        float e2 = v2;
        float e3 = v6;

        float a0 = e0 + e1;
        float a1 = e0 - e1;
        float a2 = e2 * C3_8 + e3 * S3_8;  // rotation
        float a3 = e2 * S3_8 - e3 * C3_8;

        float ee0 = a0 + a2;
        float ee1 = a1 + a3;
        float ee2 = a1 - a3;
        float ee3 = a0 - a2;

        // Odd part
        float o0 = v1, o1 = v3, o2 = v5, o3 = v7;

        float b0 = o0 * C1_8 + o3 * S1_8;
        float b1 = o1 * C3_8 + o2 * S3_8;
        float b2 = o1 * S3_8 - o2 * C3_8;
        float b3 = o0 * S1_8 - o3 * C1_8;

        float oo0 = b0 + b1;
        float oo1 = b3 + b2;
        float oo2 = b0 - b1;
        float oo3 = b3 - b2;

        float c2 = (oo2 + oo3) * C1_4;
        float c3 = (oo2 - oo3) * C1_4;

        // Combine
        output[0 * outStride + outCol] = ee0 + oo0;
        output[1 * outStride + outCol] = ee1 + c2;
        output[2 * outStride + outCol] = ee2 + oo1;
        output[3 * outStride + outCol] = ee3 + c3;
        output[4 * outStride + outCol] = ee3 - c3;
        output[5 * outStride + outCol] = ee2 - oo1;
        output[6 * outStride + outCol] = ee1 - c2;
        output[7 * outStride + outCol] = ee0 - oo0;
    }

    /// <summary>1D 8-pt IDCT for columns.</summary>
    private static void IDCT8_1DCol(float[] input, int inCol, int inStride,
        float[] output, int outOff, int outStride)
    {
        // Read column from transposed tmp
        float[] col = new float[8];
        for (int i = 0; i < 8; i++)
            col[i] = input[i * inStride + inCol];

        // Apply same 1D IDCT
        float e0 = col[0], e1 = col[4], e2 = col[2], e3 = col[6];
        float o0 = col[1], o1 = col[3], o2 = col[5], o3 = col[7];

        float a0 = e0 + e1;
        float a1 = e0 - e1;
        float a2 = e2 * C3_8 + e3 * S3_8;
        float a3 = e2 * S3_8 - e3 * C3_8;

        float ee0 = a0 + a2;
        float ee1 = a1 + a3;
        float ee2 = a1 - a3;
        float ee3 = a0 - a2;

        float b0 = o0 * C1_8 + o3 * S1_8;
        float b1 = o1 * C3_8 + o2 * S3_8;
        float b2 = o1 * S3_8 - o2 * C3_8;
        float b3 = o0 * S1_8 - o3 * C1_8;

        float oo0 = b0 + b1;
        float oo1 = b3 + b2;
        float oo2 = b0 - b1;
        float oo3 = b3 - b2;

        float c2 = (oo2 + oo3) * C1_4;
        float c3 = (oo2 - oo3) * C1_4;

        output[outOff + 0 * outStride] = (ee0 + oo0) * 0.125f;
        output[outOff + 1 * outStride] = (ee1 + c2) * 0.125f;
        output[outOff + 2 * outStride] = (ee2 + oo1) * 0.125f;
        output[outOff + 3 * outStride] = (ee3 + c3) * 0.125f;
        output[outOff + 4 * outStride] = (ee3 - c3) * 0.125f;
        output[outOff + 5 * outStride] = (ee2 - oo1) * 0.125f;
        output[outOff + 6 * outStride] = (ee1 - c2) * 0.125f;
        output[outOff + 7 * outStride] = (ee0 - oo0) * 0.125f;
    }

    /// <summary>Identity transform: pixels = coefficients (scaled).</summary>
    private static void IdentityTransform(float[] coeffs, int cOff,
        float[] pixels, int pOff, int stride)
    {
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                pixels[pOff + y * stride + x] = coeffs[cOff + y * 8 + x];
    }

    /// <summary>2×2 DCT transform in 8×8 block.</summary>
    private static void DCT2x2Transform(float[] coeffs, int cOff,
        float[] pixels, int pOff, int stride)
    {
        // DCT2x2: each 2×2 region shares one coefficient
        for (int by = 0; by < 4; by++)
            for (int bx = 0; bx < 4; bx++)
            {
                float v = coeffs[cOff + by * 8 + bx];
                pixels[pOff + (by * 2 + 0) * stride + bx * 2 + 0] = v;
                pixels[pOff + (by * 2 + 0) * stride + bx * 2 + 1] = v;
                pixels[pOff + (by * 2 + 1) * stride + bx * 2 + 0] = v;
                pixels[pOff + (by * 2 + 1) * stride + bx * 2 + 1] = v;
            }
    }

    /// <summary>4×4 IDCT inside an 8×8 block (four 4×4 transforms).</summary>
    private static void IDCT4x4In8x8(float[] coeffs, int cOff,
        float[] pixels, int pOff, int stride)
    {
        // Simplified: apply 4×4 IDCT to four quadrants
        for (int qy = 0; qy < 2; qy++)
            for (int qx = 0; qx < 2; qx++)
            {
                int qcOff = cOff + qy * 4 * 8 + qx * 4;
                int qpOff = pOff + qy * 4 * stride + qx * 4;
                IDCT4x4(coeffs, qcOff, 8, pixels, qpOff, stride);
            }
    }

    /// <summary>4×4 inverse DCT.</summary>
    private static void IDCT4x4(float[] coeffs, int cOff, int cStride,
        float[] pixels, int pOff, int pStride)
    {
        float[] tmp = new float[16];

        // Rows
        for (int y = 0; y < 4; y++)
        {
            float a = coeffs[cOff + y * cStride + 0];
            float b = coeffs[cOff + y * cStride + 1];
            float c = coeffs[cOff + y * cStride + 2];
            float d = coeffs[cOff + y * cStride + 3];

            float e0 = a + c;
            float e1 = a - c;
            float o0 = b * C1_4 + d * C1_4;
            float o1 = b * C1_4 - d * C1_4;

            tmp[y + 0 * 4] = e0 + o0;
            tmp[y + 1 * 4] = e1 + o1;
            tmp[y + 2 * 4] = e1 - o1;
            tmp[y + 3 * 4] = e0 - o0;
        }

        // Columns
        for (int x = 0; x < 4; x++)
        {
            float a = tmp[x * 4 + 0];
            float b = tmp[x * 4 + 1];
            float c = tmp[x * 4 + 2];
            float d = tmp[x * 4 + 3];

            float e0 = a + c;
            float e1 = a - c;
            float o0 = b * C1_4 + d * C1_4;
            float o1 = b * C1_4 - d * C1_4;

            pixels[pOff + 0 * pStride + x] = (e0 + o0) * 0.25f;
            pixels[pOff + 1 * pStride + x] = (e1 + o1) * 0.25f;
            pixels[pOff + 2 * pStride + x] = (e1 - o1) * 0.25f;
            pixels[pOff + 3 * pStride + x] = (e0 - o0) * 0.25f;
        }
    }

    /// <summary>
    /// Generic separable IDCT for arbitrary sizes.
    /// Uses naive O(N²) implementation per dimension.
    /// </summary>
    private static void GenericIDCT(float[] coeffs, int cOff,
        float[] pixels, int pOff, int pStride, int sizeX, int sizeY)
    {
        int n = sizeX * sizeY;
        float[] tmp = new float[n];

        // IDCT on rows
        for (int y = 0; y < sizeY; y++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                float sum = 0;
                for (int k = 0; k < sizeX; k++)
                {
                    float basis = MathF.Cos(MathF.PI * (2 * x + 1) * k / (2.0f * sizeX));
                    if (k == 0) basis *= kSqrt0_5;
                    sum += coeffs[cOff + y * sizeX + k] * basis;
                }
                tmp[y * sizeX + x] = sum * MathF.Sqrt(2.0f / sizeX);
            }
        }

        // IDCT on columns
        for (int x = 0; x < sizeX; x++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                float sum = 0;
                for (int k = 0; k < sizeY; k++)
                {
                    float basis = MathF.Cos(MathF.PI * (2 * y + 1) * k / (2.0f * sizeY));
                    if (k == 0) basis *= kSqrt0_5;
                    sum += tmp[k * sizeX + x] * basis;
                }
                pixels[pOff + y * pStride + x] = sum * MathF.Sqrt(2.0f / sizeY);
            }
        }
    }
}
