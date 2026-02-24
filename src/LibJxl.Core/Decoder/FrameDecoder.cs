// Port of lib/jxl/dec_frame.h/cc — Frame decoder orchestrator
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Fields;

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

    /// <summary>Runs final operations once all frame data is decoded.</summary>
    public JxlStatus FinalizeFrame()
    {
        _isFinalized = true;
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

    private JxlStatus ProcessDCGlobal(BitReader br)
    {
        // In full implementation: decode patches, splines, noise, quantizer, block ctx map, modular global
        // For now: mark as done (stub for modular-only decoding)
        _decodedDcGlobal = true;
        return true;
    }

    private JxlStatus ProcessDCGroup(int dcGroupId, BitReader br)
    {
        // In full implementation: decode VarDCT DC, modular DC, AC metadata
        _decodedDcGroups[dcGroupId] = true;
        return true;
    }

    private JxlStatus FinalizeDC()
    {
        // In full implementation: adaptive DC smoothing
        _finalizedDc = true;
        return true;
    }

    private JxlStatus ProcessACGlobal(BitReader br)
    {
        // In full implementation: decode quant matrices, coefficient orders, ANS histograms
        _decodedAcGlobal = true;
        return true;
    }

    private JxlStatus ProcessACGroup(int acGroupId, BitReader[] passReaders, int numPasses)
    {
        // In full implementation: decode AC coefficients, dequantize, IDCT, render pipeline
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
}
