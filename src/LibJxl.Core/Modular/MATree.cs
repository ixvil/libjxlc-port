// Port of lib/jxl/modular/encoding/dec_ma.h/cc — meta-adaptive decision tree
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;
using LibJxl.Fields;

namespace LibJxl.Modular;

/// <summary>
/// A node in the meta-adaptive decision tree.
/// Port of jxl::PropertyDecisionNode.
/// </summary>
public struct PropertyDecisionNode
{
    public int SplitVal;
    public short Property;     // -1 = leaf node
    public int LeftChild;
    public int RightChild;
    public Predictor Predictor;
    public long PredictorOffset;
    public int Multiplier;

    public bool IsLeaf => Property < 0;

    public static PropertyDecisionNode Leaf(Predictor predictor, long offset = 0, int multiplier = 1)
    {
        return new PropertyDecisionNode
        {
            Property = -1,
            Predictor = predictor,
            PredictorOffset = offset,
            Multiplier = multiplier,
        };
    }

    public static PropertyDecisionNode Split(int property, int splitVal, int lchild, int rchild)
    {
        return new PropertyDecisionNode
        {
            Property = (short)property,
            SplitVal = splitVal,
            LeftChild = lchild,
            RightChild = rchild,
        };
    }
}

/// <summary>Tree context IDs for decoding the MA tree structure.</summary>
public static class MATreeContexts
{
    public const int SplitVal = 0;
    public const int Property = 1;
    public const int PredictorCtx = 2;
    public const int Offset = 3;
    public const int MultiplierLog = 4;
    public const int MultiplierBits = 5;
    public const int NumTreeContexts = 6;
}

/// <summary>
/// Decodes a meta-adaptive decision tree from the bitstream.
/// Port of jxl::DecodeTree from dec_ma.cc.
/// </summary>
public static class MATreeDecoder
{
    private const int MaxTreeHeight = 2048;
    private const int MaxTreeNodes = 1 << 22; // ~4M

    /// <summary>
    /// Decodes a MA tree from the bitstream.
    /// </summary>
    public static JxlStatus DecodeTree(BitReader br, out List<PropertyDecisionNode> tree, int limit = MaxTreeNodes)
    {
        tree = [];

        var code = new ANSCode();
        var histStatus = HistogramDecoder.DecodeHistograms(
            br, MATreeContexts.NumTreeContexts, code, out byte[] ctxMap);
        if (!histStatus) return false;

        var reader = ANSSymbolReader.Create(code, br);

        // BFS queue: (parent_index, is_right_child, depth)
        var queue = new Queue<(int parent, bool right, int depth)>();

        // Read root
        var rootStatus = DecodeNode(reader, br, ctxMap, tree, 0);
        if (!rootStatus) return false;

        if (!tree[0].IsLeaf)
        {
            queue.Enqueue((0, false, 1)); // left child
            queue.Enqueue((0, true, 1));  // right child
        }

        while (queue.Count > 0)
        {
            var (parent, isRight, depth) = queue.Dequeue();
            if (depth > MaxTreeHeight) return false;
            if (tree.Count >= limit) return false;

            int nodeIdx = tree.Count;
            var nodeStatus = DecodeNode(reader, br, ctxMap, tree, nodeIdx);
            if (!nodeStatus) return false;

            // Link parent to this child
            if (isRight)
            {
                var p = tree[parent];
                p.RightChild = nodeIdx;
                tree[parent] = p;
            }
            else
            {
                var p = tree[parent];
                p.LeftChild = nodeIdx;
                tree[parent] = p;
            }

            if (!tree[nodeIdx].IsLeaf)
            {
                queue.Enqueue((nodeIdx, false, depth + 1));
                queue.Enqueue((nodeIdx, true, depth + 1));
            }
        }

        if (!reader.CheckANSFinalState())
            return false;

        return true;
    }

    private static JxlStatus DecodeNode(ANSSymbolReader reader, BitReader br,
                                         byte[] ctxMap, List<PropertyDecisionNode> tree, int nodeIdx)
    {
        int property = (int)reader.ReadHybridUint(MATreeContexts.Property, br, ctxMap) - 1;

        if (property < 0)
        {
            // Leaf node
            int predictorIdx = (int)reader.ReadHybridUint(MATreeContexts.PredictorCtx, br, ctxMap);
            if (predictorIdx >= (int)Predictor.NumPredictors) return false;

            uint offsetRaw = reader.ReadHybridUint(MATreeContexts.Offset, br, ctxMap);
            long offset = SignedPack.UnpackSigned(offsetRaw);

            uint mulLog = reader.ReadHybridUint(MATreeContexts.MultiplierLog, br, ctxMap);
            uint mulBits = 0;
            if (mulLog > 0)
                mulBits = reader.ReadHybridUint(MATreeContexts.MultiplierBits, br, ctxMap);

            int multiplier = (int)((mulBits + 1) << (int)mulLog);

            tree.Add(PropertyDecisionNode.Leaf(
                (Predictor)predictorIdx, offset, multiplier));
        }
        else
        {
            // Decision node
            uint splitRaw = reader.ReadHybridUint(MATreeContexts.SplitVal, br, ctxMap);
            int splitVal = SignedPack.UnpackSigned(splitRaw);

            tree.Add(PropertyDecisionNode.Split(property, splitVal, 0, 0));
        }

        return true;
    }

    /// <summary>
    /// Traverses the tree to find prediction parameters for given properties.
    /// </summary>
    public static (int context, Predictor predictor, long offset, int multiplier)
        Lookup(List<PropertyDecisionNode> tree, int[] properties)
    {
        int pos = 0;
        int ctx = 0;

        while (true)
        {
            var node = tree[pos];
            if (node.IsLeaf)
            {
                return (ctx, node.Predictor, node.PredictorOffset, node.Multiplier);
            }

            if (node.Property < properties.Length && properties[node.Property] <= node.SplitVal)
            {
                pos = node.LeftChild;
            }
            else
            {
                pos = node.RightChild;
            }
            ctx++;
        }
    }
}
