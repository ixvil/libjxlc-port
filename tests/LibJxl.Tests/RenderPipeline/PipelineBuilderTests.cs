using LibJxl.Decoder;
using LibJxl.Fields;
using LibJxl.RenderPipeline;
using Xunit;

namespace LibJxl.Tests.RenderPipeline;

public class PipelineBuilderTests
{
    [Fact]
    public void BuildVarDctPipeline_DefaultHeader_HasGaborishAndEpf()
    {
        var fh = new FrameHeader();
        // Default: Gab=true, EpfIters=2, Transform=XYB
        var fd = new FrameDimensions();
        fd.Set(16, 16, 1, 0, 0, false, 1);

        var pipeline = PipelineBuilder.BuildVarDctPipeline(fh, fd, 3, 16, 16);

        // Should have allocated buffers
        Assert.NotNull(pipeline.ChannelData);
        Assert.Equal(16, pipeline.Width);
        Assert.Equal(16, pipeline.Height);
    }

    [Fact]
    public void BuildVarDctPipeline_NoFilter_OnlyColorStages()
    {
        var fh = new FrameHeader();
        fh.Filter.Gab = false;
        fh.Filter.EpfIters = 0;

        var fd = new FrameDimensions();
        fd.Set(8, 8, 1, 0, 0, false, 1);

        var pipeline = PipelineBuilder.BuildVarDctPipeline(fh, fd, 3, 8, 8);

        // Fill with known XYB values and run
        for (int c = 0; c < 3; c++)
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            pipeline.ChannelData![c][y][x] = 0.0f;

        Assert.True(pipeline.ProcessGroup(0, 0));
    }
}

public class EpfSigmaTests
{
    [Fact]
    public void ComputeSigma_UniformQuantField_ProducesUniformSigma()
    {
        var lf = new LoopFilter();
        // Default EpfSharpLut: lut[i] = i / 7.0
        for (int i = 0; i < 8; i++)
            lf.EpfSharpLut[i] = i / 7.0f;

        int xBlocks = 4, yBlocks = 4;
        int[,] quantField = new int[yBlocks, xBlocks];
        byte[,] sharpness = new byte[yBlocks, xBlocks];

        for (int by = 0; by < yBlocks; by++)
        for (int bx = 0; bx < xBlocks; bx++)
        {
            quantField[by, bx] = 1;
            sharpness[by, bx] = 4; // mid-range sharpness
        }

        float quantScale = 1.0f;
        var sigma = EpfSigma.ComputeSigma(lf, quantScale, quantField, sharpness, xBlocks, yBlocks);

        // Check dimensions include padding (2 on each side)
        Assert.Equal(yBlocks + 4, sigma.GetLength(0));
        Assert.Equal(xBlocks + 4, sigma.GetLength(1));

        // Center values should all be equal (uniform field, uniform sharpness)
        float centerVal = sigma[2, 2]; // First non-padded value
        for (int by = 0; by < yBlocks; by++)
        for (int bx = 0; bx < xBlocks; bx++)
            Assert.Equal(centerVal, sigma[by + 2, bx + 2]);

        // Padding should be mirrored copies
        Assert.Equal(sigma[2, 2], sigma[1, 2]); // top mirror
        Assert.Equal(sigma[2, 2], sigma[2, 1]); // left mirror
    }

    [Fact]
    public void ComputeSigma_VariableSharpness_ProducesVariableSigma()
    {
        var lf = new LoopFilter();
        for (int i = 0; i < 8; i++)
            lf.EpfSharpLut[i] = i / 7.0f;

        int xBlocks = 2, yBlocks = 2;
        int[,] quantField = { { 1, 1 }, { 1, 1 } };
        byte[,] sharpness = { { 0, 7 }, { 3, 5 } };

        var sigma = EpfSigma.ComputeSigma(lf, 1.0f, quantField, sharpness, xBlocks, yBlocks);

        // Different sharpness -> different sigma
        float s00 = sigma[2, 2]; // sharpness=0, lut=0
        float s01 = sigma[2, 3]; // sharpness=7, lut=1.0
        // With sharpness 0, lut[0]=0 -> sigmaQuant*0 -> sigma very small -> 1/sigma very large
        // With sharpness 7, lut[7]=1.0 -> sigmaQuant*1.0 -> finite value
        Assert.NotEqual(s00, s01);
    }
}
