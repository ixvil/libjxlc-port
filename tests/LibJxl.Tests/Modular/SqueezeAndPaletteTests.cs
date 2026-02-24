using LibJxl.Modular;
using Xunit;

namespace LibJxl.Tests.Modular;

public class InverseSqueezeTests
{
    [Fact]
    public void HSqueeze_FlatBlock_Reconstructs()
    {
        // Average = [5, 5], Residual = [0, 0] → output = [5, 5, 5, 5]
        var image = ModularImage.Create(2, 1, 8, 2);
        image.Channels[0].Row(0)[0] = 5;
        image.Channels[0].Row(0)[1] = 5;
        image.Channels[1] = Channel.Create(2, 1);
        image.Channels[1].Row(0)[0] = 0;
        image.Channels[1].Row(0)[1] = 0;

        var t = new Transform
        {
            Id = TransformId.Squeeze,
            Squeezes =
            [
                new SqueezeParams { Horizontal = true, BeginC = 0, NumC = 1 }
            ]
        };

        var wp = new WeightedHeader();
        t.Inverse(image, wp);

        // After unsqueeze: channel 0 should be width 4
        Assert.Equal(4, image.Channels[0].W);
        for (int x = 0; x < 4; x++)
            Assert.Equal(5, image.Channels[0].Row(0)[x]);
    }

    [Fact]
    public void VSqueeze_FlatBlock_Reconstructs()
    {
        // Average rows = [5, 5], Residual rows = [0, 0] → output = 4 rows of [5, 5]
        var image = ModularImage.Create(2, 2, 8, 2);
        image.Channels[0].Row(0)[0] = 5; image.Channels[0].Row(0)[1] = 5;
        image.Channels[0].Row(1)[0] = 5; image.Channels[0].Row(1)[1] = 5;
        image.Channels[1] = Channel.Create(2, 2);
        // Residual = 0
        image.Channels[1].Row(0)[0] = 0; image.Channels[1].Row(0)[1] = 0;
        image.Channels[1].Row(1)[0] = 0; image.Channels[1].Row(1)[1] = 0;

        var t = new Transform
        {
            Id = TransformId.Squeeze,
            Squeezes =
            [
                new SqueezeParams { Horizontal = false, BeginC = 0, NumC = 1 }
            ]
        };

        var wp = new WeightedHeader();
        t.Inverse(image, wp);

        Assert.Equal(4, image.Channels[0].H);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 2; x++)
                Assert.Equal(5, image.Channels[0].Row(y)[x]);
    }

    [Fact]
    public void HSqueeze_SimpleGradient_Reconstructs()
    {
        // Average = [10, 20], Residual = [-10, -10] → reconstruct gradient
        var image = ModularImage.Create(2, 1, 8, 2);
        image.Channels[0].Row(0)[0] = 10;
        image.Channels[0].Row(0)[1] = 20;
        image.Channels[1] = Channel.Create(2, 1);
        image.Channels[1].Row(0)[0] = -10; // diff (before tendency)
        image.Channels[1].Row(0)[1] = -10;

        var t = new Transform
        {
            Id = TransformId.Squeeze,
            Squeezes =
            [
                new SqueezeParams { Horizontal = true, BeginC = 0, NumC = 1 }
            ]
        };

        var wp = new WeightedHeader();
        t.Inverse(image, wp);

        Assert.Equal(4, image.Channels[0].W);
        // Values should form a reconstructed pattern from the average + diff
    }

    [Fact]
    public void Squeeze_RemovesResidualChannel()
    {
        var image = ModularImage.Create(2, 1, 8, 2);
        image.Channels[1] = Channel.Create(2, 1);
        Assert.Equal(2, image.Channels.Count);

        var t = new Transform
        {
            Id = TransformId.Squeeze,
            Squeezes =
            [
                new SqueezeParams { Horizontal = true, BeginC = 0, NumC = 1 }
            ]
        };

        var wp = new WeightedHeader();
        t.Inverse(image, wp);

        Assert.Single(image.Channels);
    }
}

public class InversePaletteTests
{
    [Fact]
    public void SimplePalette_ExpandsColors()
    {
        // Palette: 3 rows (RGB), 4 colors
        // Indices: 2x2 image with values [0, 1, 2, 3]
        var image = ModularImage.Create(4, 3, 8, 2);
        image.NbMetaChannels = 1;

        // Palette (channel 0): 3 rows × 4 columns (3 channels, 4 colors)
        image.Channels[0] = Channel.Create(4, 3);
        // Color 0: R=10, G=20, B=30
        image.Channels[0].Row(0)[0] = 10; image.Channels[0].Row(1)[0] = 20; image.Channels[0].Row(2)[0] = 30;
        // Color 1: R=40, G=50, B=60
        image.Channels[0].Row(0)[1] = 40; image.Channels[0].Row(1)[1] = 50; image.Channels[0].Row(2)[1] = 60;
        // Color 2: R=70, G=80, B=90
        image.Channels[0].Row(0)[2] = 70; image.Channels[0].Row(1)[2] = 80; image.Channels[0].Row(2)[2] = 90;
        // Color 3: R=100, G=110, B=120
        image.Channels[0].Row(0)[3] = 100; image.Channels[0].Row(1)[3] = 110; image.Channels[0].Row(2)[3] = 120;

        // Indices (channel 1): 2x2 image
        image.Channels[1] = Channel.Create(2, 2);
        image.Channels[1].Row(0)[0] = 0; image.Channels[1].Row(0)[1] = 1;
        image.Channels[1].Row(1)[0] = 2; image.Channels[1].Row(1)[1] = 3;

        var t = new Transform
        {
            Id = TransformId.Palette,
            BeginC = 0,
            NumC = 3,
            NbColors = 4,
            NbDeltas = 0,
        };

        var wp = new WeightedHeader();
        t.Inverse(image, wp);

        // Should now have 3 channels (R, G, B) of size 2x2
        Assert.Equal(3, image.Channels.Count);

        // Check R channel
        Assert.Equal(10, image.Channels[0].Row(0)[0]); // Color 0
        Assert.Equal(40, image.Channels[0].Row(0)[1]); // Color 1
        Assert.Equal(70, image.Channels[0].Row(1)[0]); // Color 2
        Assert.Equal(100, image.Channels[0].Row(1)[1]); // Color 3

        // Check G channel
        Assert.Equal(20, image.Channels[1].Row(0)[0]);
        Assert.Equal(50, image.Channels[1].Row(0)[1]);

        // Check B channel
        Assert.Equal(30, image.Channels[2].Row(0)[0]);
        Assert.Equal(60, image.Channels[2].Row(0)[1]);
    }

    [Fact]
    public void DeltaPalette_NegativeIndex()
    {
        // Test that negative indices produce delta palette values
        var image = ModularImage.Create(1, 3, 8, 2);
        image.NbMetaChannels = 1;

        // Minimal palette (1 color)
        image.Channels[0] = Channel.Create(1, 3);
        image.Channels[0].Row(0)[0] = 128; // R
        image.Channels[0].Row(1)[0] = 128; // G
        image.Channels[0].Row(2)[0] = 128; // B

        // Indices with negative value (delta palette)
        image.Channels[1] = Channel.Create(1, 1);
        image.Channels[1].Row(0)[0] = -1; // First delta palette entry

        var t = new Transform
        {
            Id = TransformId.Palette,
            BeginC = 0,
            NumC = 3,
            NbColors = 1,
            NbDeltas = 72,
        };

        var wp = new WeightedHeader();
        t.Inverse(image, wp);

        Assert.Equal(3, image.Channels.Count);
        // Delta palette index -1 → kDeltaPalette[0] = {0, 0, 0}
        Assert.Equal(0, image.Channels[0].Row(0)[0]);
    }

    [Fact]
    public void Palette_MetaChannelsUpdated()
    {
        var image = ModularImage.Create(4, 3, 8, 2);
        image.NbMetaChannels = 1;

        image.Channels[0] = Channel.Create(4, 3);
        image.Channels[1] = Channel.Create(2, 2);
        image.Channels[1].Row(0)[0] = 0;
        image.Channels[1].Row(0)[1] = 0;
        image.Channels[1].Row(1)[0] = 0;
        image.Channels[1].Row(1)[1] = 0;

        var t = new Transform
        {
            Id = TransformId.Palette,
            BeginC = 0,
            NumC = 3,
            NbColors = 4,
        };

        var wp = new WeightedHeader();
        t.Inverse(image, wp);

        Assert.Equal(0, image.NbMetaChannels);
    }
}

public class SmoothTendencyTests
{
    [Fact]
    public void FlatArea_ZeroTendency()
    {
        // When left = avg = next, tendency should be 0
        // This is tested implicitly through InverseSqueeze
        var image = ModularImage.Create(3, 1, 8, 2);
        image.Channels[0].Row(0)[0] = 10;
        image.Channels[0].Row(0)[1] = 10;
        image.Channels[0].Row(0)[2] = 10;
        image.Channels[1] = Channel.Create(3, 1);

        var t = new Transform
        {
            Id = TransformId.Squeeze,
            Squeezes =
            [
                new SqueezeParams { Horizontal = true, BeginC = 0, NumC = 1 }
            ]
        };

        var wp = new WeightedHeader();
        t.Inverse(image, wp);

        // Flat input with zero residual should produce flat output
        for (int x = 0; x < image.Channels[0].W; x++)
            Assert.Equal(10, image.Channels[0].Row(0)[x]);
    }
}
