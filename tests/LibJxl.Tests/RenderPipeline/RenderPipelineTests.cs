using LibJxl.ColorManagement;
using LibJxl.RenderPipeline;
using Xunit;

namespace LibJxl.Tests.RenderPipeline;

public class StageSettingsTests
{
    [Fact]
    public void None_AllZero()
    {
        var s = StageSettings.None;
        Assert.Equal(0, s.BorderX);
        Assert.Equal(0, s.BorderY);
        Assert.Equal(0, s.ShiftX);
        Assert.Equal(0, s.ShiftY);
    }

    [Fact]
    public void SymmetricBorderOnly_SetsBoth()
    {
        var s = StageSettings.SymmetricBorderOnly(3);
        Assert.Equal(3, s.BorderX);
        Assert.Equal(3, s.BorderY);
        Assert.Equal(0, s.ShiftX);
    }

    [Fact]
    public void Symmetric_SetsAll()
    {
        var s = StageSettings.Symmetric(2, 1);
        Assert.Equal(2, s.ShiftX);
        Assert.Equal(2, s.ShiftY);
        Assert.Equal(1, s.BorderX);
        Assert.Equal(1, s.BorderY);
    }
}

public class StageXybTests
{
    [Fact]
    public void ProcessRow_BlackPixels()
    {
        var p = new OpsinParams();
        p.InitDefault();
        var stage = StageXyb.Create(p);

        // Black in XYB = (0, 0, 0)
        float[] x = { 0, 0, 0, 0 };
        float[] y = { 0, 0, 0, 0 };
        float[] b = { 0, 0, 0, 0 };
        var input = new float[][] { x, y, b };
        var output = new float[][] { x, y, b };

        Assert.True(stage.ProcessRow(input, output, 4, 0, 0, 0));

        for (int i = 0; i < 4; i++)
        {
            Assert.InRange(x[i], -0.01f, 0.01f);
            Assert.InRange(y[i], -0.01f, 0.01f);
            Assert.InRange(b[i], -0.01f, 0.01f);
        }
    }

    [Fact]
    public void ChannelMode_FirstThreeInPlace()
    {
        var p = new OpsinParams();
        p.InitDefault();
        var stage = StageXyb.Create(p);

        Assert.Equal(ChannelMode.InPlace, stage.GetChannelMode(0));
        Assert.Equal(ChannelMode.InPlace, stage.GetChannelMode(1));
        Assert.Equal(ChannelMode.InPlace, stage.GetChannelMode(2));
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(3));
    }
}

public class StageFromLinearTests
{
    [Fact]
    public void ProcessRow_ConvertsLinearToSrgb()
    {
        var stage = new StageFromLinear();

        // Linear 0.5 should become sRGB ~0.735
        float[] r = { 0.0f, 0.5f, 1.0f };
        float[] g = { 0.0f, 0.5f, 1.0f };
        float[] b = { 0.0f, 0.5f, 1.0f };
        var input = new float[][] { r, g, b };

        Assert.True(stage.ProcessRow(input, input, 3, 0, 0, 0));

        // Zero should stay zero
        Assert.InRange(r[0], -0.001f, 0.001f);
        // 0.5 linear → ~0.735 sRGB
        Assert.InRange(r[1], 0.7f, 0.77f);
        // 1.0 linear → ~1.0 sRGB
        Assert.InRange(r[2], 0.99f, 1.01f);
    }
}

public class StageYcbcrTests
{
    [Fact]
    public void ProcessRow_GreyPixel()
    {
        var stage = new StageYcbcr();

        // Grey: Y=0.5, Cb=0, Cr=0 → R=G=B=0.5
        float[] cb = { 0.0f };
        float[] y = { 0.5f };
        float[] cr = { 0.0f };
        var input = new float[][] { cb, y, cr };

        Assert.True(stage.ProcessRow(input, input, 1, 0, 0, 0));

        Assert.InRange(cb[0], 0.499f, 0.501f); // R
        Assert.InRange(y[0], 0.499f, 0.501f);  // G
        Assert.InRange(cr[0], 0.499f, 0.501f);  // B
    }
}

public class StageWriteTests
{
    [Fact]
    public void ProcessRow_WritesRGB()
    {
        byte[] output = new byte[4 * 3]; // 4 pixels, RGB
        var stage = new StageWrite(output, 4, 3, false);

        float[] r = { 0.0f, 0.5f, 1.0f, 0.25f };
        float[] g = { 1.0f, 0.0f, 0.5f, 0.75f };
        float[] b = { 0.5f, 1.0f, 0.0f, 1.0f };
        var input = new float[][] { r, g, b };

        Assert.True(stage.ProcessRow(input, input, 4, 0, 0, 0));

        // Pixel 0: R=0, G=255, B=128
        Assert.Equal(0, output[0]);
        Assert.Equal(255, output[1]);
        Assert.InRange(output[2], (byte)127, (byte)128);

        // Pixel 2: R=255, G=128, B=0
        Assert.Equal(255, output[6]);
        Assert.InRange(output[7], (byte)127, (byte)128);
        Assert.Equal(0, output[8]);
    }

    [Fact]
    public void ProcessRow_WritesRGBA()
    {
        byte[] output = new byte[2 * 4]; // 2 pixels, RGBA
        var stage = new StageWrite(output, 2, 4, true);

        float[] r = { 1.0f, 0.0f };
        float[] g = { 0.0f, 1.0f };
        float[] b = { 0.0f, 0.0f };
        float[] a = { 1.0f, 0.5f };
        var input = new float[][] { r, g, b, a };

        Assert.True(stage.ProcessRow(input, input, 2, 0, 0, 0));

        // Pixel 0: R=255, G=0, B=0, A=255
        Assert.Equal(255, output[0]);
        Assert.Equal(0, output[1]);
        Assert.Equal(0, output[2]);
        Assert.Equal(255, output[3]);

        // Pixel 1: A~128
        Assert.InRange(output[7], (byte)127, (byte)128);
    }

    [Fact]
    public void ChannelMode_RGB_Input()
    {
        var stage = new StageWrite(new byte[12], 4, 3, false);
        Assert.Equal(ChannelMode.Input, stage.GetChannelMode(0));
        Assert.Equal(ChannelMode.Input, stage.GetChannelMode(1));
        Assert.Equal(ChannelMode.Input, stage.GetChannelMode(2));
        Assert.Equal(ChannelMode.Ignored, stage.GetChannelMode(3));
    }
}

public class SimpleRenderPipelineTests
{
    [Fact]
    public void EmptyPipeline_NoOp()
    {
        var pipeline = LibJxl.RenderPipeline.RenderPipeline.CreateSimple();
        Assert.True(pipeline.ProcessGroup(0, 0));
    }

    [Fact]
    public void XybThenFromLinear_Pipeline()
    {
        var p = new OpsinParams();
        p.InitDefault();

        var pipeline = LibJxl.RenderPipeline.RenderPipeline.CreateSimple();
        pipeline.AddStage(StageXyb.Create(p));
        pipeline.AddStage(new StageFromLinear());

        // Create 2x2 black image in XYB space
        pipeline.AllocateBuffers(2, 2, 3);
        // XYB = (0, 0, 0) = black, already zero-initialized

        Assert.True(pipeline.ProcessGroup(0, 0));

        // After XYB→Linear→sRGB, black should remain near zero
        var data = pipeline.ChannelData!;
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < 2; y++)
            {
                var row = data[c][y];
                Assert.InRange(row[0], -0.02f, 0.02f);
                Assert.InRange(row[1], -0.02f, 0.02f);
            }
        }
    }

    [Fact]
    public void FullPipeline_XybToSrgbBytes()
    {
        var p = new OpsinParams();
        p.InitDefault();

        int w = 4, h = 2;
        byte[] outputBytes = new byte[w * h * 3];

        var pipeline = LibJxl.RenderPipeline.RenderPipeline.CreateSimple();
        pipeline.AddStage(StageXyb.Create(p));
        pipeline.AddStage(new StageFromLinear());
        pipeline.AddStage(new StageWrite(outputBytes, w, 3, false));
        pipeline.AllocateBuffers(w, h, 3);

        Assert.True(pipeline.ProcessGroup(0, 0));

        // Black pixels should all be near 0
        for (int i = 0; i < outputBytes.Length; i++)
        {
            Assert.InRange(outputBytes[i], (byte)0, (byte)5);
        }
    }
}
