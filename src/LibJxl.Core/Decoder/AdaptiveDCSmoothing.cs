// Port of lib/jxl/compressed_dc.cc — AdaptiveDCSmoothing
// Reduces banding artifacts in smooth DC areas using adaptive 3×3 convolution.

namespace LibJxl.Decoder;

/// <summary>
/// Adaptive DC smoothing filter.
/// Port of jxl::AdaptiveDCSmoothing from compressed_dc.cc.
///
/// Applies a 3×3 weighted smoothing kernel to DC values, with an adaptive
/// blending factor based on local variation. Smooth areas get more smoothing,
/// edges/transitions are preserved.
/// </summary>
public static class AdaptiveDCSmoothing
{
    // 3×3 smoothing kernel weights (sum to 1.0)
    private const float kW1 = 0.20345139757231578f; // side (N, S, E, W)
    private const float kW2 = 0.0334829185968739f;  // corner (NE, NW, SE, SW)
    private const float kW0 = 1.0f - 4.0f * (kW1 + kW2); // center ≈ 0.4570

    /// <summary>
    /// Applies adaptive DC smoothing in-place on a 3-channel DC image.
    /// </summary>
    /// <param name="dc">DC image [3 channels][height][width], modified in-place.</param>
    /// <param name="dcFactors">DC quantization factors per channel (3 values).</param>
    /// <returns>True on success.</returns>
    public static bool Smooth(float[][][] dc, float[] dcFactors)
    {
        if (dc.Length < 3) return false;

        int height = dc[0].Length;
        int width = height > 0 ? dc[0][0].Length : 0;

        // Need at least 3×3 to apply the filter
        if (height <= 2 || width <= 2) return true;

        // Create output buffer (we'll swap at the end)
        var smoothed = new float[3][][];
        for (int c = 0; c < 3; c++)
        {
            smoothed[c] = new float[height][];
            for (int y = 0; y < height; y++)
            {
                smoothed[c][y] = new float[width];
            }
        }

        // Copy borders (first/last row, first/last column) unchanged
        for (int c = 0; c < 3; c++)
        {
            Array.Copy(dc[c][0], smoothed[c][0], width);
            Array.Copy(dc[c][height - 1], smoothed[c][height - 1], width);
            for (int y = 1; y < height - 1; y++)
            {
                smoothed[c][y][0] = dc[c][y][0];
                smoothed[c][y][width - 1] = dc[c][y][width - 1];
            }
        }

        // Process interior pixels (rows 1..height-2, cols 1..width-2)
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                ComputePixel(dc, smoothed, dcFactors, x, y);
            }
        }

        // Swap: replace dc with smoothed
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < height; y++)
            {
                dc[c][y] = smoothed[c][y];
            }
        }

        return true;
    }

    /// <summary>
    /// Computes the smoothed value for a single pixel across all 3 channels.
    /// Uses the adaptive factor: factor = max(0, 3.0 - 4.0 * gap)
    /// where gap = max over channels of |center - smoothed| / dcFactor.
    /// </summary>
    private static void ComputePixel(float[][][] dc, float[][][] output,
        float[] dcFactors, int x, int y)
    {
        // Compute smoothed values and gap across all 3 channels
        float gap = 0.5f; // initial gap value
        Span<float> mc = stackalloc float[3];
        Span<float> sm = stackalloc float[3];

        for (int c = 0; c < 3; c++)
        {
            float[] rowT = dc[c][y - 1]; // top row
            float[] rowM = dc[c][y];     // middle row
            float[] rowB = dc[c][y + 1]; // bottom row

            // Center value
            mc[c] = rowM[x];

            // 3×3 weighted average
            float corners = rowT[x - 1] + rowT[x + 1] + rowB[x - 1] + rowB[x + 1];
            float sides = rowT[x] + rowB[x] + rowM[x - 1] + rowM[x + 1];
            float center = rowM[x];

            sm[c] = kW2 * corners + kW1 * sides + kW0 * center;

            // Compute normalized gap
            float dcFactor = dcFactors[c];
            if (dcFactor > 0)
            {
                float diff = MathF.Abs(mc[c] - sm[c]) / dcFactor;
                if (diff > gap) gap = diff;
            }
        }

        // Adaptive factor: strong smoothing in flat areas, none at edges
        float factor = MathF.Max(0.0f, 3.0f - 4.0f * gap);

        // Interpolate: output = center + factor * (smoothed - center)
        for (int c = 0; c < 3; c++)
        {
            output[c][y][x] = mc[c] + factor * (sm[c] - mc[c]);
        }
    }
}
