using LibJxl.Modular;
using Xunit;

namespace LibJxl.Tests.Modular;

public class PredictorTests
{
    [Fact]
    public void ClampedGradient_NoClamp()
    {
        // top=10, left=20, topleft=15 → grad = 10+20-15 = 15
        Assert.Equal(15, PredictionFunctions.ClampedGradient(10, 20, 15));
    }

    [Fact]
    public void ClampedGradient_ClampLow()
    {
        // top=0, left=0, topleft=100 → grad = -100, clamped to min(0,0) = 0
        Assert.Equal(0, PredictionFunctions.ClampedGradient(0, 0, 100));
    }

    [Fact]
    public void PredictOne_Zero()
    {
        Assert.Equal(0L, PredictionFunctions.PredictOne(
            Predictor.Zero, 10, 20, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void PredictOne_Left()
    {
        Assert.Equal(42L, PredictionFunctions.PredictOne(
            Predictor.Left, 42, 20, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void PredictOne_Top()
    {
        Assert.Equal(33L, PredictionFunctions.PredictOne(
            Predictor.Top, 10, 33, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void PredictOne_Average0()
    {
        // (left + top) / 2 = (10 + 20) / 2 = 15
        Assert.Equal(15L, PredictionFunctions.PredictOne(
            Predictor.Average0, 10, 20, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void PredictOne_Gradient()
    {
        // ClampedGradient(top=10, left=20, topleft=15) = 15
        Assert.Equal(15L, PredictionFunctions.PredictOne(
            Predictor.Gradient, 20, 10, 0, 15, 0, 0, 0));
    }

    [Fact]
    public void PredictOne_Select()
    {
        // p = left + top - topleft = 20 + 10 - 15 = 15
        // dLeft = |15-20| = 5, dTop = |15-10| = 5
        // dLeft >= dTop → return top (10)
        Assert.Equal(10L, PredictionFunctions.PredictOne(
            Predictor.Select, 20, 10, 0, 15, 0, 0, 0));
    }
}

public class ChannelTests
{
    [Fact]
    public void Create_And_AccessRows()
    {
        var ch = Channel.Create(4, 3);
        Assert.Equal(4, ch.W);
        Assert.Equal(3, ch.H);

        ch.Row(0)[0] = 42;
        Assert.Equal(42, ch.Row(0)[0]);
    }

    [Fact]
    public void Shrink_SmallToBig()
    {
        var ch = Channel.Create(4, 4);
        ch.Row(0)[0] = 100;
        ch.Row(0)[1] = 200;

        ch.Shrink(2, 2);
        Assert.Equal(2, ch.W);
        Assert.Equal(2, ch.H);
        Assert.Equal(100, ch.Row(0)[0]);
        Assert.Equal(200, ch.Row(0)[1]);
    }
}

public class ModularImageTests
{
    [Fact]
    public void Create_ThreeChannels()
    {
        var img = ModularImage.Create(8, 8, 8, 3);
        Assert.Equal(3, img.Channels.Count);
        Assert.Equal(8, img.Channels[0].W);
        Assert.Equal(8, img.Channels[0].H);
    }
}

public class PropertyDecisionNodeTests
{
    [Fact]
    public void Leaf_IsLeaf()
    {
        var leaf = PropertyDecisionNode.Leaf(Predictor.Gradient, 5, 2);
        Assert.True(leaf.IsLeaf);
        Assert.Equal(Predictor.Gradient, leaf.Predictor);
        Assert.Equal(5L, leaf.PredictorOffset);
        Assert.Equal(2, leaf.Multiplier);
    }

    [Fact]
    public void Split_IsNotLeaf()
    {
        var split = PropertyDecisionNode.Split(3, 42, 1, 2);
        Assert.False(split.IsLeaf);
        Assert.Equal(3, split.Property);
        Assert.Equal(42, split.SplitVal);
    }
}

public class MATreeLookupTests
{
    [Fact]
    public void SingleLeaf_ReturnsLeaf()
    {
        var tree = new List<PropertyDecisionNode>
        {
            PropertyDecisionNode.Leaf(Predictor.Zero, 0, 1)
        };

        var (ctx, pred, offset, mul) = MATreeDecoder.Lookup(tree, [0, 0]);
        Assert.Equal(Predictor.Zero, pred);
        Assert.Equal(0L, offset);
        Assert.Equal(1, mul);
    }

    [Fact]
    public void TwoLevel_BranchLeft()
    {
        var tree = new List<PropertyDecisionNode>
        {
            PropertyDecisionNode.Split(0, 5, 1, 2),          // if prop[0] <= 5 → left
            PropertyDecisionNode.Leaf(Predictor.Left, 10, 1), // left child
            PropertyDecisionNode.Leaf(Predictor.Top, 20, 1),  // right child
        };

        // prop[0] = 3 <= 5 → left child
        var (ctx, pred, offset, mul) = MATreeDecoder.Lookup(tree, [3, 0]);
        Assert.Equal(Predictor.Left, pred);
        Assert.Equal(10L, offset);
    }

    [Fact]
    public void TwoLevel_BranchRight()
    {
        var tree = new List<PropertyDecisionNode>
        {
            PropertyDecisionNode.Split(0, 5, 1, 2),
            PropertyDecisionNode.Leaf(Predictor.Left, 10, 1),
            PropertyDecisionNode.Leaf(Predictor.Top, 20, 1),
        };

        // prop[0] = 10 > 5 → right child
        var (ctx, pred, offset, mul) = MATreeDecoder.Lookup(tree, [10, 0]);
        Assert.Equal(Predictor.Top, pred);
        Assert.Equal(20L, offset);
    }
}

public class TransformTests
{
    [Fact]
    public void InverseRCT_Identity()
    {
        var img = ModularImage.Create(2, 2, 8, 3);
        // Set some values
        img.Channels[0].Row(0)[0] = 100;
        img.Channels[1].Row(0)[0] = 50;
        img.Channels[2].Row(0)[0] = 25;

        var t = new Transform { Id = TransformId.RCT, BeginC = 0, RctType = 0 };
        var status = t.Inverse(img, new WeightedHeader());
        Assert.True(status);

        // RCT type 0 = identity
        Assert.Equal(100, img.Channels[0].Row(0)[0]);
        Assert.Equal(50, img.Channels[1].Row(0)[0]);
        Assert.Equal(25, img.Channels[2].Row(0)[0]);
    }
}

public class WeightedHeaderTests
{
    [Fact]
    public void ReadAllDefault()
    {
        var writer = new LibJxl.Bitstream.BitWriter();
        writer.Write(1, 1); // all_default
        writer.ZeroPadToByte();

        using var reader = new LibJxl.Bitstream.BitReader(writer.GetSpan().ToArray());
        var header = new WeightedHeader();
        var status = header.ReadFromBitStream(reader);
        Assert.True(status);
        Assert.True(header.AllDefault);
        reader.Close();
    }
}

public class GroupHeaderTests
{
    [Fact]
    public void ReadNoTransforms()
    {
        var writer = new LibJxl.Bitstream.BitWriter();
        writer.Write(1, 1); // use_global_tree = true
        writer.Write(1, 1); // wp_header all_default = true
        writer.Write(2, 0); // num_transforms selector 0 = Val(0) = 0
        writer.ZeroPadToByte();

        using var reader = new LibJxl.Bitstream.BitReader(writer.GetSpan().ToArray());
        var header = new GroupHeader();
        var status = header.ReadFromBitStream(reader);
        Assert.True(status);
        Assert.True(header.UseGlobalTree);
        Assert.Empty(header.Transforms);
        reader.Close();
    }
}
