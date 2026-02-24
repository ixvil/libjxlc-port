// Port of lib/jxl/modular/encoding/encoding.cc — modular generic decompression
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;
using LibJxl.Fields;

namespace LibJxl.Modular;

/// <summary>
/// Group header for modular coding.
/// Port of jxl::GroupHeader.
/// </summary>
public class GroupHeader
{
    public bool UseGlobalTree;
    public WeightedHeader WpHeader = new();
    public List<Transform> Transforms = [];

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        UseGlobalTree = FieldReader.ReadBool(br);

        var wpStatus = WpHeader.ReadFromBitStream(br);
        if (!wpStatus) return false;

        // Read transforms
        int numTransforms = (int)FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Val(1),
            U32Distr.BitsOffset(2, 2), U32Distr.BitsOffset(4, 6));

        for (int i = 0; i < numTransforms; i++)
        {
            var t = new Transform();
            var s = t.ReadFromBitStream(br);
            if (!s) return false;
            Transforms.Add(t);
        }

        return true;
    }
}

/// <summary>
/// Main modular decoder. Decompresses channels from the bitstream using
/// MA trees, predictors, and ANS entropy coding.
/// Port of jxl::ModularGenericDecompress.
/// </summary>
public static class ModularDecoder
{
    /// <summary>
    /// Decompresses a modular image from the bitstream.
    /// </summary>
    public static JxlStatus Decompress(
        BitReader br,
        ModularImage image,
        GroupHeader? header,
        int groupId,
        bool undoTransforms = true,
        List<PropertyDecisionNode>? globalTree = null,
        ANSCode? code = null,
        byte[]? ctxMap = null)
    {
        // Read group header if not provided
        if (header == null)
        {
            header = new GroupHeader();
            var hdrStatus = header.ReadFromBitStream(br);
            if (!hdrStatus) return false;
        }

        // Apply forward transforms to set up channels
        foreach (var t in header.Transforms)
        {
            image.Transforms.Add(t);
        }

        // Decode tree
        List<PropertyDecisionNode> tree;
        if (header.UseGlobalTree && globalTree != null)
        {
            tree = globalTree;
        }
        else
        {
            var treeStatus = MATreeDecoder.DecodeTree(br, out tree);
            if (!treeStatus) return false;
        }

        // Decode ANS histograms if not provided
        if (code == null)
        {
            int numContexts = CountLeaves(tree);
            code = new ANSCode();
            var histStatus = HistogramDecoder.DecodeHistograms(
                br, numContexts, code, out byte[] decodedCtxMap);
            if (!histStatus) return false;
            ctxMap = decodedCtxMap;
        }

        var reader = ANSSymbolReader.Create(code, br);

        // Decode each channel
        bool usesWeighted = TreeUsesPredictor(tree, Predictor.Weighted);
        var wpState = usesWeighted ? new WeightedState(header.WpHeader, image.W) : null;

        for (int ch = 0; ch < image.Channels.Count; ch++)
        {
            var channel = image.Channels[ch];
            if (channel.W == 0 || channel.H == 0) continue;

            var decStatus = DecodeChannel(
                ch, groupId, channel, tree, reader, br, ctxMap!, header.WpHeader, wpState);
            if (!decStatus) return false;
        }

        if (!reader.CheckANSFinalState())
            return false;

        // Undo transforms if requested
        if (undoTransforms)
        {
            image.UndoTransforms(header.WpHeader);
        }

        return true;
    }

    /// <summary>
    /// Decodes a single channel pixel by pixel.
    /// </summary>
    private static JxlStatus DecodeChannel(
        int channelIdx, int groupId, Channel channel,
        List<PropertyDecisionNode> tree,
        ANSSymbolReader reader, BitReader br, byte[] ctxMap,
        WeightedHeader wpHeader, WeightedState? wpState)
    {
        int w = channel.W;
        int h = channel.H;

        // Property array: [channel, group_id, ...computed props...]
        int[] properties = new int[PropertyConstants.NumNonrefProperties];

        for (int y = 0; y < h; y++)
        {
            var row = channel.Row(y);
            var rowAbove = y > 0 ? channel.Row(y - 1) : Span<int>.Empty;
            var rowAbove2 = y > 1 ? channel.Row(y - 2) : Span<int>.Empty;

            // Reset weighted predictor for new row if needed
            if (wpState != null && y > 0)
            {
                wpState = new WeightedState(wpHeader, w);
            }

            for (int x = 0; x < w; x++)
            {
                // Get neighbor values
                int left = x > 0 ? row[x - 1] : (y > 0 ? rowAbove[x] : 0);
                int top = y > 0 ? rowAbove[x] : left;
                int topleft = (x > 0 && y > 0) ? rowAbove[x - 1] : left;
                int topright = (x < w - 1 && y > 0) ? rowAbove[x + 1] : top;
                int leftleft = x > 1 ? row[x - 2] : left;
                int toptop = y > 1 ? rowAbove2[x] : top;

                // Compute static and spatial properties
                properties[0] = channelIdx;
                properties[1] = groupId;

                // Spatial properties
                if (tree.Count > 1)
                {
                    properties[2] = y;
                    properties[3] = x;
                    properties[4] = Math.Abs(top);
                    properties[5] = Math.Abs(left);
                    properties[6] = top;
                    properties[7] = left;
                    properties[8] = left - Math.Abs(top);
                    properties[9] = left + top - topleft;
                    properties[10] = left - topleft;
                    properties[11] = topleft - top;
                    properties[12] = top - topright;
                    properties[13] = top - toptop;
                    properties[14] = left - leftleft;
                    properties[15] = 0; // WP error (simplified)
                }

                // Compute weighted prediction if needed
                long wpPred = 0;
                if (wpState != null)
                {
                    int nn = y > 1 ? rowAbove2[x] : top;
                    int nw = (x > 0 && y > 0) ? rowAbove[x - 1] : top;
                    int ne = (x < w - 1 && y > 0) ? rowAbove[x + 1] : top;
                    wpPred = wpState.Predict(x, y, w, top, left, ne, nw, nn);
                }

                // Tree lookup
                var (ctx, predictor, offset, multiplier) =
                    MATreeLookup(tree, properties);

                // Read value from ANS
                uint symbol = reader.ReadHybridUint(ctx, br, ctxMap);
                int value = SignedPack.UnpackSigned(symbol);

                // Compute prediction
                long guess = PredictionFunctions.PredictOne(
                    predictor, left, top, toptop, topleft, topright, leftleft, wpPred);

                // Apply multiplier and offset
                int pixel = (int)((long)value * multiplier + offset + guess);
                row[x] = pixel;

                // Update weighted predictor
                wpState?.UpdateErrors(x, pixel, guess);
            }
        }

        return true;
    }

    /// <summary>Counts the number of leaf nodes (contexts) in the tree.</summary>
    private static int CountLeaves(List<PropertyDecisionNode> tree)
    {
        int count = 0;
        foreach (var node in tree)
        {
            if (node.IsLeaf) count++;
        }
        return Math.Max(count, 1);
    }

    /// <summary>Checks if any leaf in the tree uses the given predictor.</summary>
    private static bool TreeUsesPredictor(List<PropertyDecisionNode> tree, Predictor p)
    {
        foreach (var node in tree)
        {
            if (node.IsLeaf && node.Predictor == p) return true;
        }
        return false;
    }

    /// <summary>Simple tree traversal for lookup.</summary>
    private static (int context, Predictor predictor, long offset, int multiplier)
        MATreeLookup(List<PropertyDecisionNode> tree, int[] properties)
    {
        int pos = 0;
        int leafIdx = 0;

        while (true)
        {
            var node = tree[pos];
            if (node.IsLeaf)
            {
                return (leafIdx, node.Predictor, node.PredictorOffset, node.Multiplier);
            }

            // Count leaves visited before this branch to compute context index
            int prop = node.Property;
            int val = prop < properties.Length ? properties[prop] : 0;

            if (val <= node.SplitVal)
            {
                pos = node.LeftChild;
            }
            else
            {
                pos = node.RightChild;
            }
        }
    }
}
