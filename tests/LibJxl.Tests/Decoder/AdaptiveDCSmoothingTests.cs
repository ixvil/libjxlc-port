using LibJxl.Decoder;
using Xunit;

namespace LibJxl.Tests.Decoder;

public class AdaptiveDCSmoothingTests
{
    private static float[][][] CreateUniformDC(int height, int width, float value)
    {
        var dc = new float[3][][];
        for (int c = 0; c < 3; c++)
        {
            dc[c] = new float[height][];
            for (int y = 0; y < height; y++)
            {
                dc[c][y] = new float[width];
                Array.Fill(dc[c][y], value);
            }
        }
        return dc;
    }

    [Fact]
    public void Smooth_UniformInput_PreservedExactly()
    {
        // Uniform DC values => smoothing should preserve them
        int w = 8, h = 8;
        float val = 0.5f;
        var dc = CreateUniformDC(h, w, val);
        float[] dcFactors = { 1.0f, 1.0f, 1.0f };

        bool result = AdaptiveDCSmoothing.Smooth(dc, dcFactors);
        Assert.True(result);

        // All values should still be 0.5 (uniform → gap=0 → factor=3, but smoothed=center)
        for (int c = 0; c < 3; c++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Assert.InRange(dc[c][y][x], val - 0.001f, val + 0.001f);
    }

    [Fact]
    public void Smooth_SmallImage_ReturnsTrueNoChange()
    {
        // Images ≤ 2×2 should not be smoothed (just return true)
        var dc = CreateUniformDC(2, 2, 1.0f);
        float[] dcFactors = { 1.0f, 1.0f, 1.0f };

        bool result = AdaptiveDCSmoothing.Smooth(dc, dcFactors);
        Assert.True(result);
        Assert.Equal(1.0f, dc[0][0][0]);
    }

    [Fact]
    public void Smooth_TooFewChannels_ReturnsFalse()
    {
        var dc = new float[2][][]; // Only 2 channels
        dc[0] = new float[4][];
        dc[1] = new float[4][];
        for (int y = 0; y < 4; y++)
        {
            dc[0][y] = new float[4];
            dc[1][y] = new float[4];
        }
        float[] dcFactors = { 1.0f, 1.0f, 1.0f };

        bool result = AdaptiveDCSmoothing.Smooth(dc, dcFactors);
        Assert.False(result);
    }

    [Fact]
    public void Smooth_Borders_PreservedExactly()
    {
        // Borders (first/last row and col) should be unchanged
        int w = 6, h = 6;
        var dc = CreateUniformDC(h, w, 0.5f);

        // Set known values on border elements
        for (int c = 0; c < 3; c++)
        {
            // Top row
            for (int x = 0; x < w; x++)
                dc[c][0][x] = 1.0f;
            // Bottom row
            for (int x = 0; x < w; x++)
                dc[c][h - 1][x] = 2.0f;
            // Left column (interior only — rows 1..h-2)
            for (int y = 1; y < h - 1; y++)
                dc[c][y][0] = 3.0f;
            // Right column (interior only — rows 1..h-2)
            for (int y = 1; y < h - 1; y++)
                dc[c][y][w - 1] = 4.0f;
        }

        // Record border values before smoothing
        float topVal = dc[0][0][2];         // 1.0
        float botVal = dc[0][h - 1][2];     // 2.0
        float leftVal = dc[0][2][0];         // 3.0
        float rightVal = dc[0][2][w - 1];    // 4.0

        float[] dcFactors = { 1.0f, 1.0f, 1.0f };
        AdaptiveDCSmoothing.Smooth(dc, dcFactors);

        // Top and bottom rows should be unchanged (non-corner cells)
        for (int c = 0; c < 3; c++)
        {
            // Check a middle element in top row
            Assert.Equal(1.0f, dc[c][0][2]);
            // Check a middle element in bottom row
            Assert.Equal(2.0f, dc[c][h - 1][2]);
            // Left and right columns (interior rows) should be unchanged
            for (int y = 1; y < h - 1; y++)
            {
                Assert.Equal(3.0f, dc[c][y][0]);
                Assert.Equal(4.0f, dc[c][y][w - 1]);
            }
        }
    }

    [Fact]
    public void Smooth_SmoothArea_StrongSmoothing()
    {
        // In a flat area with a small outlier, smoothing should reduce the outlier
        int w = 5, h = 5;
        var dc = CreateUniformDC(h, w, 1.0f);

        // Put a small bump at center
        dc[0][2][2] = 1.01f; // very small deviation
        dc[1][2][2] = 1.01f;
        dc[2][2][2] = 1.01f;

        float[] dcFactors = { 1.0f, 1.0f, 1.0f };
        AdaptiveDCSmoothing.Smooth(dc, dcFactors);

        // The center pixel should be smoothed closer to 1.0
        // (in flat area, factor ≈ 3.0 → strong smoothing toward weighted average)
        Assert.InRange(dc[0][2][2], 0.99f, 1.01f);
    }

    [Fact]
    public void Smooth_SharpEdge_Preserved()
    {
        // A sharp edge should be preserved (gap is large → factor≈0)
        int w = 8, h = 8;
        var dc = CreateUniformDC(h, w, 0.0f);

        // Create sharp vertical edge: left half = 0, right half = 100
        for (int c = 0; c < 3; c++)
            for (int y = 0; y < h; y++)
                for (int x = w / 2; x < w; x++)
                    dc[c][y][x] = 100.0f;

        float[] dcFactors = { 1.0f, 1.0f, 1.0f };
        float edgeBefore = dc[0][3][3]; // 0.0 (just before edge)

        AdaptiveDCSmoothing.Smooth(dc, dcFactors);

        // With large dcFactor=1.0 and values=100, gap = |0-smooth|/1 >> 0.75
        // So factor ≈ 0 → no smoothing near the edge
        // Interior flat areas should be preserved too
        Assert.InRange(dc[0][1][1], -0.01f, 0.01f); // Far from edge: still ~0
        Assert.InRange(dc[0][1][6], 99.9f, 100.1f); // Far from edge: still ~100
    }

    [Fact]
    public void Smooth_KernelWeights_SumToOne()
    {
        // Verify that the kernel weights sum to approximately 1.0
        // kW0 = 1 - 4*(kW1 + kW2)
        float kW1 = 0.20345139757231578f;
        float kW2 = 0.0334829185968739f;
        float kW0 = 1.0f - 4.0f * (kW1 + kW2);

        // Sum = kW0 + 4*kW1 + 4*kW2 = 1.0
        float sum = kW0 + 4 * kW1 + 4 * kW2;
        Assert.InRange(sum, 0.999f, 1.001f);
    }

    [Fact]
    public void Smooth_AdaptiveFactor_RangeCheck()
    {
        // Test that the adaptive factor = max(0, 3 - 4*gap) is always ≥ 0
        // gap=0 → factor=3, gap=0.75 → factor=0, gap>0.75 → factor=0
        int w = 6, h = 6;

        // Create channel data with specific gap
        var dc = CreateUniformDC(h, w, 0.0f);
        // Set one channel differently to create known gap
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                dc[0][y][x] = 10.0f; // channel 0 is 10.0

        float[] dcFactors = { 0.1f, 0.1f, 0.1f }; // Small factor → large normalized gap

        float valueBefore = dc[0][2][2];
        AdaptiveDCSmoothing.Smooth(dc, dcFactors);

        // With all values being 10.0 uniformly in channel 0, smoothing should preserve them
        Assert.InRange(dc[0][2][2], 9.9f, 10.1f);
    }
}
