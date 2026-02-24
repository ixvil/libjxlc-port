// Port of lib/jxl/decode.cc — top-level JXL decoder state machine
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Fields;

namespace LibJxl.Decoder;

/// <summary>Stage of the codestream decoder.</summary>
public enum DecoderStage
{
    Inited,
    Started,
    CodestreamFinished,
    Error,
}

/// <summary>Stage within a frame.</summary>
public enum FrameStage
{
    Header,
    TOC,
    Full,
}

/// <summary>
/// High-level JXL decoder. Parses codestream signature, headers, and frames.
/// Port of JxlDecoder from lib/jxl/decode.cc.
/// </summary>
public class JxlDecoder
{
    // JXL codestream magic: 0xFF0A
    private const byte JxlSignature1 = 0xFF;
    private const byte JxlSignature2 = 0x0A;

    private DecoderStage _stage = DecoderStage.Inited;

    // Parsed metadata
    public SizeHeader Size { get; private set; } = new();
    public ImageMetadata Metadata { get; private set; } = new();
    public FrameHeader? CurrentFrameHeader { get; private set; }

    // State
    private bool _parsedSignature;
    private bool _parsedBasicInfo;
    private FrameDecoder? _frameDecoder;

    /// <summary>Current decoder stage.</summary>
    public DecoderStage Stage => _stage;

    /// <summary>Width of the image (after reading basic info).</summary>
    public int Width => Size.XSize;

    /// <summary>Height of the image (after reading basic info).</summary>
    public int Height => Size.YSize;

    /// <summary>
    /// Processes the codestream from the given data.
    /// Returns true if basic info was successfully read.
    /// </summary>
    public JxlStatus ReadBasicInfo(byte[] data)
    {
        if (data.Length < 2) return false;

        using var reader = new BitReader(data);

        // Check signature
        if (!_parsedSignature)
        {
            byte b1 = (byte)reader.ReadFixedBits(8);
            byte b2 = (byte)reader.ReadFixedBits(8);

            if (b1 != JxlSignature1 || b2 != JxlSignature2)
                return false;

            _parsedSignature = true;
        }

        // Read SizeHeader
        var sizeStatus = Size.ReadFromBitStream(reader);
        if (!sizeStatus) return false;

        // Read ImageMetadata
        var metaStatus = Metadata.ReadFromBitStream(reader);
        if (!metaStatus) return false;

        _parsedBasicInfo = true;
        _stage = DecoderStage.Started;

        reader.Close();
        return true;
    }

    /// <summary>
    /// Reads basic info and then decodes the first frame header + TOC.
    /// The caller can then use the FrameDecoder to process sections.
    /// </summary>
    public JxlStatus ReadFrame(byte[] data, out FrameDecoder? frameDecoder)
    {
        frameDecoder = null;

        using var reader = new BitReader(data);

        // Check signature
        if (!_parsedSignature)
        {
            byte b1 = (byte)reader.ReadFixedBits(8);
            byte b2 = (byte)reader.ReadFixedBits(8);

            if (b1 != JxlSignature1 || b2 != JxlSignature2)
                return false;

            _parsedSignature = true;
        }

        // Read SizeHeader if not done
        if (!_parsedBasicInfo)
        {
            var sizeStatus = Size.ReadFromBitStream(reader);
            if (!sizeStatus) return false;

            var metaStatus = Metadata.ReadFromBitStream(reader);
            if (!metaStatus) return false;

            _parsedBasicInfo = true;
        }

        // Read frame
        _frameDecoder = new FrameDecoder(Metadata, Size);
        var frameStatus = _frameDecoder.InitFrame(reader);
        if (!frameStatus) return false;

        var outputStatus = _frameDecoder.InitFrameOutput();
        if (!outputStatus) return false;

        CurrentFrameHeader = _frameDecoder.Header;
        frameDecoder = _frameDecoder;

        _stage = DecoderStage.Started;
        reader.Close();
        return true;
    }

    /// <summary>
    /// Simple all-in-one decode: reads signature, headers, frame header + TOC,
    /// and processes all sections from contiguous data.
    /// </summary>
    public JxlStatus DecodeFrame(byte[] data)
    {
        if (data.Length < 2) return false;

        using var headerReader = new BitReader(data);

        // Check signature
        byte b1 = (byte)headerReader.ReadFixedBits(8);
        byte b2 = (byte)headerReader.ReadFixedBits(8);
        if (b1 != JxlSignature1 || b2 != JxlSignature2)
            return false;

        // Read SizeHeader
        var sizeStatus = Size.ReadFromBitStream(headerReader);
        if (!sizeStatus) return false;

        // Read ImageMetadata
        var metaStatus = Metadata.ReadFromBitStream(headerReader);
        if (!metaStatus) return false;

        _parsedBasicInfo = true;

        // Read frame header + TOC
        _frameDecoder = new FrameDecoder(Metadata, Size);
        var frameStatus = _frameDecoder.InitFrame(headerReader);
        if (!frameStatus) return false;

        var outputStatus = _frameDecoder.InitFrameOutput();
        if (!outputStatus) return false;

        CurrentFrameHeader = _frameDecoder.Header;

        // Calculate header bytes consumed
        int headerBits = (int)headerReader.TotalBitsConsumed;
        int headerBytes = headerBits / JxlConstants.BitsPerByte;
        headerReader.Close();

        // Now create BitReaders for each section and process them
        var toc = _frameDecoder.Toc;
        var sections = new SectionInfo[toc.Length];
        var sectionStatus = new SectionStatus[toc.Length];
        var sectionReaders = new BitReader[toc.Length];

        int pos = headerBytes;
        for (int i = 0; i < toc.Length; i++)
        {
            if (pos + toc[i].Size > data.Length)
                return false;

            sectionReaders[i] = new BitReader(data, pos, toc[i].Size);
            sections[i] = new SectionInfo
            {
                Reader = sectionReaders[i],
                Id = toc[i].Id,
                Index = i,
            };
            pos += toc[i].Size;
        }

        var processStatus = _frameDecoder.ProcessSections(sections, sectionStatus);

        // Close section readers
        for (int i = 0; i < sectionReaders.Length; i++)
            sectionReaders[i].Close();

        if (!processStatus) return false;

        var finalizeStatus = _frameDecoder.FinalizeFrame();
        if (!finalizeStatus) return false;

        _stage = DecoderStage.CodestreamFinished;
        return true;
    }
}
