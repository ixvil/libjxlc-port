// Port of lib/jxl/passes_state.h + dec_cache.h — Shared decoding state
using LibJxl.Entropy;
using LibJxl.Fields;
using LibJxl.Image;

namespace LibJxl.Decoder;

/// <summary>
/// Shared state between encoder and decoder for a frame.
/// Port of jxl::PassesSharedState from passes_state.h.
/// Contains all decoded metadata needed during frame reconstruction.
/// </summary>
public class PassesSharedState
{
    /// <summary>Frame dimensions in blocks, groups, pixels.</summary>
    public FrameDimensions FrameDim = new();

    /// <summary>AC strategy per 8×8 block (which DCT to use).</summary>
    public AcStrategyType[,]? AcStrategy; // [by, bx]

    /// <summary>Dequantization weight matrices.</summary>
    public DequantMatrices Matrices = new();

    /// <summary>Quantizer parameters (global scale, DC quant).</summary>
    public Quantizer Quantizer;

    /// <summary>Raw quantization field per block.</summary>
    public int[,]? RawQuantField; // [by, bx]

    /// <summary>Edge-preserving filter sharpness per block.</summary>
    public byte[,]? EpfSharpness; // [by, bx]

    /// <summary>Color correlation map (chroma from luma).</summary>
    public ColorCorrelationMap Cmap = new();

    /// <summary>Image-level features (patches, splines, noise).</summary>
    public ImageFeatures ImageFeatures = new();

    /// <summary>Coefficient scan orders per pass.</summary>
    public int[][]? CoeffOrders; // [pass] → order array

    /// <summary>DC image (3 channels, float).</summary>
    public float[,,]? DcStorage; // [channel, y, x]

    /// <summary>Block context map for AC entropy coding.</summary>
    public BlockCtxMap BlockCtxMap = new();

    /// <summary>Number of histogram sets used for AC decoding.</summary>
    public int NumHistograms;

    public PassesSharedState()
    {
        Quantizer = new Quantizer(Matrices);
    }

    /// <summary>
    /// Allocates per-block fields for the given frame dimensions.
    /// </summary>
    public void AllocateBlockFields()
    {
        int bx = FrameDim.XSizeBlocks;
        int by = FrameDim.YSizeBlocks;

        AcStrategy = new AcStrategyType[by, bx];
        RawQuantField = new int[by, bx];
        EpfSharpness = new byte[by, bx];

        // DC storage: 3 channels
        int dcX = FrameDim.XSizeDcGroups > 0 ? FrameDim.XSizeBlocks : 1;
        int dcY = FrameDim.YSizeDcGroups > 0 ? FrameDim.YSizeBlocks : 1;
        DcStorage = new float[3, dcY, dcX];

        // Color correlation map
        Cmap.Create(FrameDim.XSize, FrameDim.YSize);
    }
}

/// <summary>
/// Per-frame decoder state.
/// Port of jxl::PassesDecoderState from dec_cache.h.
/// </summary>
public class PassesDecoderState
{
    /// <summary>Shared codec state (quantizer, matrices, etc.).</summary>
    public PassesSharedState Shared = new();

    /// <summary>ANS codes per pass.</summary>
    public ANSCode[]? Codes;

    /// <summary>Context mappings per pass.</summary>
    public byte[][]? ContextMaps;

    /// <summary>X channel distance multiplier.</summary>
    public float XDmMultiplier = 1.0f;

    /// <summary>B channel distance multiplier.</summary>
    public float BDmMultiplier = 1.0f;

    /// <summary>Whether to use 16-bit AC coefficients (vs 32-bit).</summary>
    public bool Use16BitAc;

    /// <summary>Used AC strategy types bitmask.</summary>
    public uint UsedAcs;

    /// <summary>
    /// Initializes the decoder state for a given frame header.
    /// </summary>
    public void Init(FrameHeader header)
    {
        Shared.FrameDim = header.ToFrameDimensions();

        int numPasses = (int)header.PassesInfo.NumPasses;
        Codes = new ANSCode[numPasses];
        ContextMaps = new byte[numPasses][];

        if (header.Encoding == FrameEncoding.VarDCT)
        {
            Shared.AllocateBlockFields();
        }
    }
}
