using LibJxl.Decoder;
using LibJxl.Fields;
using LibJxl.Modular;
using Xunit;

namespace LibJxl.Tests.Decoder;

public class ModularStreamIdTests
{
    [Fact]
    public void Global_ReturnsZero()
    {
        var id = ModularStreamId.Global();
        var fd = new FrameDimensions();
        fd.Set(64, 64, 1, 0, 0, true, 1);
        Assert.Equal(0, id.ID(fd));
    }

    [Fact]
    public void VarDctDC_ReturnsCorrectId()
    {
        var id = ModularStreamId.VarDctDC(2);
        var fd = new FrameDimensions();
        fd.Set(64, 64, 1, 0, 0, false, 1);
        Assert.Equal(1 + 2, id.ID(fd)); // 1 + groupId
    }

    [Fact]
    public void ModularDC_ReturnsCorrectId()
    {
        var id = ModularStreamId.ModularDC(0);
        var fd = new FrameDimensions();
        fd.Set(64, 64, 1, 0, 0, true, 1);
        int expected = 1 + fd.NumDcGroups + 0; // 1 + numDcGroups + groupId
        Assert.Equal(expected, id.ID(fd));
    }

    [Fact]
    public void AcMetadata_ReturnsCorrectId()
    {
        var id = ModularStreamId.AcMetadata(1);
        var fd = new FrameDimensions();
        fd.Set(64, 64, 1, 0, 0, false, 1);
        int expected = 1 + 2 * fd.NumDcGroups + 1;
        Assert.Equal(expected, id.ID(fd));
    }

    [Fact]
    public void ModularAC_ReturnsCorrectId()
    {
        var id = ModularStreamId.ModularAC(3, 0);
        var fd = new FrameDimensions();
        fd.Set(64, 64, 1, 0, 0, true, 1);
        int expected = 1 + 3 * fd.NumDcGroups + DequantMatrices.kNumQuantTables
                       + fd.NumGroups * 0 + 3;
        Assert.Equal(expected, id.ID(fd));
    }

    [Fact]
    public void StreamIds_AreUnique()
    {
        var fd = new FrameDimensions();
        fd.Set(512, 512, 1, 0, 0, false, 1);

        var ids = new HashSet<int>();
        ids.Add(ModularStreamId.Global().ID(fd));

        for (int g = 0; g < fd.NumDcGroups; g++)
        {
            ids.Add(ModularStreamId.VarDctDC(g).ID(fd));
            ids.Add(ModularStreamId.ModularDC(g).ID(fd));
            ids.Add(ModularStreamId.AcMetadata(g).ID(fd));
        }

        // All should be unique
        int expectedCount = 1 + 3 * fd.NumDcGroups;
        Assert.Equal(expectedCount, ids.Count);
    }
}

public class ModularFrameDecoderTests
{
    [Fact]
    public void Constructor_InitialState()
    {
        var decoder = new ModularFrameDecoder();
        Assert.Null(decoder.FullImage);
        Assert.False(decoder.HasGlobalInfo);
    }

    [Fact]
    public void CopyToFloat_NullImage_ReturnsFalse()
    {
        var decoder = new ModularFrameDecoder();
        var fh = new FrameHeader();
        var output = new float[3][][];
        Assert.False(decoder.CopyToFloat(fh, output));
    }

    [Fact]
    public void FinalizeDecoding_NullImage_ReturnsFalse()
    {
        var decoder = new ModularFrameDecoder();
        var fh = new FrameHeader();
        Assert.False(decoder.FinalizeDecoding(fh));
    }

    [Fact]
    public void DecodeGroup_NullImage_ReturnsFalse()
    {
        var decoder = new ModularFrameDecoder();
        var fh = new FrameHeader();
        // No DecodeGlobalInfo called → _fullImage is null
        Assert.False(decoder.DecodeGroup(null!, fh, 0, 0, 3,
            ModularStreamId.ModularDC(0)));
    }
}

public class ModularImageIntegrationTests
{
    [Fact]
    public void ModularImage_Create_HasCorrectChannels()
    {
        var img = ModularImage.Create(64, 48, 8, 4);
        Assert.Equal(64, img.W);
        Assert.Equal(48, img.H);
        Assert.Equal(8, img.BitDepth);
        Assert.Equal(4, img.Channels.Count);

        foreach (var ch in img.Channels)
        {
            Assert.Equal(64, ch.W);
            Assert.Equal(48, ch.H);
        }
    }

    [Fact]
    public void ModularImage_UndoTransforms_EmptyList_Succeeds()
    {
        var img = ModularImage.Create(8, 8, 8, 3);
        var wpHeader = new WeightedHeader();
        img.UndoTransforms(wpHeader);
        // No transforms → no error, channels unchanged
        Assert.Equal(3, img.Channels.Count);
    }

    [Fact]
    public void Channel_Shrink_ReducesDimensions()
    {
        var ch = Channel.Create(16, 16);
        Assert.Equal(16, ch.W);
        Assert.Equal(16, ch.H);

        ch.Shrink(8, 4);
        Assert.Equal(8, ch.W);
        Assert.Equal(4, ch.H);
    }

    [Fact]
    public void Channel_Row_AccessesData()
    {
        var ch = Channel.Create(4, 4);
        ch.Row(0)[0] = 42;
        ch.Row(3)[3] = 99;

        Assert.Equal(42, ch.Row(0)[0]);
        Assert.Equal(99, ch.Row(3)[3]);
    }

    [Fact]
    public void Transform_InverseRCT_Identity()
    {
        // RCT type 0 = identity (no transform)
        var img = ModularImage.Create(4, 4, 8, 3);
        for (int c = 0; c < 3; c++)
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    img.Channels[c].Row(y)[x] = (c + 1) * 10 + y * 4 + x;

        var transform = new Transform { Id = TransformId.RCT, BeginC = 0, RctType = 0 };
        transform.Inverse(img, new WeightedHeader());

        // Identity: values should be unchanged
        Assert.Equal(10, img.Channels[0].Row(0)[0]); // c=0, y=0, x=0
        Assert.Equal(20, img.Channels[1].Row(0)[0]); // c=1
        Assert.Equal(30, img.Channels[2].Row(0)[0]); // c=2
    }

    [Fact]
    public void Transform_InverseRCT_YCoCg()
    {
        // RCT type 6 = YCoCg (the most common)
        var img = ModularImage.Create(2, 1, 8, 3);

        // Set encoded values (Y=100, Co=20, Cg=10)
        img.Channels[0].Row(0)[0] = 100; // a (Y-like)
        img.Channels[1].Row(0)[0] = 20;  // b (Co-like)
        img.Channels[2].Row(0)[0] = 10;  // c (Cg-like)

        var transform = new Transform { Id = TransformId.RCT, BeginC = 0, RctType = 6 };
        transform.Inverse(img, new WeightedHeader());

        // After YCoCg inverse (type 6):
        // ra = a - (b >> 1) = 100 - 10 = 90
        // rc = ra - c = 90 - 10 = 80
        // rb = b + rc = 20 + 80 = 100
        Assert.Equal(90, img.Channels[0].Row(0)[0]);
        Assert.Equal(100, img.Channels[1].Row(0)[0]);
        Assert.Equal(80, img.Channels[2].Row(0)[0]);
    }
}

public class PropertyDecisionNodeTests
{
    [Fact]
    public void Leaf_IsLeaf()
    {
        var node = PropertyDecisionNode.Leaf(Predictor.Zero);
        Assert.True(node.IsLeaf);
        Assert.Equal(Predictor.Zero, node.Predictor);
    }

    [Fact]
    public void Split_IsNotLeaf()
    {
        var node = PropertyDecisionNode.Split(0, 5, 1, 2);
        Assert.False(node.IsLeaf);
        Assert.Equal(0, node.Property);
        Assert.Equal(5, node.SplitVal);
    }

    [Fact]
    public void Leaf_WithParams()
    {
        var node = PropertyDecisionNode.Leaf(Predictor.Left, offset: 3, multiplier: 2);
        Assert.True(node.IsLeaf);
        Assert.Equal(Predictor.Left, node.Predictor);
        Assert.Equal(3, node.PredictorOffset);
        Assert.Equal(2, node.Multiplier);
    }

    [Fact]
    public void MATreeLookup_SingleLeaf()
    {
        var tree = new List<PropertyDecisionNode>
        {
            PropertyDecisionNode.Leaf(Predictor.Top, offset: 5, multiplier: 1)
        };

        int[] props = new int[16];
        var (ctx, pred, offset, mul) = MATreeDecoder.Lookup(tree, props);

        Assert.Equal(0, ctx);
        Assert.Equal(Predictor.Top, pred);
        Assert.Equal(5, offset);
        Assert.Equal(1, mul);
    }

    [Fact]
    public void MATreeLookup_BinaryTree()
    {
        // Tree: if property[0] <= 5, go left (leaf=Zero), else right (leaf=Left)
        var tree = new List<PropertyDecisionNode>
        {
            PropertyDecisionNode.Split(0, 5, 1, 2),
            PropertyDecisionNode.Leaf(Predictor.Zero),
            PropertyDecisionNode.Leaf(Predictor.Left),
        };

        int[] props = new int[16];

        // property[0] = 3 <= 5 → left (Predictor.Zero)
        props[0] = 3;
        var (ctx1, pred1, _, _) = MATreeDecoder.Lookup(tree, props);
        Assert.Equal(Predictor.Zero, pred1);

        // property[0] = 10 > 5 → right (Predictor.Left)
        props[0] = 10;
        var (ctx2, pred2, _, _) = MATreeDecoder.Lookup(tree, props);
        Assert.Equal(Predictor.Left, pred2);
    }
}
