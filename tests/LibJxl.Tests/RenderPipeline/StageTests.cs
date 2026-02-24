using LibJxl.RenderPipeline;
using Xunit;

namespace LibJxl.Tests.RenderPipeline;

public class StageGaborishTests
{
    [Fact]
    public void ChannelMode_FirstThreeInOut()
    {
        var stage = StageGaborish.CreateDefault();
        Assert.Equal(ChannelMode.InOut, stage.GetChannelMode(0));
        Assert.Equal(ChannelMode.InOut, stage.GetChannelMode(1));
        Assert.Equal(ChannelMode.InOut, stage.GetChannelMode(2));
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(3));
    }

    [Fact]
    public void ProcessRow_OutputNonZeroForNonZeroInput()
    {
        var stage = StageGaborish.CreateDefault();
        float[] r = { 0.5f, 0.5f, 0.5f, 0.5f };
        float[] g = { 0.3f, 0.3f, 0.3f, 0.3f };
        float[] b = { 0.1f, 0.1f, 0.1f, 0.1f };
        var input = new float[][] { r, g, b };
        var output = new float[][] { new float[4], new float[4], new float[4] };

        Assert.True(stage.ProcessRow(input, output, 4, 0, 0, 0));

        // Simplified mode just scales by center weight
        for (int i = 0; i < 4; i++)
            Assert.True(output[0][i] > 0);
    }

    [Fact]
    public void Border_IsOne()
    {
        var stage = StageGaborish.CreateDefault();
        Assert.Equal(1, stage.Settings.BorderX);
        Assert.Equal(1, stage.Settings.BorderY);
    }

    [Fact]
    public void ProcessRowWithBorder_Full3x3Convolution()
    {
        var stage = StageGaborish.CreateDefault();
        // Create 3 rows for border=1: top, center, bottom — with padding borderX=1
        // Image is 3 pixels wide, so padded rows are 5 pixels [pad|data|pad]
        int borderX = 1;
        int width = 3;
        int paddedW = width + 2 * borderX;

        // Fill with uniform 1.0 for channel 0 — output should be ~1.0
        float[] topRow  = { 1, 1, 1, 1, 1 }; // padded
        float[] midRow  = { 1, 1, 1, 1, 1 };
        float[] botRow  = { 1, 1, 1, 1, 1 };
        float[] outRow  = new float[width];

        // For channels 1 and 2, fill with 0.5
        float[] topR2 = { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
        float[] midR2 = { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
        float[] botR2 = { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };

        var inputRows = new float[][][] {
            new[] { topRow, midRow, botRow },  // channel 0: 3 rows
            new[] { topR2, midR2, botR2 },     // channel 1
            new[] { topR2, midR2, botR2 },     // channel 2
        };
        var outputRows = new float[][][] {
            new[] { outRow },
            new[] { new float[width] },
            new[] { new float[width] },
        };

        Assert.True(stage.ProcessRowWithBorder(inputRows, outputRows, width, borderX, 0, 0));

        // Uniform input -> output should equal input (normalized weights sum to 1)
        for (int x = 0; x < width; x++)
            Assert.InRange(outputRows[0][0][x], 0.99f, 1.01f);
        for (int x = 0; x < width; x++)
            Assert.InRange(outputRows[1][0][x], 0.49f, 0.51f);
    }

    [Fact]
    public void ProcessRowWithBorder_EdgeEnhancement()
    {
        // Test with a sharp edge: left half=0, right half=1
        var stage = StageGaborish.CreateDefault();
        int borderX = 1;
        int width = 4;
        // Padded: [mirror | 0 0 1 1 | mirror]
        float[] topRow = { 0, 0, 0, 1, 1, 1 };
        float[] midRow = { 0, 0, 0, 1, 1, 1 };
        float[] botRow = { 0, 0, 0, 1, 1, 1 };
        float[] outRow = new float[width];

        var inputRows = new float[][][] {
            new[] { topRow, midRow, botRow },
            new[] { topRow, midRow, botRow },
            new[] { topRow, midRow, botRow },
        };
        var outputRows = new float[][][] {
            new[] { outRow },
            new[] { new float[width] },
            new[] { new float[width] },
        };

        Assert.True(stage.ProcessRowWithBorder(inputRows, outputRows, width, borderX, 0, 0));

        // Far left should stay ~0, far right ~1
        Assert.InRange(outRow[0], -0.01f, 0.05f);
        Assert.InRange(outRow[3], 0.95f, 1.01f);
        // Middle values should be between 0 and 1 (smoothing effect)
        Assert.InRange(outRow[1], 0.0f, 0.5f);
        Assert.InRange(outRow[2], 0.5f, 1.0f);
    }
}

public class StageEpfTests
{
    [Fact]
    public void Stage0_Border3()
    {
        var stage = StageEpf.CreateStage0();
        Assert.Equal(3, stage.Settings.BorderX);
    }

    [Fact]
    public void Stage1_Border2()
    {
        var stage = StageEpf.CreateStage1();
        Assert.Equal(2, stage.Settings.BorderX);
    }

    [Fact]
    public void Stage2_Border1()
    {
        var stage = StageEpf.CreateStage2();
        Assert.Equal(1, stage.Settings.BorderX);
    }

    [Fact]
    public void ChannelMode_FirstThreeInOut()
    {
        var stage = StageEpf.CreateStage0();
        Assert.Equal(ChannelMode.InOut, stage.GetChannelMode(0));
        Assert.Equal(ChannelMode.InOut, stage.GetChannelMode(1));
        Assert.Equal(ChannelMode.InOut, stage.GetChannelMode(2));
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(3));
    }

    [Fact]
    public void ProcessRow_Passthrough()
    {
        var stage = StageEpf.CreateStage2();
        float[] r = { 1.0f, 2.0f, 3.0f };
        float[] g = { 4.0f, 5.0f, 6.0f };
        float[] b = { 7.0f, 8.0f, 9.0f };
        var input = new float[][] { r, g, b };
        var output = new float[][] { new float[3], new float[3], new float[3] };

        Assert.True(stage.ProcessRow(input, output, 3, 0, 0, 0));

        Assert.Equal(1.0f, output[0][0]);
        Assert.Equal(5.0f, output[1][1]);
        Assert.Equal(9.0f, output[2][2]);
    }

    [Fact]
    public void Epf2_WithBorder_UniformInput_Preserved()
    {
        // Uniform image -> EPF should preserve the value
        var stage = StageEpf.CreateStage2();
        stage.UniformSigma = 1.0f;
        int borderX = 1;
        int width = 3;

        float val = 0.5f;
        float[] row = new float[width + 2 * borderX];
        Array.Fill(row, val);

        var inputRows = new float[][][] {
            new[] { (float[])row.Clone(), (float[])row.Clone(), (float[])row.Clone() },
            new[] { (float[])row.Clone(), (float[])row.Clone(), (float[])row.Clone() },
            new[] { (float[])row.Clone(), (float[])row.Clone(), (float[])row.Clone() },
        };
        var outputRows = new float[][][] {
            new[] { new float[width] },
            new[] { new float[width] },
            new[] { new float[width] },
        };

        Assert.True(stage.ProcessRowWithBorder(inputRows, outputRows, width, borderX, 0, 0));

        for (int x = 0; x < width; x++)
        {
            Assert.InRange(outputRows[0][0][x], val - 0.01f, val + 0.01f);
            Assert.InRange(outputRows[1][0][x], val - 0.01f, val + 0.01f);
            Assert.InRange(outputRows[2][0][x], val - 0.01f, val + 0.01f);
        }
    }

    [Fact]
    public void Epf2_WithBorder_EdgePreserving()
    {
        // Sharp edge: left=0, right=1 — EPF should preserve the edge to some degree
        var stage = StageEpf.CreateStage2();
        stage.UniformSigma = 0.5f;
        int borderX = 1;
        int width = 4;

        // [0, 0, 0, 1, 1, 1] padded
        float[] rowLeft = { 0, 0, 0, 1, 1, 1 };

        var inputRows = new float[][][] {
            new[] { (float[])rowLeft.Clone(), (float[])rowLeft.Clone(), (float[])rowLeft.Clone() },
            new[] { (float[])rowLeft.Clone(), (float[])rowLeft.Clone(), (float[])rowLeft.Clone() },
            new[] { (float[])rowLeft.Clone(), (float[])rowLeft.Clone(), (float[])rowLeft.Clone() },
        };
        var outputRows = new float[][][] {
            new[] { new float[width] },
            new[] { new float[width] },
            new[] { new float[width] },
        };

        Assert.True(stage.ProcessRowWithBorder(inputRows, outputRows, width, borderX, 0, 0));

        // The edge should be somewhat preserved (not fully smoothed)
        Assert.InRange(outputRows[0][0][0], -0.1f, 0.3f); // left side stays low
        Assert.InRange(outputRows[0][0][3], 0.7f, 1.1f);  // right side stays high
    }
}

public class StageUpsamplingTests
{
    [Fact]
    public void Create2x_Shift1()
    {
        var stage = StageUpsampling.Create2x(0);
        Assert.Equal(1, stage.Settings.ShiftX);
        Assert.Equal(1, stage.Settings.ShiftY);
    }

    [Fact]
    public void Create4x_Shift2()
    {
        var stage = StageUpsampling.Create4x(1);
        Assert.Equal(2, stage.Settings.ShiftX);
    }

    [Fact]
    public void ChannelMode_TargetChannelInOut()
    {
        var stage = StageUpsampling.Create2x(0);
        Assert.Equal(ChannelMode.InOut, stage.GetChannelMode(0));
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(1));
    }

    [Fact]
    public void ProcessRow_ReplicatesValues()
    {
        var stage = StageUpsampling.Create2x(0);
        float[] input = { 1.0f, 2.0f, 3.0f };
        float[] output = new float[6];
        var inputRows = new float[][] { input };
        var outputRows = new float[][] { output };

        Assert.True(stage.ProcessRow(inputRows, outputRows, 3, 0, 0, 0));

        Assert.Equal(1.0f, output[0]);
        Assert.Equal(1.0f, output[1]);
        Assert.Equal(2.0f, output[2]);
        Assert.Equal(2.0f, output[3]);
        Assert.Equal(3.0f, output[4]);
        Assert.Equal(3.0f, output[5]);
    }

    [Fact]
    public void ProcessRowWithBorder_2x_DefaultKernel()
    {
        // Default kernel: center weight only (nearest-neighbor via 5x5)
        var stage = StageUpsampling.Create2x(0);
        int borderX = 2;
        int width = 3;
        int paddedW = width + 2 * borderX;

        // Fill with incrementing values: pad | 1 2 3 | pad (mirrored)
        float[] MakeRow(float[] data)
        {
            var row = new float[paddedW];
            Array.Copy(data, 0, row, borderX, data.Length);
            // mirror left
            row[1] = data[0]; row[0] = data[1];
            // mirror right
            row[borderX + width] = data[width - 1]; row[borderX + width + 1] = data[width - 2];
            return row;
        }

        float[] vals = { 1.0f, 2.0f, 3.0f };
        float[] row = MakeRow(vals);

        // 5 rows for border=2
        var inputRows = new float[][][] {
            new[] { row, row, row, row, row },
        };
        // 2 output rows for shift=1 (2x)
        var outputRows = new float[][][] {
            new[] { new float[width * 2], new float[width * 2] },
        };

        Assert.True(stage.ProcessRowWithBorder(inputRows, outputRows, width, borderX, 0, 0));

        // With default center-only kernel, output should replicate input
        for (int x = 0; x < width; x++)
        {
            for (int ox = 0; ox < 2; ox++)
            {
                int outX = x * 2 + ox;
                // All sub-pixels should equal the center pixel value
                Assert.InRange(outputRows[0][0][outX], vals[x] - 0.01f, vals[x] + 0.01f);
                Assert.InRange(outputRows[0][1][outX], vals[x] - 0.01f, vals[x] + 0.01f);
            }
        }
    }
}

public class StageNoiseTests
{
    [Fact]
    public void ChannelMode_RgbInPlace()
    {
        var stage = new StageNoise(new float[8]);
        Assert.Equal(ChannelMode.InPlace, stage.GetChannelMode(0));
        Assert.Equal(ChannelMode.InPlace, stage.GetChannelMode(1));
        Assert.Equal(ChannelMode.InPlace, stage.GetChannelMode(2));
    }

    [Fact]
    public void ChannelMode_NoiseChannelsInput()
    {
        var stage = new StageNoise(new float[8], 3);
        Assert.Equal(ChannelMode.Input, stage.GetChannelMode(3));
        Assert.Equal(ChannelMode.Input, stage.GetChannelMode(4));
        Assert.Equal(ChannelMode.Input, stage.GetChannelMode(5));
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(6));
    }

    [Fact]
    public void EvalNoiseStrength_ZeroIntensity()
    {
        float[] lut = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f };
        var stage = new StageNoise(lut);
        Assert.Equal(0.1f, stage.EvalNoiseStrength(0.0f));
    }

    [Fact]
    public void EvalNoiseStrength_Interpolation()
    {
        float[] lut = { 0.0f, 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f };
        var stage = new StageNoise(lut);
        float strength = stage.EvalNoiseStrength(0.5f / 6.0f);
        Assert.InRange(strength, 0.49f, 0.51f);
    }

    [Fact]
    public void ConvolveNoise_Border2()
    {
        var stage = new StageConvolveNoise();
        Assert.Equal(2, stage.Settings.BorderX);
    }

    [Fact]
    public void ProcessRow_WithNoiseChannels_ModifiesOutput()
    {
        float[] lut = { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
        var stage = new StageNoise(lut, 3);

        // Create 6 channels: 3 image + 3 noise
        float[] rx = { 0.5f, 0.5f };
        float[] ry = { 0.5f, 0.5f };
        float[] rb = { 0.5f, 0.5f };
        float[] n0 = { 1.0f, 1.0f }; // noise R
        float[] n1 = { 1.0f, 1.0f }; // noise G
        float[] n2 = { 1.0f, 1.0f }; // noise C (correlated)
        var input = new float[][] { rx, ry, rb, n0, n1, n2 };
        var output = new float[][] { rx, ry, rb, n0, n1, n2 }; // in-place

        float origY0 = ry[0];
        Assert.True(stage.ProcessRow(input, output, 2, 0, 0, 0));

        // With noise channels, the Y channel should be modified (noise adds to Y)
        Assert.NotEqual(origY0, ry[0]); // Y channel receives rgNoise
    }

    [Fact]
    public void ConvolveNoise_WithBorder_LaplacianFilter()
    {
        // Test the 5×5 convolve noise with uniform input -> output should be ~0
        var stage = new StageConvolveNoise(3);
        int borderX = 2;
        int width = 3;
        int paddedW = width + 2 * borderX;

        // All 1.0 input -> center*(-3.84) + 24 neighbors * 0.16 = -3.84 + 3.84 = 0
        float[] row = new float[paddedW];
        Array.Fill(row, 1.0f);

        // Channel 3 with 5 rows
        var empty = new float[][][] {
            new[] { new float[0] }, // ch0
            new[] { new float[0] }, // ch1
            new[] { new float[0] }, // ch2
            new[] { row, row, row, row, row }, // ch3 noise
            new[] { row, row, row, row, row }, // ch4 noise
            new[] { row, row, row, row, row }, // ch5 noise
        };
        var outputRows = new float[][][] {
            new[] { new float[0] },
            new[] { new float[0] },
            new[] { new float[0] },
            new[] { new float[width] },
            new[] { new float[width] },
            new[] { new float[width] },
        };

        Assert.True(stage.ProcessRowWithBorder(empty, outputRows, width, borderX, 0, 0));

        // Uniform input: center*kCenter + 24*kOther = 1*(-3.84) + 24*1*0.16 = 0
        for (int x = 0; x < width; x++)
            Assert.InRange(outputRows[3][0][x], -0.01f, 0.01f);
    }
}

public class SimpleRenderPipelineIntegrationTests
{
    [Fact]
    public void Pipeline_GaborishStage_ProcessesWith3x3Border()
    {
        // Run Gaborish through the full pipeline with border mirroring
        var pipeline = new SimpleRenderPipeline();
        pipeline.AllocateBuffers(4, 4, 3);

        // Fill with known values
        for (int c = 0; c < 3; c++)
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            pipeline.ChannelData![c][y][x] = 0.5f;

        pipeline.AddStage(StageGaborish.CreateDefault());
        Assert.True(pipeline.ProcessGroup(0, 0));

        // Uniform input -> uniform output through Gaborish
        // Output dims should be 4×4 (InOut with shift=0)
        Assert.Equal(4, pipeline.Width);
        Assert.Equal(4, pipeline.Height);
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            Assert.InRange(pipeline.ChannelData![0][y][x], 0.49f, 0.51f);
    }

    [Fact]
    public void Pipeline_Epf2Stage_PreservesUniform()
    {
        var pipeline = new SimpleRenderPipeline();
        pipeline.AllocateBuffers(4, 4, 3);

        float val = 0.7f;
        for (int c = 0; c < 3; c++)
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            pipeline.ChannelData![c][y][x] = val;

        var epf = StageEpf.CreateStage2();
        epf.UniformSigma = 1.0f;
        pipeline.AddStage(epf);
        Assert.True(pipeline.ProcessGroup(0, 0));

        Assert.Equal(4, pipeline.Width);
        Assert.Equal(4, pipeline.Height);
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            Assert.InRange(pipeline.ChannelData![0][y][x], val - 0.01f, val + 0.01f);
    }

    [Fact]
    public void Pipeline_UpsamplingStage_DoublesSize()
    {
        var pipeline = new SimpleRenderPipeline();
        pipeline.AllocateBuffers(4, 4, 1);

        // Fill with incrementing values
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            pipeline.ChannelData![0][y][x] = (y * 4 + x) * 0.1f;

        pipeline.AddStage(StageUpsampling.Create2x(0));
        Assert.True(pipeline.ProcessGroup(0, 0));

        // Output should be 8×8 (2× upsampled)
        Assert.Equal(8, pipeline.Width);
        Assert.Equal(8, pipeline.Height);

        // With default nearest-neighbor kernel, check center pixel replication
        // Input[0][0] = 0.0 -> output[0][0] and [0][1] and [1][0] and [1][1] should all be ~0.0
        Assert.InRange(pipeline.ChannelData![0][0][0], -0.01f, 0.01f);
    }

    [Fact]
    public void Pipeline_InPlaceStage_NoBorderIssues()
    {
        // Test that in-place stages (no border) work correctly with new pipeline
        var pipeline = new SimpleRenderPipeline();
        pipeline.AllocateBuffers(3, 3, 3);

        for (int c = 0; c < 3; c++)
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
            pipeline.ChannelData![c][y][x] = 1.0f;

        pipeline.AddStage(new StageFromLinear());
        Assert.True(pipeline.ProcessGroup(0, 0));

        // sRGB of 1.0 linear = 1.0
        Assert.Equal(3, pipeline.Width);
        Assert.Equal(3, pipeline.Height);
        Assert.InRange(pipeline.ChannelData![0][0][0], 0.99f, 1.01f);
    }
}
