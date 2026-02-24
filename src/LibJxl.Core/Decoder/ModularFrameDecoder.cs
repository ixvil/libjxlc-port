// Port of lib/jxl/dec_modular.h/cc — ModularFrameDecoder
// Orchestrates modular (lossless) decoding: global info, group data, finalization.

using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;
using LibJxl.Fields;
using LibJxl.Modular;

namespace LibJxl.Decoder;

/// <summary>
/// Identifies the type of modular stream.
/// Port of jxl::ModularStreamId.
/// </summary>
public struct ModularStreamId
{
    public enum Kind
    {
        GlobalData = 0,
        VarDCTDC = 1,
        ModularDC = 2,
        ACMetadata = 3,
        QuantTable = 4,
        ModularAC = 5,
    }

    public Kind StreamKind;
    public int GroupId;
    public int PassId;

    public static ModularStreamId Global() =>
        new() { StreamKind = Kind.GlobalData };

    public static ModularStreamId ModularDC(int groupId) =>
        new() { StreamKind = Kind.ModularDC, GroupId = groupId };

    public static ModularStreamId VarDctDC(int groupId) =>
        new() { StreamKind = Kind.VarDCTDC, GroupId = groupId };

    public static ModularStreamId AcMetadata(int groupId) =>
        new() { StreamKind = Kind.ACMetadata, GroupId = groupId };

    public static ModularStreamId QuantTable(int tableId) =>
        new() { StreamKind = Kind.QuantTable, GroupId = tableId };

    public static ModularStreamId ModularAC(int groupId, int passId) =>
        new() { StreamKind = Kind.ModularAC, GroupId = groupId, PassId = passId };

    /// <summary>
    /// Computes a unique stream ID from dimensions (for TOC mapping).
    /// Port of ModularStreamId::ID.
    /// </summary>
    public int ID(FrameDimensions fd)
    {
        int numDcGroups = fd.NumDcGroups;
        int numGroups = fd.NumGroups;

        return StreamKind switch
        {
            Kind.GlobalData => 0,
            Kind.VarDCTDC => 1 + GroupId,
            Kind.ModularDC => 1 + numDcGroups + GroupId,
            Kind.ACMetadata => 1 + 2 * numDcGroups + GroupId,
            Kind.QuantTable => 1 + 3 * numDcGroups + GroupId,
            Kind.ModularAC => 1 + 3 * numDcGroups + DequantMatrices.kNumQuantTables
                               + numGroups * PassId + GroupId,
            _ => 0,
        };
    }
}

/// <summary>
/// Orchestrates modular decoding for a frame.
/// Port of jxl::ModularFrameDecoder.
///
/// Three phases:
/// 1. DecodeGlobalInfo — decode tree, ANS histograms, global transforms, DC channels
/// 2. DecodeGroup — per-group: decode channels using global tree/histograms
/// 3. FinalizeDecoding — undo transforms, convert int→float, feed render pipeline
/// </summary>
public class ModularFrameDecoder
{
    // Global decoded image (DC or full image)
    private ModularImage? _fullImage;

    // Global MA tree (shared by all groups that set UseGlobalTree)
    private List<PropertyDecisionNode>? _globalTree;

    // Global ANS code and context map (shared by groups)
    private ANSCode? _globalCode;
    private byte[]? _globalCtxMap;

    // Global group header (weighted predictor params)
    private GroupHeader? _globalHeader;

    // Transforms that apply at group level (moved from global for optimization)
    private readonly List<Transform> _globalTransforms = new();

    // Frame info
    private FrameDimensions _frameDim = new();
    private bool _doColor;        // Whether this frame has color channels
    private bool _haveSomething;  // Whether global image has any data to process
    private bool _useFullImage;   // Whether to store data in full image (vs streaming)
    private int _nbChannels;      // Total channels (color + extra)
    private int _nbExtraChannels; // Extra channels (alpha, depth, etc.)

    /// <summary>The full modular image (null until DecodeGlobalInfo).</summary>
    public ModularImage? FullImage => _fullImage;

    /// <summary>Whether global info has been decoded.</summary>
    public bool HasGlobalInfo => _fullImage != null;

    /// <summary>
    /// Decodes global modular information: MA tree, ANS histograms, transforms, and DC channels.
    /// Port of ModularFrameDecoder::DecodeGlobalInfo.
    /// </summary>
    public JxlStatus DecodeGlobalInfo(BitReader br, FrameHeader fh, bool allowExtraChannels = true)
    {
        _frameDim = fh.ToFrameDimensions();

        // Determine channel configuration
        bool xyb = fh.Transform == ColorTransform.XYB;
        bool isModular = fh.Encoding == FrameEncoding.Modular;

        // Color channels: 3 for VarDCT (always), 0 or 3 for modular depending on xyb
        _doColor = isModular;
        int nbColorChannels = isModular ? 3 : 0;

        _nbExtraChannels = allowExtraChannels
            ? (int)(fh.NonserializedMetadata?.NumExtraChannels ?? 0)
            : 0;
        _nbChannels = nbColorChannels + _nbExtraChannels;

        if (_nbChannels == 0)
        {
            // Nothing to decode (VarDCT with no extra channels uses separate DC decode)
            _fullImage = ModularImage.Create(0, 0, 8, 0);
            return true;
        }

        // Create the full modular image
        int xsize = _frameDim.XSize;
        int ysize = _frameDim.YSize;
        int bitdepth = (int)(fh.NonserializedMetadata?.BitDepth.BitsPerSample ?? 8);

        _fullImage = ModularImage.Create(xsize, ysize, bitdepth, _nbChannels);

        // Set chroma subsampling shifts for color channels
        if (_doColor && fh.Transform == ColorTransform.YCbCr)
        {
            for (int c = 0; c < Math.Min(3, _fullImage.Channels.Count); c++)
            {
                var ch = _fullImage.Channels[c];
                int hs = fh.ChromaSubsampling.IsHSubsampled(c) ? 1 : 0;
                int vs = fh.ChromaSubsampling.IsVSubsampled(c) ? 1 : 0;
                ch.HShift = hs;
                ch.VShift = vs;

                // Adjust channel dimensions for subsampling
                if (hs > 0 || vs > 0)
                {
                    int newW = (xsize + (1 << hs) - 1) >> hs;
                    int newH = (ysize + (1 << vs) - 1) >> vs;
                    ch.Shrink(newW, newH);
                }
            }
        }

        // Decode the global header + tree + histograms + channels
        _globalHeader = new GroupHeader();
        var headerStatus = _globalHeader.ReadFromBitStream(br);
        if (!headerStatus) return false;

        // Decode MA tree
        bool hasTree = FieldReader.ReadBool(br);
        if (hasTree)
        {
            int treeLimit = Math.Min(1 << 22, 1024 + (xsize * ysize * _nbChannels) / 16);
            treeLimit = Math.Max(treeLimit, 128);
            var treeStatus = MATreeDecoder.DecodeTree(br, out var tree, treeLimit);
            if (!treeStatus) return false;
            _globalTree = tree;
        }
        else
        {
            // Single-leaf tree: all pixels use the same predictor
            _globalTree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Leaf(Predictor.Zero)
            };
        }

        // Decode ANS histograms
        int numContexts = CountLeaves(_globalTree);
        _globalCode = new ANSCode();
        var histStatus = HistogramDecoder.DecodeHistograms(
            br, numContexts, _globalCode, out byte[] ctxMap);
        if (!histStatus) return false;
        _globalCtxMap = ctxMap;

        // Decode global channels using ModularDecoder
        var decompStatus = ModularDecoder.Decompress(
            br, _fullImage, _globalHeader, 0,
            undoTransforms: false, // Keep transforms for now
            globalTree: _globalTree,
            code: _globalCode,
            ctxMap: _globalCtxMap);
        if (!decompStatus) return false;

        // Determine if we need full image storage
        int groupDim = _frameDim.GroupDimValue;
        _haveSomething = false;
        _useFullImage = true;

        foreach (var ch in _fullImage.Channels)
        {
            if (ch.W > groupDim || ch.H > groupDim)
            {
                _haveSomething = true;
                break;
            }
        }

        // Optimization: if all channels fit in one group and there's a single RCT,
        // move the transform to group level
        if (!_haveSomething && _fullImage.Transforms.Count == 1 &&
            _fullImage.Transforms[0].Id == TransformId.RCT)
        {
            _globalTransforms.Add(_fullImage.Transforms[0]);
            _fullImage.Transforms.Clear();
            _useFullImage = false;
        }

        return true;
    }

    /// <summary>
    /// Decodes a group of modular channels.
    /// Port of ModularFrameDecoder::DecodeGroup.
    /// </summary>
    public JxlStatus DecodeGroup(
        BitReader br, FrameHeader fh,
        int groupId, int minShift, int maxShift,
        ModularStreamId streamId, bool zerofill = false)
    {
        if (_fullImage == null) return false;

        int groupDim = _frameDim.GroupDimValue;
        int xGroups = _frameDim.XSizeGroups;

        // Compute group rectangle
        int gx = groupId % xGroups;
        int gy = groupId / xGroups;
        int groupX = gx * groupDim;
        int groupY = gy * groupDim;
        int groupW = Math.Min(groupDim, _frameDim.XSize - groupX);
        int groupH = Math.Min(groupDim, _frameDim.YSize - groupY);

        if (groupW <= 0 || groupH <= 0) return true;

        if (zerofill)
        {
            // Zero-fill group channels
            foreach (var ch in _fullImage.Channels)
            {
                int shiftX = ch.HShift;
                int shiftY = ch.VShift;
                int cX = groupX >> shiftX;
                int cY = groupY >> shiftY;
                int cW = Math.Min((groupW + (1 << shiftX) - 1) >> shiftX, ch.W - cX);
                int cH = Math.Min((groupH + (1 << shiftY) - 1) >> shiftY, ch.H - cY);

                for (int y = cY; y < cY + cH && y < ch.H; y++)
                {
                    var row = ch.Row(y);
                    for (int x = cX; x < cX + cW && x < ch.W; x++)
                        row[x] = 0;
                }
            }
            return true;
        }

        // Build per-group image with just the channels in the [minShift, maxShift] range
        var groupChannels = new List<(int srcIdx, int chX, int chY, int chW, int chH)>();

        for (int c = 0; c < _fullImage.Channels.Count; c++)
        {
            var ch = _fullImage.Channels[c];
            int shift = Math.Min(ch.HShift, ch.VShift);
            if (shift < minShift || shift >= maxShift) continue;

            int cX = groupX >> ch.HShift;
            int cY = groupY >> ch.VShift;
            int cW = Math.Min((groupW + (1 << ch.HShift) - 1) >> ch.HShift, ch.W - cX);
            int cH = Math.Min((groupH + (1 << ch.VShift) - 1) >> ch.VShift, ch.H - cY);

            if (cW <= 0 || cH <= 0) continue;
            groupChannels.Add((c, cX, cY, cW, cH));
        }

        if (groupChannels.Count == 0) return true;

        // Create temporary image for this group
        var gi = ModularImage.Create(groupW, groupH, _fullImage.BitDepth, groupChannels.Count);
        for (int i = 0; i < groupChannels.Count; i++)
        {
            var (_, _, _, cW, cH) = groupChannels[i];
            gi.Channels[i].Shrink(cW, cH);
        }

        // Decompress group channels
        var status = ModularDecoder.Decompress(
            br, gi, null, groupId,
            undoTransforms: true,
            globalTree: _globalTree,
            code: _globalCode,
            ctxMap: _globalCtxMap);
        if (!status) return false;

        // Copy decoded data back to full image
        if (_useFullImage)
        {
            for (int i = 0; i < groupChannels.Count; i++)
            {
                var (srcIdx, cX, cY, cW, cH) = groupChannels[i];
                var srcCh = gi.Channels[i];
                var dstCh = _fullImage.Channels[srcIdx];

                for (int y = 0; y < cH; y++)
                {
                    var srcRow = srcCh.Row(y);
                    var dstRow = dstCh.Row(cY + y);
                    for (int x = 0; x < cW; x++)
                    {
                        dstRow[cX + x] = srcRow[x];
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Finalizes modular decoding: undoes global transforms and converts int pixels to float.
    /// Port of ModularFrameDecoder::FinalizeDecoding.
    /// </summary>
    public JxlStatus FinalizeDecoding(FrameHeader fh, float[][]? outputFloat = null)
    {
        if (_fullImage == null) return false;
        if (_nbChannels == 0) return true;

        // Undo all transforms
        var wpHeader = _globalHeader?.WpHeader ?? new WeightedHeader();
        _fullImage.UndoTransforms(wpHeader);

        // Also apply any group-level transforms
        foreach (var t in _globalTransforms)
        {
            t.Inverse(_fullImage, wpHeader);
        }

        return true;
    }

    /// <summary>
    /// Converts the finalized modular image channels to float output.
    /// Call after FinalizeDecoding.
    /// Port of ModularImageToDecodedRect.
    /// </summary>
    public JxlStatus CopyToFloat(FrameHeader fh, float[][][] output,
                                  int outputXOffset = 0, int outputYOffset = 0)
    {
        if (_fullImage == null) return false;

        int bitdepth = _fullImage.BitDepth;
        float scale = 1.0f / ((1 << bitdepth) - 1);
        bool isXyb = fh.Transform == ColorTransform.XYB;

        int numColor = _doColor ? Math.Min(3, _fullImage.Channels.Count) : 0;
        int numExtra = Math.Min(_nbExtraChannels,
                                _fullImage.Channels.Count - numColor);

        // Copy color channels (int → float, normalized to [0,1])
        for (int c = 0; c < numColor && c < output.Length; c++)
        {
            var ch = _fullImage.Channels[c];
            for (int y = 0; y < ch.H && (outputYOffset + y) < output[c].Length; y++)
            {
                var srcRow = ch.Row(y);
                var dstRow = output[c][outputYOffset + y];
                for (int x = 0; x < ch.W && (outputXOffset + x) < dstRow.Length; x++)
                {
                    dstRow[outputXOffset + x] = srcRow[x] * scale;
                }
            }
        }

        // Copy extra channels
        for (int ec = 0; ec < numExtra && (numColor + ec) < output.Length; ec++)
        {
            int srcIdx = numColor + ec;
            int dstIdx = numColor + ec;
            if (srcIdx >= _fullImage.Channels.Count) break;

            var ch = _fullImage.Channels[srcIdx];
            for (int y = 0; y < ch.H && (outputYOffset + y) < output[dstIdx].Length; y++)
            {
                var srcRow = ch.Row(y);
                var dstRow = output[dstIdx][outputYOffset + y];
                for (int x = 0; x < ch.W && (outputXOffset + x) < dstRow.Length; x++)
                {
                    dstRow[outputXOffset + x] = srcRow[x] * scale;
                }
            }
        }

        return true;
    }

    /// <summary>Counts leaf nodes in the tree (number of ANS contexts).</summary>
    private static int CountLeaves(List<PropertyDecisionNode> tree)
    {
        int count = 0;
        foreach (var node in tree)
            if (node.IsLeaf) count++;
        return Math.Max(count, 1);
    }
}
