using LibJxl.Decoder;
using LibJxl.Fields;
using LibJxl.RenderPipeline;
using Xunit;

namespace LibJxl.Tests.RenderPipeline;

public class StageBlendTests
{
    private static FrameHeader CreateBlendHeader(BlendMode mode, bool clamp = false)
    {
        var fh = new FrameHeader
        {
            Type = FrameType.RegularFrame,
            Blending = new BlendingInfo
            {
                Mode = mode,
                AlphaChannel = 0,
                Clamp = clamp,
            },
            ExtraChannelBlending = Array.Empty<BlendingInfo>(),
        };
        return fh;
    }

    private static ReferenceFrame CreateBgFrame(int width, int height, int numChannels, float fillValue)
    {
        var refFrame = new ReferenceFrame
        {
            Width = width,
            Height = height,
            ChannelData = new float[numChannels][][],
        };
        for (int c = 0; c < numChannels; c++)
        {
            refFrame.ChannelData[c] = new float[height][];
            for (int y = 0; y < height; y++)
            {
                refFrame.ChannelData[c][y] = new float[width];
                Array.Fill(refFrame.ChannelData[c][y], fillValue);
            }
        }
        return refFrame;
    }

    [Fact]
    public void Name_IsBlend()
    {
        var fh = CreateBlendHeader(BlendMode.Replace);
        var stage = new StageBlend(fh, null, 3, 4, 4);
        Assert.Equal("Blend", stage.Name);
    }

    [Fact]
    public void ChannelMode_AllInPlace()
    {
        var fh = CreateBlendHeader(BlendMode.Replace);
        var stage = new StageBlend(fh, null, 3, 4, 4);
        Assert.Equal(ChannelMode.InPlace, stage.GetChannelMode(0));
        Assert.Equal(ChannelMode.InPlace, stage.GetChannelMode(1));
        Assert.Equal(ChannelMode.InPlace, stage.GetChannelMode(2));
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(3));
    }

    [Fact]
    public void Replace_NoBg_KeepsFg()
    {
        var fh = CreateBlendHeader(BlendMode.Replace);
        var stage = new StageBlend(fh, null, 3, 4, 1);

        float[] fg0 = { 0.5f, 0.6f, 0.7f, 0.8f };
        float[] fg1 = { 0.1f, 0.2f, 0.3f, 0.4f };
        float[] fg2 = { 0.9f, 0.8f, 0.7f, 0.6f };
        var rows = new float[][] { fg0, fg1, fg2 };

        // Replace with no background: nothing changes
        Assert.True(stage.ProcessRow(rows, rows, 4, 0, 0, 0));
        Assert.Equal(0.5f, fg0[0]);
        Assert.Equal(0.2f, fg1[1]);
    }

    [Fact]
    public void Add_BlendsFgAndBg()
    {
        var fh = CreateBlendHeader(BlendMode.Add);
        var bg = CreateBgFrame(4, 1, 3, 0.3f);
        var stage = new StageBlend(fh, bg, 3, 4, 1);

        float[] fg0 = { 0.5f, 0.5f, 0.5f, 0.5f };
        float[] fg1 = { 0.1f, 0.1f, 0.1f, 0.1f };
        float[] fg2 = { 0.2f, 0.2f, 0.2f, 0.2f };
        var rows = new float[][] { fg0, fg1, fg2 };

        Assert.True(stage.ProcessRow(rows, rows, 4, 0, 0, 0));

        // Add: fg + bg = 0.5 + 0.3 = 0.8
        for (int x = 0; x < 4; x++)
            Assert.InRange(fg0[x], 0.79f, 0.81f);
        // fg1: 0.1 + 0.3 = 0.4
        for (int x = 0; x < 4; x++)
            Assert.InRange(fg1[x], 0.39f, 0.41f);
    }

    [Fact]
    public void Mul_WithClamp_MultipliesBgByClampedFg()
    {
        var fh = CreateBlendHeader(BlendMode.Mul, clamp: true);
        var bg = CreateBgFrame(4, 1, 3, 0.8f);
        var stage = new StageBlend(fh, bg, 3, 4, 1);

        float[] fg0 = { 0.5f, 1.5f, -0.2f, 0.0f };
        float[] fg1 = { 0.5f, 0.5f, 0.5f, 0.5f };
        float[] fg2 = { 0.5f, 0.5f, 0.5f, 0.5f };
        var rows = new float[][] { fg0, fg1, fg2 };

        Assert.True(stage.ProcessRow(rows, rows, 4, 0, 0, 0));

        // Mul+clamp: bg * clamp(fg, 0, 1)
        Assert.InRange(fg0[0], 0.39f, 0.41f); // 0.8 * 0.5 = 0.4
        Assert.InRange(fg0[1], 0.79f, 0.81f); // 0.8 * clamp(1.5, 0, 1) = 0.8 * 1.0 = 0.8
        Assert.InRange(fg0[2], -0.01f, 0.01f); // 0.8 * clamp(-0.2, 0, 1) = 0.8 * 0.0 = 0.0
        Assert.InRange(fg0[3], -0.01f, 0.01f); // 0.8 * 0.0 = 0.0
    }

    [Fact]
    public void Mul_WithoutClamp_MultipliesBgByFg()
    {
        var fh = CreateBlendHeader(BlendMode.Mul, clamp: false);
        var bg = CreateBgFrame(2, 1, 3, 0.8f);
        var stage = new StageBlend(fh, bg, 3, 2, 1);

        float[] fg0 = { 1.5f, -0.5f };
        float[] fg1 = { 0.5f, 0.5f };
        float[] fg2 = { 0.5f, 0.5f };
        var rows = new float[][] { fg0, fg1, fg2 };

        Assert.True(stage.ProcessRow(rows, rows, 2, 0, 0, 0));

        // Without clamp: bg * fg
        Assert.InRange(fg0[0], 1.19f, 1.21f); // 0.8 * 1.5 = 1.2
        Assert.InRange(fg0[1], -0.41f, -0.39f); // 0.8 * (-0.5) = -0.4
    }

    [Fact]
    public void Blend_AlphaAbove_OpaqueAlpha()
    {
        var fh = new FrameHeader
        {
            Type = FrameType.RegularFrame,
            Blending = new BlendingInfo
            {
                Mode = BlendMode.Blend,
                AlphaChannel = 0,
                Clamp = true,
            },
            ExtraChannelBlending = Array.Empty<BlendingInfo>(),
        };

        // Background: 0.2 for all channels, with alpha (channel 3) = 1.0
        var bg = CreateBgFrame(4, 1, 4, 0.2f);
        // Set bg alpha to 1.0
        Array.Fill(bg.ChannelData[3][0], 1.0f);

        var stage = new StageBlend(fh, bg, 4, 4, 1);

        // Foreground: 0.8, alpha=1.0 (fully opaque)
        float[] fg0 = { 0.8f, 0.8f, 0.8f, 0.8f };
        float[] fg1 = { 0.8f, 0.8f, 0.8f, 0.8f };
        float[] fg2 = { 0.8f, 0.8f, 0.8f, 0.8f };
        float[] fgA = { 1.0f, 1.0f, 1.0f, 1.0f }; // alpha = 1.0
        var rows = new float[][] { fg0, fg1, fg2, fgA };

        Assert.True(stage.ProcessRow(rows, rows, 4, 0, 0, 0));

        // Fully opaque foreground over background: result = fg = 0.8
        for (int x = 0; x < 4; x++)
            Assert.InRange(fg0[x], 0.79f, 0.81f);
    }

    [Fact]
    public void Blend_AlphaAbove_TransparentAlpha()
    {
        var fh = new FrameHeader
        {
            Type = FrameType.RegularFrame,
            Blending = new BlendingInfo
            {
                Mode = BlendMode.Blend,
                AlphaChannel = 0,
                Clamp = true,
            },
            ExtraChannelBlending = Array.Empty<BlendingInfo>(),
        };

        var bg = CreateBgFrame(4, 1, 4, 0.2f);
        Array.Fill(bg.ChannelData[3][0], 1.0f);
        var stage = new StageBlend(fh, bg, 4, 4, 1);

        // Foreground with alpha=0 (fully transparent)
        float[] fg0 = { 0.8f, 0.8f, 0.8f, 0.8f };
        float[] fg1 = { 0.8f, 0.8f, 0.8f, 0.8f };
        float[] fg2 = { 0.8f, 0.8f, 0.8f, 0.8f };
        float[] fgA = { 0.0f, 0.0f, 0.0f, 0.0f }; // alpha = 0
        var rows = new float[][] { fg0, fg1, fg2, fgA };

        Assert.True(stage.ProcessRow(rows, rows, 4, 0, 0, 0));

        // Fully transparent fg: result = bg = 0.2
        for (int x = 0; x < 4; x++)
            Assert.InRange(fg0[x], 0.19f, 0.21f);
    }

    [Fact]
    public void Blend_AlphaAbove_HalfAlpha()
    {
        var fh = new FrameHeader
        {
            Type = FrameType.RegularFrame,
            Blending = new BlendingInfo
            {
                Mode = BlendMode.Blend,
                AlphaChannel = 0,
                Clamp = false,
            },
            ExtraChannelBlending = Array.Empty<BlendingInfo>(),
        };

        var bg = CreateBgFrame(4, 1, 4, 0.0f);
        Array.Fill(bg.ChannelData[3][0], 1.0f); // bg alpha=1
        var stage = new StageBlend(fh, bg, 4, 4, 1);

        float[] fg0 = { 1.0f, 1.0f, 1.0f, 1.0f };
        float[] fg1 = { 1.0f, 1.0f, 1.0f, 1.0f };
        float[] fg2 = { 1.0f, 1.0f, 1.0f, 1.0f };
        float[] fgA = { 0.5f, 0.5f, 0.5f, 0.5f }; // alpha=0.5
        var rows = new float[][] { fg0, fg1, fg2, fgA };

        Assert.True(stage.ProcessRow(rows, rows, 4, 0, 0, 0));

        // newA = 1-(1-0.5)*(1-1) = 1-0.5*0 = 1.0
        // result = (fg*fga + bg*bga*(1-fga))/newA = (1*0.5 + 0*1*0.5)/1 = 0.5
        for (int x = 0; x < 4; x++)
            Assert.InRange(fg0[x], 0.49f, 0.51f);
    }

    [Fact]
    public void NoBgFrame_ReturnsTrue()
    {
        var fh = CreateBlendHeader(BlendMode.Add);
        var stage = new StageBlend(fh, null, 3, 4, 1);

        float[] fg0 = { 0.5f, 0.5f };
        float[] fg1 = { 0.5f, 0.5f };
        float[] fg2 = { 0.5f, 0.5f };
        var rows = new float[][] { fg0, fg1, fg2 };

        // No bg: ProcessRow should just return true without modification
        Assert.True(stage.ProcessRow(rows, rows, 2, 0, 0, 0));
        Assert.Equal(0.5f, fg0[0]);
    }

    [Fact]
    public void OutOfBounds_Ypos_ReturnsTrue()
    {
        var fh = CreateBlendHeader(BlendMode.Add);
        var bg = CreateBgFrame(4, 2, 3, 0.3f);
        var stage = new StageBlend(fh, bg, 3, 4, 2);

        float[] fg0 = { 0.5f, 0.5f, 0.5f, 0.5f };
        float[] fg1 = { 0.5f, 0.5f, 0.5f, 0.5f };
        float[] fg2 = { 0.5f, 0.5f, 0.5f, 0.5f };
        var rows = new float[][] { fg0, fg1, fg2 };

        // ypos=5 is out of bounds for bg height=2
        Assert.True(stage.ProcessRow(rows, rows, 4, 0, 5, 0));
        // fg should be unchanged
        Assert.Equal(0.5f, fg0[0]);
    }
}

public class PatchBlendModeTests
{
    [Theory]
    [InlineData(PatchBlendMode.None, 0)]
    [InlineData(PatchBlendMode.Replace, 1)]
    [InlineData(PatchBlendMode.Add, 2)]
    [InlineData(PatchBlendMode.Mul, 3)]
    [InlineData(PatchBlendMode.BlendAbove, 4)]
    [InlineData(PatchBlendMode.BlendBelow, 5)]
    [InlineData(PatchBlendMode.AlphaWeightedAddAbove, 6)]
    [InlineData(PatchBlendMode.AlphaWeightedAddBelow, 7)]
    public void EnumValues_MatchExpected(PatchBlendMode mode, int expected)
    {
        Assert.Equal(expected, (int)mode);
    }
}
