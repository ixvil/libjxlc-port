// Port of lib/jxl/dec_frame.h/cc — Frame decoder orchestrator
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.ColorManagement;
using LibJxl.Entropy;
using LibJxl.Fields;
using LibJxl.RenderPipeline;

namespace LibJxl.Decoder;

/// <summary>Status of a processed section.</summary>
public enum SectionStatus
{
    Done = 0,
    Skipped = 1,
    Duplicate = 2,
    Partial = 3,
}

/// <summary>Info for a section to be processed.</summary>
public struct SectionInfo
{
    public BitReader Reader;
    public int Id;     // Logical section ID
    public int Index;  // Physical index in the bytestream
}

/// <summary>
/// Stored reference frame data.
/// Port of jxl::FrameDecoder reference_frames.
/// </summary>
public class ReferenceFrame
{
    /// <summary>Pixel data for this reference frame (float per channel per row).</summary>
    public float[][][]? ChannelData; // [channel][row][col]

    /// <summary>Width of the reference frame.</summary>
    public int Width;

    /// <summary>Height of the reference frame.</summary>
    public int Height;

    /// <summary>Whether saved before color transform (for proper reconstruction).</summary>
    public bool SavedBeforeColorTransform;

    /// <summary>The frame header of the source frame.</summary>
    public FrameHeader? SourceHeader;
}

/// <summary>
/// Decodes a single frame from a JXL bitstream.
/// Port of jxl::FrameDecoder. Manages TOC, section dispatch, and DC/AC decoding phases.
/// </summary>
public class FrameDecoder
{
    private readonly FrameHeader _frameHeader;
    private FrameDimensions _frameDim = new();
    private TocEntry[] _toc = [];
    private long _sectionSizesSum;

    // Decoding state tracking
    private bool[] _processedSection = [];
    private byte[] _decodedPassesPerAcGroup = [];
    private bool[] _decodedDcGroups = [];
    private bool _decodedDcGlobal;
    private bool _decodedAcGlobal;
    private bool _finalizedDc = true;
    private bool _isFinalized = true;
    private int _numSectionsDone;

    // Frame decoding state
    private PassesDecoderState _decState = new();

    // Render pipeline
    private SimpleRenderPipeline? _pipeline;
    private byte[]? _outputPixels;

    // Reference frames (up to 4, indexed by save_as_reference)
    private readonly ReferenceFrame?[] _referenceFrames = new ReferenceFrame?[4];

    public FrameDecoder(ImageMetadata metadata, SizeHeader sizeHeader)
    {
        _frameHeader = new FrameHeader
        {
            NonserializedMetadata = metadata,
            NonserializedSizeHeader = sizeHeader,
        };
    }

    /// <summary>The frame header parsed by InitFrame.</summary>
    public FrameHeader Header => _frameHeader;

    /// <summary>The computed frame dimensions.</summary>
    public FrameDimensions FrameDim => _frameDim;

    /// <summary>The Table of Contents entries.</summary>
    public ReadOnlySpan<TocEntry> Toc => _toc;

    /// <summary>Sum of all section sizes in bytes.</summary>
    public long SumSectionSizes => _sectionSizesSum;

    /// <summary>Whether all DC groups have been decoded.</summary>
    public bool HasDecodedDC => _finalizedDc;

    /// <summary>Whether all sections have been processed.</summary>
    public bool HasDecodedAll => _numSectionsDone == _toc.Length;

    /// <summary>The decoder state containing shared/per-frame data.</summary>
    public PassesDecoderState DecState => _decState;

    /// <summary>The render pipeline (available after DC finalization).</summary>
    public SimpleRenderPipeline? Pipeline => _pipeline;

    /// <summary>
    /// Output pixel buffer (available after FinalizeFrame).
    /// Layout: RGB or RGBA, row-major, 8-bit per channel.
    /// </summary>
    public byte[]? OutputPixels => _outputPixels;

    /// <summary>Reference frames storage (up to 4 frames).</summary>
    public ReadOnlySpan<ReferenceFrame?> ReferenceFrames => _referenceFrames;

    /// <summary>
    /// Reads the frame header and TOC from the bitstream.
    /// After this call, the caller can read individual sections.
    /// </summary>
    public JxlStatus InitFrame(BitReader br)
    {
        // Read frame header
        var status = _frameHeader.ReadFromBitStream(br);
        if (!status) return false;

        _frameDim = _frameHeader.ToFrameDimensions();

        int numPasses = (int)_frameHeader.PassesInfo.NumPasses;
        int numGroups = _frameDim.NumGroups;
        int numDcGroups = _frameDim.NumDcGroups;

        // Read TOC
        int tocEntries = TocReader.NumTocEntries(numGroups, numDcGroups, numPasses);
        var tocStatus = TocReader.ReadToc(tocEntries, br, out uint[] sizes, out int[]? permutation);
        if (!tocStatus) return false;

        bool havePermutation = permutation != null;
        _toc = new TocEntry[tocEntries];
        _sectionSizesSum = 0;

        for (int i = 0; i < tocEntries; i++)
        {
            _toc[i].Size = (int)sizes[i];
            int index = havePermutation ? permutation![i] : i;
            _toc[index].Id = i;

            _sectionSizesSum += _toc[i].Size;
        }

        // Verify we're on byte boundary after TOC
        br.JumpToByteBoundary();

        return true;
    }

    /// <summary>
    /// Initializes frame output state — must be called after InitFrame.
    /// </summary>
    public JxlStatus InitFrameOutput()
    {
        _decodedDcGlobal = false;
        _decodedAcGlobal = false;
        _isFinalized = false;
        _finalizedDc = false;
        _numSectionsDone = 0;

        _decodedDcGroups = new bool[_frameDim.NumDcGroups];
        _decodedPassesPerAcGroup = new byte[_frameDim.NumGroups];
        _processedSection = new bool[_toc.Length];

        // Initialize decoder state
        _decState = new PassesDecoderState();
        _decState.Init(_frameHeader);

        return true;
    }

    /// <summary>
    /// Processes an array of sections. Each section has a BitReader positioned at the section data.
    /// Returns status for each section.
    /// </summary>
    public JxlStatus ProcessSections(SectionInfo[] sections, SectionStatus[] sectionStatus)
    {
        int num = sections.Length;
        if (num == 0) return true;

        Array.Fill(sectionStatus, SectionStatus.Skipped);

        int numPasses = (int)_frameHeader.PassesInfo.NumPasses;
        bool singleSection = _frameDim.NumGroups == 1 && numPasses == 1;

        // Classify sections by type
        int dcGlobalSec = num; // sentinel = not found
        int acGlobalSec = num;
        int[] dcGroupSec = new int[_frameDim.NumDcGroups];
        Array.Fill(dcGroupSec, num);

        // For AC groups: [group][pass] = section index
        int[,] acGroupSec = new int[_frameDim.NumGroups, numPasses];
        for (int g = 0; g < _frameDim.NumGroups; g++)
            for (int p = 0; p < numPasses; p++)
                acGroupSec[g, p] = num;

        int[] desiredNumAcPasses = new int[_frameDim.NumGroups];

        if (singleSection)
        {
            if (num != 1 || sections[0].Id != 0) return false;
            if (!_processedSection[0])
            {
                _processedSection[0] = true;
                dcGlobalSec = 0;
                acGlobalSec = 0;
                dcGroupSec[0] = 0;
                acGroupSec[0, 0] = 0;
                desiredNumAcPasses[0] = 1;
            }
            else
            {
                sectionStatus[0] = SectionStatus.Duplicate;
            }
        }
        else
        {
            int acGlobalIndex = _frameDim.NumDcGroups + 1;
            for (int i = 0; i < num; i++)
            {
                if (_processedSection[sections[i].Id])
                {
                    sectionStatus[i] = SectionStatus.Duplicate;
                    continue;
                }

                int id = sections[i].Id;
                if (id == 0)
                {
                    dcGlobalSec = i;
                }
                else if (id < acGlobalIndex)
                {
                    dcGroupSec[id - 1] = i;
                }
                else if (id == acGlobalIndex)
                {
                    acGlobalSec = i;
                }
                else
                {
                    int acIdx = id - acGlobalIndex - 1;
                    int acg = acIdx % _frameDim.NumGroups;
                    int acp = acIdx / _frameDim.NumGroups;
                    if (acp >= numPasses) return false;
                    acGroupSec[acg, acp] = i;
                }
                _processedSection[sections[i].Id] = true;
            }

            // Count desired passes per group
            for (int g = 0; g < _frameDim.NumGroups; g++)
            {
                int j = 0;
                while (j + _decodedPassesPerAcGroup[g] < numPasses &&
                       acGroupSec[g, j + _decodedPassesPerAcGroup[g]] != num)
                {
                    j++;
                }
                desiredNumAcPasses[g] = j;
            }
        }

        // Phase 1: DC Global
        if (dcGlobalSec != num)
        {
            var dcStatus = ProcessDCGlobal(sections[dcGlobalSec].Reader);
            if (!dcStatus) return false;
            sectionStatus[dcGlobalSec] = SectionStatus.Done;
        }

        // Phase 2: DC Groups
        if (_decodedDcGlobal)
        {
            for (int i = 0; i < _frameDim.NumDcGroups; i++)
            {
                if (dcGroupSec[i] != num)
                {
                    var s = ProcessDCGroup(i, sections[dcGroupSec[i]].Reader);
                    if (!s) return false;
                    sectionStatus[dcGroupSec[i]] = SectionStatus.Done;
                }
            }
        }

        // Phase 3: Finalize DC
        if (!HasDcGroupToDecode() && !_finalizedDc)
        {
            var fdcStatus = FinalizeDC();
            if (!fdcStatus) return false;
        }

        // Phase 4: AC Global
        if (_finalizedDc && acGlobalSec != num && !_decodedAcGlobal)
        {
            var acgStatus = ProcessACGlobal(sections[acGlobalSec].Reader);
            if (!acgStatus) return false;
            sectionStatus[acGlobalSec] = SectionStatus.Done;
        }

        // Phase 5: AC Groups
        if (_decodedAcGlobal)
        {
            for (int g = 0; g < _frameDim.NumGroups; g++)
            {
                if (desiredNumAcPasses[g] == 0) continue;

                BitReader[] passReaders = new BitReader[desiredNumAcPasses[g]];
                for (int p = 0; p < desiredNumAcPasses[g]; p++)
                {
                    int secIdx = acGroupSec[g, _decodedPassesPerAcGroup[g] + p];
                    if (secIdx == num) break;
                    passReaders[p] = sections[secIdx].Reader;
                    sectionStatus[secIdx] = SectionStatus.Done;
                }

                var acStatus = ProcessACGroup(g, passReaders, desiredNumAcPasses[g]);
                if (!acStatus) return false;
            }
        }

        // Count done sections
        _numSectionsDone = 0;
        for (int i = 0; i < num; i++)
        {
            if (sectionStatus[i] == SectionStatus.Done)
                _numSectionsDone++;
        }

        return true;
    }

    /// <summary>
    /// Runs final operations once all frame data is decoded.
    /// Executes the render pipeline to produce output pixels.
    /// Port of FrameDecoder::FinalizeFrame.
    ///
    /// Steps:
    /// 1. Finalize modular decoding (undo global transforms).
    /// 2. Execute the render pipeline (all stages: filter → upsample → color → write).
    /// 3. Store reference frame if CanBeReferenced().
    /// </summary>
    public JxlStatus FinalizeFrame()
    {
        if (_isFinalized) return false; // Already finalized

        _isFinalized = true;

        // Step 1: Finalize modular decoding
        // In C++, this calls modular_frame_decoder_.FinalizeDecoding()
        // which undoes global modular transforms and copies buffers.
        // For now, this is a placeholder — modular transform undo
        // will be implemented when the modular decoder is fully ported.
        // TODO: modularFrameDecoder.FinalizeDecoding()

        // Step 2: Execute the render pipeline
        // SimpleRenderPipeline processes the full image in one call.
        // A proper grouped pipeline would iterate per group, but
        // the simple pipeline processes all stages across the entire buffer.
        if (_pipeline != null)
        {
            if (!_pipeline.ProcessGroup(0, 0))
                return false;
        }

        // Step 3: Store reference frame if needed
        if (_frameHeader.CanBeReferenced() && _pipeline?.ChannelData != null)
        {
            int slot = (int)_frameHeader.SaveAsReference;
            if (slot < _referenceFrames.Length)
            {
                var refFrame = new ReferenceFrame
                {
                    Width = _frameDim.XSize,
                    Height = _frameDim.YSize,
                    SavedBeforeColorTransform = _frameHeader.SaveBeforeColorTransform,
                    SourceHeader = _frameHeader,
                };

                // Deep-copy the channel data so future frames can reference it
                var src = _pipeline.ChannelData;
                refFrame.ChannelData = new float[src.Length][][];
                for (int c = 0; c < src.Length; c++)
                {
                    refFrame.ChannelData[c] = new float[src[c].Length][];
                    for (int y = 0; y < src[c].Length; y++)
                    {
                        refFrame.ChannelData[c][y] = new float[src[c][y].Length];
                        Array.Copy(src[c][y], refFrame.ChannelData[c][y], src[c][y].Length);
                    }
                }

                _referenceFrames[slot] = refFrame;
            }
        }

        return true;
    }

    /// <summary>Returns a bit mask of reference frames this frame depends on.</summary>
    public int References()
    {
        int refs = 0;
        if (_frameHeader.Blending.Mode != BlendMode.Replace)
            refs |= 1 << (int)_frameHeader.Blending.Source;
        if ((_frameHeader.Flags & FrameHeader.FlagUseDcFrame) != 0)
            refs |= 16; // DC frame reference
        return refs;
    }

    // === Private section processors ===

    /// <summary>
    /// Phase 1: DC Global — patches, splines, noise, quantizer, color correlation, modular global.
    /// Port of FrameDecoder::ProcessDCGlobal.
    /// </summary>
    private JxlStatus ProcessDCGlobal(BitReader br)
    {
        var shared = _decState.Shared;
        var features = shared.ImageFeatures;

        if (_frameHeader.Encoding == FrameEncoding.VarDCT)
        {
            // Decode image features
            if ((_frameHeader.Flags & FrameHeader.FlagPatches) != 0)
            {
                if (!features.Patches.Decode(br, _frameDim.XSize, _frameDim.YSize, 0))
                    return false;
            }

            if ((_frameHeader.Flags & FrameHeader.FlagSplines) != 0)
            {
                if (!features.Splines.Decode(br, (long)_frameDim.XSize * _frameDim.YSize))
                    return false;
            }

            if ((_frameHeader.Flags & FrameHeader.FlagNoise) != 0)
            {
                if (!features.Noise.Decode(br))
                    return false;
            }

            // Decode DC dequantization (DC quant matrices)
            if (!shared.Matrices.DecodeDC(br))
                return false;

            // Decode quantizer (global_scale + quant_dc)
            if (!shared.Quantizer.ReadFromBitStream(br))
                return false;

            // Decode color correlation map (chroma from luma DC parameters)
            if (!shared.Cmap.DecodeDC(br))
                return false;
        }

        // TODO: Decode modular global info (tree, ANS codes, global channels)

        _decodedDcGlobal = true;
        return true;
    }

    /// <summary>
    /// Phase 2: DC Group — VarDCT DC coefficients, AC metadata per group.
    /// Port of FrameDecoder::ProcessDCGroup.
    /// </summary>
    private JxlStatus ProcessDCGroup(int dcGroupId, BitReader br)
    {
        var shared = _decState.Shared;

        if (_frameHeader.Encoding == FrameEncoding.VarDCT)
        {
            // TODO: Decode VarDCT DC coefficients for this group via modular decoder
            // - Extra precision bits
            // - Create 3-channel image for YCbCr DC
            // - Dequantize DC

            // TODO: Decode AC metadata (strategy, quant field, EPF sharpness)
            // via modular streams
        }

        // TODO: Decode modular group data for DC group rectangle

        _decodedDcGroups[dcGroupId] = true;
        return true;
    }

    /// <summary>
    /// Phase 3: Finalize DC — build pipeline, adaptive DC smoothing.
    /// Port of FrameDecoder::FinalizeDC + PreparePipeline.
    /// </summary>
    private JxlStatus FinalizeDC()
    {
        int width = _frameDim.XSize;
        int height = _frameDim.YSize;

        // Detect alpha channel from metadata
        bool hasAlpha = false;
        if (_frameHeader.NonserializedMetadata != null)
        {
            foreach (var ec in _frameHeader.NonserializedMetadata.ExtraChannelInfos)
            {
                if (ec.Type == ExtraChannelType.Alpha)
                {
                    hasAlpha = true;
                    break;
                }
            }
        }

        int numChannels = hasAlpha ? 4 : 3;

        // Build the render pipeline
        if (_frameHeader.Encoding == FrameEncoding.VarDCT)
        {
            // Build pipeline using PipelineBuilder (adds Gaborish, EPF, Upsampling, XYB/YCbCr, FromLinear)
            _pipeline = PipelineBuilder.BuildVarDctPipeline(
                _frameHeader, _frameDim, numChannels, width, height);

            // Allocate output pixel buffer
            int stride = hasAlpha ? 4 : 3;
            _outputPixels = new byte[width * height * stride];

            // Add the final write stage
            _pipeline.AddStage(new StageWrite(_outputPixels, width, numChannels, hasAlpha));

            // TODO: AdaptiveDCSmoothing()
            // Gaussian-weighted smoothing across 3×3 DC blocks at boundaries
        }
        else
        {
            // Modular mode: simpler pipeline (just output writing)
            _pipeline = new SimpleRenderPipeline();
            _pipeline.AllocateBuffers(width, height, numChannels);

            int stride = hasAlpha ? 4 : 3;
            _outputPixels = new byte[width * height * stride];
            _pipeline.AddStage(new StageWrite(_outputPixels, width, numChannels, hasAlpha));
        }

        _finalizedDc = true;
        return true;
    }

    /// <summary>
    /// Phase 4: AC Global — dequant matrices, coefficient orders, ANS histograms.
    /// Port of FrameDecoder::ProcessACGlobal.
    /// </summary>
    private JxlStatus ProcessACGlobal(BitReader br)
    {
        var shared = _decState.Shared;

        if (_frameHeader.Encoding == FrameEncoding.VarDCT)
        {
            // Decode full dequantization matrices
            if (!shared.Matrices.Decode(br))
                return false;

            // Number of histogram sets
            int numHistoBits = CeilLog2(shared.FrameDim.NumGroups);
            shared.NumHistograms = 1;
            if (numHistoBits > 0)
                shared.NumHistograms = 1 + (int)br.ReadBits(numHistoBits);

            int numPasses = (int)_frameHeader.PassesInfo.NumPasses;
            shared.CoeffOrders = new int[numPasses][];

            // Decode coefficient orders + histograms per pass
            for (int pass = 0; pass < numPasses; pass++)
            {
                // Determine which orders are used
                ushort usedOrders = (ushort)br.ReadBits(AcStrategy.kNumOrders);

                // Decode coefficient orders
                shared.CoeffOrders[pass] = CoeffOrder.DecodeCoeffOrders(usedOrders, br);

                // Decode ANS histograms for this pass
                int numContexts = shared.BlockCtxMap.NumACContexts() * shared.NumHistograms;
                _decState.Codes![pass] = new ANSCode();
                var histStatus = HistogramDecoder.DecodeHistograms(
                    br, numContexts, _decState.Codes[pass], out _decState.ContextMaps![pass]);
                if (!histStatus) return false;
            }

            // Compute dequant tables for used AC strategies
            if (!shared.Matrices.EnsureComputed(_decState.UsedAcs))
                return false;
        }

        _decodedAcGlobal = true;
        return true;
    }

    /// <summary>
    /// Phase 5: AC Group — decode AC coefficients, dequantize, IDCT, render pipeline.
    /// Port of FrameDecoder::ProcessACGroup.
    /// </summary>
    private JxlStatus ProcessACGroup(int acGroupId, BitReader[] passReaders, int numPasses)
    {
        if (_frameHeader.Encoding == FrameEncoding.VarDCT)
        {
            for (int p = 0; p < numPasses; p++)
            {
                int passIdx = _decodedPassesPerAcGroup[acGroupId] + p;
                if (passReaders[p] == null) continue;

                if (!DecGroup.DecodeGroupPass(_frameHeader, _decState, acGroupId, passIdx, passReaders[p]))
                    return false;
            }
        }
        else
        {
            // Modular AC: decode modular group for each pass
            // TODO: modular_frame_decoder.DecodeGroup()
        }

        _decodedPassesPerAcGroup[acGroupId] += (byte)numPasses;
        return true;
    }

    private bool HasDcGroupToDecode()
    {
        for (int i = 0; i < _decodedDcGroups.Length; i++)
        {
            if (!_decodedDcGroups[i]) return true;
        }
        return false;
    }

    private static int CeilLog2(int n)
    {
        if (n <= 1) return 0;
        return 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)(n - 1));
    }
}
