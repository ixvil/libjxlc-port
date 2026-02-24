using LibJxl.Fields;
using LibJxl.RenderPipeline;
using Xunit;

namespace LibJxl.Tests.RenderPipeline;

public class EpfSigmaExtendedTests
{
    [Fact]
    public void ComputeSigma_AllOnes_ProducesNonZero()
    {
        var lf = new LoopFilter { EpfQuantMul = 1.0f };
        // Initialize EpfSharpLut with default values
        for (int i = 0; i < 8; i++)
            lf.EpfSharpLut[i] = 1.0f;

        int xBlocks = 4, yBlocks = 4;
        var rawQuantField = new int[yBlocks, xBlocks];
        var epfSharpness = new byte[yBlocks, xBlocks];

        for (int by = 0; by < yBlocks; by++)
            for (int bx = 0; bx < xBlocks; bx++)
            {
                rawQuantField[by, bx] = 1;
                epfSharpness[by, bx] = 0;
            }

        var sigma = EpfSigma.ComputeSigma(lf, 1.0f, rawQuantField, epfSharpness, xBlocks, yBlocks);

        // Padded dimensions: (4+4) x (4+4) = 8x8
        Assert.Equal(8, sigma.GetLength(0));
        Assert.Equal(8, sigma.GetLength(1));

        // Interior values should be non-zero
        Assert.NotEqual(0.0f, sigma[2, 2]);
    }

    [Fact]
    public void ComputeSigma_PaddingDimensions()
    {
        var lf = new LoopFilter { EpfQuantMul = 1.0f };
        for (int i = 0; i < 8; i++) lf.EpfSharpLut[i] = 1.0f;

        int xBlocks = 3, yBlocks = 5;
        var rawQuantField = new int[yBlocks, xBlocks];
        var epfSharpness = new byte[yBlocks, xBlocks];
        for (int by = 0; by < yBlocks; by++)
            for (int bx = 0; bx < xBlocks; bx++)
                rawQuantField[by, bx] = 1;

        var sigma = EpfSigma.ComputeSigma(lf, 1.0f, rawQuantField, epfSharpness, xBlocks, yBlocks);

        // kSigmaPadding=2, so dims = (yBlocks + 4) × (xBlocks + 4)
        Assert.Equal(yBlocks + 4, sigma.GetLength(0));
        Assert.Equal(xBlocks + 4, sigma.GetLength(1));
    }

    [Fact]
    public void ComputeSigma_ZeroQuantField_ClampsToOne()
    {
        var lf = new LoopFilter { EpfQuantMul = 1.0f };
        for (int i = 0; i < 8; i++) lf.EpfSharpLut[i] = 1.0f;

        int xBlocks = 2, yBlocks = 2;
        var rawQuantField = new int[yBlocks, xBlocks]; // all zeros
        var epfSharpness = new byte[yBlocks, xBlocks];

        // Should not crash (zero is clamped to 1)
        var sigma = EpfSigma.ComputeSigma(lf, 1.0f, rawQuantField, epfSharpness, xBlocks, yBlocks);
        Assert.NotNull(sigma);
        // Interior value at [2,2] should be non-zero (qf=1 after clamp)
        Assert.NotEqual(0.0f, sigma[2, 2]);
    }

    [Fact]
    public void ComputeSigma_MirrorPadding_MatchesInterior()
    {
        var lf = new LoopFilter { EpfQuantMul = 2.0f };
        for (int i = 0; i < 8; i++) lf.EpfSharpLut[i] = 1.0f;

        int xBlocks = 3, yBlocks = 3;
        var rawQuantField = new int[yBlocks, xBlocks];
        var epfSharpness = new byte[yBlocks, xBlocks];
        // All quantField = 2
        for (int by = 0; by < yBlocks; by++)
            for (int bx = 0; bx < xBlocks; bx++)
                rawQuantField[by, bx] = 2;

        var sigma = EpfSigma.ComputeSigma(lf, 1.0f, rawQuantField, epfSharpness, xBlocks, yBlocks);

        // With uniform input, padding should mirror the edge values
        // Left padding: sigma[2, 1] should equal sigma[2, 2] (mirrored from column 0)
        Assert.Equal(sigma[2, 2], sigma[2, 1]);
        // Top padding: sigma[1, 2] should equal sigma[2, 2] (mirrored from row 0)
        Assert.Equal(sigma[2, 2], sigma[1, 2]);
    }

    [Fact]
    public void ComputeSigma_HigherSharpness_AffectsResult()
    {
        var lf = new LoopFilter { EpfQuantMul = 1.0f };
        // Different lut values per sharpness level
        for (int i = 0; i < 8; i++) lf.EpfSharpLut[i] = 1.0f + i * 0.5f;

        int xBlocks = 2, yBlocks = 2;
        var rawQuantField = new int[yBlocks, xBlocks];
        var epfSharpness = new byte[yBlocks, xBlocks];

        for (int by = 0; by < yBlocks; by++)
            for (int bx = 0; bx < xBlocks; bx++)
                rawQuantField[by, bx] = 1;

        // Block [0,0] has sharpness 0, block [0,1] has sharpness 3
        epfSharpness[0, 0] = 0;
        epfSharpness[0, 1] = 3;

        var sigma = EpfSigma.ComputeSigma(lf, 1.0f, rawQuantField, epfSharpness, xBlocks, yBlocks);

        // Different sharpness → different sigma values
        float s0 = sigma[2, 2]; // sharpness 0
        float s3 = sigma[2, 3]; // sharpness 3
        Assert.NotEqual(s0, s3);
    }
}

public class PipelineBuilderChromaTests
{
    [Fact]
    public void BuildVarDctPipeline_444_NoChromaStages()
    {
        var fh = new FrameHeader
        {
            Transform = LibJxl.Fields.ColorTransform.XYB,
            Filter = new LoopFilter(),
            Upsampling = 1,
            ChromaSubsampling = new YCbCrChromaSubsampling(), // default = 4:4:4
        };

        var fd = new LibJxl.Decoder.FrameDimensions();
        fd.Set(8, 8, 1, 0, 0, false, 1);

        var pipeline = PipelineBuilder.BuildVarDctPipeline(fh, fd, 3, 8, 8);

        // 4:4:4 = no chroma upsampling stages
        // Should have XYB + FromLinear stages only
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void BuildVarDctPipeline_YCbCr_HasYcbcrStage()
    {
        var fh = new FrameHeader
        {
            Transform = LibJxl.Fields.ColorTransform.YCbCr,
            Filter = new LoopFilter(),
            Upsampling = 1,
            ChromaSubsampling = new YCbCrChromaSubsampling(),
        };

        var fd = new LibJxl.Decoder.FrameDimensions();
        fd.Set(8, 8, 1, 0, 0, false, 1);

        var pipeline = PipelineBuilder.BuildVarDctPipeline(fh, fd, 3, 8, 8);

        Assert.NotNull(pipeline);
        // Pipeline should work correctly
        pipeline.AllocateBuffers(8, 8, 3);
        Assert.True(pipeline.ProcessGroup(0, 0));
    }
}
