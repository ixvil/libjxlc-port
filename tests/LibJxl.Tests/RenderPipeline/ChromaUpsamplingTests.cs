using LibJxl.RenderPipeline;
using Xunit;

namespace LibJxl.Tests.RenderPipeline;

public class StageChromaUpsamplingHTests
{
    [Fact]
    public void Settings_ShiftX1_BorderX1()
    {
        var stage = new StageChromaUpsamplingH(0);
        Assert.Equal(1, stage.Settings.ShiftX);
        Assert.Equal(0, stage.Settings.ShiftY);
        Assert.Equal(1, stage.Settings.BorderX);
        Assert.Equal(0, stage.Settings.BorderY);
    }

    [Fact]
    public void ChannelMode_OnlyTargetChannelInOut()
    {
        var stage = new StageChromaUpsamplingH(1);
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(0));
        Assert.Equal(ChannelMode.InOut, stage.GetChannelMode(1));
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(2));
    }

    [Fact]
    public void Name_ContainsChannel()
    {
        var stage = new StageChromaUpsamplingH(2);
        Assert.Contains("c2", stage.Name);
        Assert.Contains("ChromaUpsampleH", stage.Name);
    }

    [Fact]
    public void ProcessRowWithBorder_UniformInput_PreservedAfterUpsampling()
    {
        var stage = new StageChromaUpsamplingH(0);
        int borderX = 1;
        int inputWidth = 4;
        int paddedW = inputWidth + 2 * borderX;
        int outputWidth = inputWidth * 2;

        // Uniform 0.5 input with border padding
        float[] padded = new float[paddedW];
        Array.Fill(padded, 0.5f);

        var inputRows = new float[][][] {
            new[] { padded }, // channel 0: single row (borderY=0)
        };
        var outputRows = new float[][][] {
            new[] { new float[outputWidth] },
        };

        Assert.True(stage.ProcessRowWithBorder(inputRows, outputRows, inputWidth, borderX, 0, 0));

        // Uniform input: 0.75*0.5 + 0.25*0.5 = 0.5 for all output pixels
        for (int x = 0; x < outputWidth; x++)
            Assert.InRange(outputRows[0][0][x], 0.49f, 0.51f);
    }

    [Fact]
    public void ProcessRowWithBorder_LinearGradient_InterpolatesCorrectly()
    {
        var stage = new StageChromaUpsamplingH(0);
        int borderX = 1;
        int inputWidth = 3;

        // Input: [mirror | 1.0  2.0  3.0 | mirror]
        //         0: 1.0, 1: 1.0, 2: 2.0, 3: 3.0, 4: 3.0
        float[] padded = { 1.0f, 1.0f, 2.0f, 3.0f, 3.0f };

        var inputRows = new float[][][] {
            new[] { padded },
        };
        var outputRows = new float[][][] {
            new[] { new float[6] }, // 3*2 = 6 output pixels
        };

        Assert.True(stage.ProcessRowWithBorder(inputRows, outputRows, inputWidth, borderX, 0, 0));

        float[] out0 = outputRows[0][0];

        // out[0] = 0.75*1.0 + 0.25*1.0 = 1.0 (prev=mirror=1.0)
        Assert.InRange(out0[0], 0.99f, 1.01f);
        // out[1] = 0.75*1.0 + 0.25*2.0 = 1.25
        Assert.InRange(out0[1], 1.24f, 1.26f);
        // out[2] = 0.75*2.0 + 0.25*1.0 = 1.75
        Assert.InRange(out0[2], 1.74f, 1.76f);
        // out[3] = 0.75*2.0 + 0.25*3.0 = 2.25
        Assert.InRange(out0[3], 2.24f, 2.26f);
        // out[4] = 0.75*3.0 + 0.25*2.0 = 2.75
        Assert.InRange(out0[4], 2.74f, 2.76f);
        // out[5] = 0.75*3.0 + 0.25*3.0 = 3.0 (next=mirror=3.0)
        Assert.InRange(out0[5], 2.99f, 3.01f);
    }

    [Fact]
    public void ProcessRowWithBorder_StepFunction_SmoothTransition()
    {
        var stage = new StageChromaUpsamplingH(0);
        int borderX = 1;
        int inputWidth = 4;

        // Step: [0 | 0  0  1  1 | 1]
        float[] padded = { 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f };

        var inputRows = new float[][][] { new[] { padded } };
        var outputRows = new float[][][] { new[] { new float[8] } };

        Assert.True(stage.ProcessRowWithBorder(inputRows, outputRows, inputWidth, borderX, 0, 0));

        float[] o = outputRows[0][0];
        // Left side should be ~0
        Assert.InRange(o[0], -0.01f, 0.01f); // 0.75*0 + 0.25*0
        Assert.InRange(o[1], -0.01f, 0.01f); // 0.75*0 + 0.25*0
        // Transition: out[3] = 0.75*0 + 0.25*1 = 0.25
        Assert.InRange(o[3], 0.24f, 0.26f);
        // out[4] = 0.75*1 + 0.25*0 = 0.75
        Assert.InRange(o[4], 0.74f, 0.76f);
        // Right side should be ~1
        Assert.InRange(o[7], 0.99f, 1.01f);
    }

    [Fact]
    public void Pipeline_Integration_ProcessesSuccessfully()
    {
        var pipeline = new SimpleRenderPipeline();
        pipeline.AllocateBuffers(4, 2, 3);

        // Fill channel 1 with uniform 0.6
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
                pipeline.ChannelData![1][y][x] = 0.6f;

        pipeline.AddStage(new StageChromaUpsamplingH(1));
        Assert.True(pipeline.ProcessGroup(0, 0));

        // Pipeline processes successfully (dimensions stay as allocated)
        Assert.Equal(4, pipeline.Width);
        Assert.Equal(2, pipeline.Height);
    }
}

public class StageChromaUpsamplingVTests
{
    [Fact]
    public void Settings_ShiftY1_BorderY1()
    {
        var stage = new StageChromaUpsamplingV(0);
        Assert.Equal(0, stage.Settings.ShiftX);
        Assert.Equal(1, stage.Settings.ShiftY);
        Assert.Equal(0, stage.Settings.BorderX);
        Assert.Equal(1, stage.Settings.BorderY);
    }

    [Fact]
    public void ChannelMode_OnlyTargetChannelInOut()
    {
        var stage = new StageChromaUpsamplingV(2);
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(0));
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(1));
        Assert.Equal(ChannelMode.InOut, stage.GetChannelMode(2));
    }

    [Fact]
    public void ProcessRowWithBorder_UniformInput_Preserved()
    {
        var stage = new StageChromaUpsamplingV(0);
        int width = 4;

        // 3 input rows: top, mid, bot — all uniform 0.7
        float[] rowTop = { 0.7f, 0.7f, 0.7f, 0.7f };
        float[] rowMid = { 0.7f, 0.7f, 0.7f, 0.7f };
        float[] rowBot = { 0.7f, 0.7f, 0.7f, 0.7f };

        var inputRows = new float[][][] {
            new[] { rowTop, rowMid, rowBot }, // channel 0: 3 rows (borderY=1)
        };
        var outputRows = new float[][][] {
            new[] { new float[width], new float[width] }, // 2 output rows
        };

        Assert.True(stage.ProcessRowWithBorder(inputRows, outputRows, width, 0, 0, 0));

        for (int x = 0; x < width; x++)
        {
            Assert.InRange(outputRows[0][0][x], 0.69f, 0.71f);
            Assert.InRange(outputRows[0][1][x], 0.69f, 0.71f);
        }
    }

    [Fact]
    public void ProcessRowWithBorder_VerticalGradient_InterpolatesCorrectly()
    {
        var stage = new StageChromaUpsamplingV(0);
        int width = 2;

        // Vertical gradient: top=0, mid=1, bot=2
        float[] rowTop = { 0.0f, 0.0f };
        float[] rowMid = { 1.0f, 1.0f };
        float[] rowBot = { 2.0f, 2.0f };

        var inputRows = new float[][][] {
            new[] { rowTop, rowMid, rowBot },
        };
        var outputRows = new float[][][] {
            new[] { new float[width], new float[width] },
        };

        Assert.True(stage.ProcessRowWithBorder(inputRows, outputRows, width, 0, 0, 0));

        // out0[x] = 0.75*1.0 + 0.25*0.0 = 0.75 (mid weighted toward top)
        // out1[x] = 0.75*1.0 + 0.25*2.0 = 1.25 (mid weighted toward bot)
        for (int x = 0; x < width; x++)
        {
            Assert.InRange(outputRows[0][0][x], 0.74f, 0.76f);
            Assert.InRange(outputRows[0][1][x], 1.24f, 1.26f);
        }
    }

    [Fact]
    public void Pipeline_Integration_ProcessesSuccessfully()
    {
        var pipeline = new SimpleRenderPipeline();
        pipeline.AllocateBuffers(3, 4, 3);

        // Fill channel 2 with uniform 0.4
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 3; x++)
                pipeline.ChannelData![2][y][x] = 0.4f;

        pipeline.AddStage(new StageChromaUpsamplingV(2));
        Assert.True(pipeline.ProcessGroup(0, 0));

        // Pipeline processes successfully (dimensions stay as allocated)
        Assert.Equal(3, pipeline.Width);
        Assert.Equal(4, pipeline.Height);
    }
}
