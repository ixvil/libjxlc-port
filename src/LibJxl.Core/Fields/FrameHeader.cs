// Port of FrameHeader from lib/jxl/frame_header.h/cc
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Decoder;
using LibJxl.Entropy;

namespace LibJxl.Fields;

/// <summary>YCbCr chroma subsampling modes. Port of jxl::YCbCrChromaSubsampling.</summary>
public class YCbCrChromaSubsampling
{
    public uint[] ChannelMode = new uint[3]; // Y, Cb, Cr

    private static readonly byte[] HShift = [0, 1, 1, 0];
    private static readonly byte[] VShift = [0, 1, 0, 1];

    public int MaxHShift { get; private set; }
    public int MaxVShift { get; private set; }

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        for (int i = 0; i < 3; i++)
            ChannelMode[i] = (uint)br.ReadBits(2);
        Recompute();
        return true;
    }

    /// <summary>Whether channel c is horizontally subsampled relative to max.</summary>
    public bool IsHSubsampled(int c) =>
        c < 3 && HShift[ChannelMode[c]] < MaxHShift;

    /// <summary>Whether channel c is vertically subsampled relative to max.</summary>
    public bool IsVSubsampled(int c) =>
        c < 3 && VShift[ChannelMode[c]] < MaxVShift;

    /// <summary>Whether any chroma subsampling is used.</summary>
    public bool Is444 => MaxHShift == 0 && MaxVShift == 0;

    private void Recompute()
    {
        MaxHShift = 0;
        MaxVShift = 0;
        for (int i = 0; i < 3; i++)
        {
            MaxHShift = Math.Max(MaxHShift, HShift[ChannelMode[i]]);
            MaxVShift = Math.Max(MaxVShift, VShift[ChannelMode[i]]);
        }
    }
}

/// <summary>Blending info for a frame. Port of jxl::BlendingInfo.</summary>
public class BlendingInfo
{
    public BlendMode Mode = BlendMode.Replace;
    public uint AlphaChannel;
    public bool Clamp;
    public uint Source;

    public JxlStatus ReadFromBitStream(BitReader br, int numExtraChannels)
    {
        Mode = (BlendMode)FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Val(1),
            U32Distr.Val(2), U32Distr.BitsOffset(2, 3));

        if (numExtraChannels > 0 &&
            (Mode == BlendMode.Blend || Mode == BlendMode.AlphaWeightedAdd || Mode == BlendMode.Mul))
        {
            AlphaChannel = FieldReader.ReadU32(br,
                U32Distr.Val(0), U32Distr.Val(1),
                U32Distr.Val(2), U32Distr.BitsOffset(3, 3));
        }

        if (Mode == BlendMode.Blend || Mode == BlendMode.AlphaWeightedAdd || Mode == BlendMode.Mul)
            Clamp = FieldReader.ReadBool(br);

        if (Mode != BlendMode.Replace)
            Source = FieldReader.ReadU32(br,
                U32Distr.Val(0), U32Distr.Val(1),
                U32Distr.Val(2), U32Distr.Val(3));

        return true;
    }
}

/// <summary>Multi-pass progressive parameters. Port of jxl::Passes.</summary>
public class Passes
{
    public const int MaxNumPasses = 11;

    public uint NumPasses = 1;
    public uint NumDownsample;
    public uint[] Downsample = new uint[MaxNumPasses];
    public uint[] LastPass = new uint[MaxNumPasses];
    public uint[] Shift = new uint[MaxNumPasses];

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        NumPasses = FieldReader.ReadU32(br,
            U32Distr.Val(1), U32Distr.Val(2),
            U32Distr.Val(3), U32Distr.BitsOffset(3, 4));

        if (NumPasses > MaxNumPasses) return false;

        if (NumPasses != 1)
        {
            NumDownsample = FieldReader.ReadU32(br,
                U32Distr.Val(0), U32Distr.Val(1),
                U32Distr.Val(2), U32Distr.BitsOffset(1, 3));

            for (int i = 0; i < (int)NumPasses - 1; i++)
                Shift[i] = (uint)br.ReadBits(2);
            Shift[NumPasses - 1] = 0;

            for (int i = 0; i < (int)NumDownsample; i++)
                Downsample[i] = FieldReader.ReadU32(br,
                    U32Distr.Val(1), U32Distr.Val(2),
                    U32Distr.Val(4), U32Distr.Val(8));

            for (int i = 0; i < (int)NumDownsample; i++)
                LastPass[i] = FieldReader.ReadU32(br,
                    U32Distr.Val(0), U32Distr.Val(1),
                    U32Distr.Val(2), U32Distr.Bits(3));
        }

        return true;
    }
}

/// <summary>
/// Frame header. Contains all parameters for decoding a frame.
/// Port of jxl::FrameHeader.
/// </summary>
public class FrameHeader
{
    // Flag constants
    public const ulong FlagNoise = 1;
    public const ulong FlagPatches = 2;
    public const ulong FlagSplines = 16;
    public const ulong FlagUseDcFrame = 32;
    public const ulong FlagSkipAdaptiveDCSmoothing = 128;

    public FrameType Type = FrameType.RegularFrame;
    public FrameEncoding Encoding = FrameEncoding.VarDCT;
    public ulong Flags;
    public ColorTransform Transform = ColorTransform.XYB;
    public YCbCrChromaSubsampling ChromaSubsampling = new();
    public uint Upsampling = 1;
    public uint[] ExtraChannelUpsampling = [];
    public uint GroupSizeShift = 1;
    public uint XQmScale = 3;
    public uint BQmScale = 2;
    public Passes PassesInfo = new();
    public uint DcLevel;
    public bool CustomSizeOrOrigin;
    public int FrameOriginX0;
    public int FrameOriginY0;
    public uint FrameSizeX;
    public uint FrameSizeY;
    public BlendingInfo Blending = new();
    public BlendingInfo[] ExtraChannelBlending = [];
    public uint AnimationDuration;
    public uint Timecode;
    public bool IsLast = true;
    public uint SaveAsReference;
    public bool SaveBeforeColorTransform;
    public string Name = "";
    public LoopFilter Filter = new();
    public ulong Extensions;

    // Set by caller before reading
    public ImageMetadata? NonserializedMetadata;
    public SizeHeader? NonserializedSizeHeader;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        // AllDefault check
        bool allDefault = FieldReader.ReadBool(br);
        if (allDefault)
        {
            // Set all to defaults
            Type = FrameType.RegularFrame;
            Encoding = FrameEncoding.VarDCT;
            Flags = 0;
            Transform = (NonserializedMetadata?.XybEncoded ?? true)
                ? ColorTransform.XYB
                : ColorTransform.None;
            IsLast = true;
            return true;
        }

        // Frame type
        Type = (FrameType)FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Val(1),
            U32Distr.Val(2), U32Distr.Val(3));

        // Encoding
        bool isModular = FieldReader.ReadBool(br);
        Encoding = isModular ? FrameEncoding.Modular : FrameEncoding.VarDCT;

        // Flags
        Flags = U64Coder.Read(br);

        // Color transform
        bool xybEncoded = NonserializedMetadata?.XybEncoded ?? true;
        if (xybEncoded)
        {
            Transform = ColorTransform.XYB;
        }
        else
        {
            bool alternate = FieldReader.ReadBool(br);
            Transform = alternate ? ColorTransform.YCbCr : ColorTransform.None;
        }

        // Chroma subsampling (conditional)
        if (Transform == ColorTransform.YCbCr && (Flags & FlagUseDcFrame) == 0)
        {
            var csStatus = ChromaSubsampling.ReadFromBitStream(br);
            if (!csStatus) return false;
        }

        // Upsampling (if not DC frame)
        if ((Flags & FlagUseDcFrame) == 0)
        {
            Upsampling = FieldReader.ReadU32(br,
                U32Distr.Val(1), U32Distr.Val(2),
                U32Distr.Val(4), U32Distr.Val(8));

            int numExtra = (int)(NonserializedMetadata?.NumExtraChannels ?? 0);
            ExtraChannelUpsampling = new uint[numExtra];
            for (int i = 0; i < numExtra; i++)
            {
                ExtraChannelUpsampling[i] = FieldReader.ReadU32(br,
                    U32Distr.Val(1), U32Distr.Val(2),
                    U32Distr.Val(4), U32Distr.Val(8));
            }
        }

        // Modular group size shift
        if (Encoding == FrameEncoding.Modular)
            GroupSizeShift = (uint)br.ReadBits(2);

        // VarDCT quantization scales (conditional)
        if (Encoding == FrameEncoding.VarDCT && Transform == ColorTransform.XYB)
        {
            XQmScale = (uint)br.ReadBits(3);
            BQmScale = (uint)br.ReadBits(3);
        }

        // Passes (if not reference-only)
        if (Type != FrameType.ReferenceOnly)
        {
            var passStatus = PassesInfo.ReadFromBitStream(br);
            if (!passStatus) return false;
        }

        // DC level
        if (Type == FrameType.DCFrame)
        {
            DcLevel = FieldReader.ReadU32(br,
                U32Distr.Val(1), U32Distr.Val(2),
                U32Distr.Val(3), U32Distr.Val(4));
        }

        // Custom size or origin
        if (Type != FrameType.DCFrame)
        {
            CustomSizeOrOrigin = FieldReader.ReadBool(br);
            if (CustomSizeOrOrigin)
            {
                // Frame origin and size using signed packing
                uint ux0 = FieldReader.ReadU32(br,
                    U32Distr.Bits(8), U32Distr.BitsOffset(11, 256),
                    U32Distr.BitsOffset(14, 2304), U32Distr.BitsOffset(30, 18688));
                uint uy0 = FieldReader.ReadU32(br,
                    U32Distr.Bits(8), U32Distr.BitsOffset(11, 256),
                    U32Distr.BitsOffset(14, 2304), U32Distr.BitsOffset(30, 18688));
                FrameOriginX0 = SignedPack.UnpackSigned(ux0);
                FrameOriginY0 = SignedPack.UnpackSigned(uy0);

                FrameSizeX = FieldReader.ReadU32(br,
                    U32Distr.Bits(8), U32Distr.BitsOffset(11, 256),
                    U32Distr.BitsOffset(14, 2304), U32Distr.BitsOffset(30, 18688));
                FrameSizeY = FieldReader.ReadU32(br,
                    U32Distr.Bits(8), U32Distr.BitsOffset(11, 256),
                    U32Distr.BitsOffset(14, 2304), U32Distr.BitsOffset(30, 18688));
            }
        }

        // Blending info (for regular frames)
        if (Type == FrameType.RegularFrame || Type == FrameType.SkipProgressive)
        {
            int numExtra = (int)(NonserializedMetadata?.NumExtraChannels ?? 0);
            var blendStatus = Blending.ReadFromBitStream(br, numExtra);
            if (!blendStatus) return false;

            ExtraChannelBlending = new BlendingInfo[numExtra];
            for (int i = 0; i < numExtra; i++)
            {
                ExtraChannelBlending[i] = new BlendingInfo();
                var ecStatus = ExtraChannelBlending[i].ReadFromBitStream(br, numExtra);
                if (!ecStatus) return false;
            }

            // Animation frame
            if (NonserializedMetadata?.HaveAnimation == true)
            {
                AnimationDuration = FieldReader.ReadU32(br,
                    U32Distr.Val(0), U32Distr.Val(1),
                    U32Distr.Bits(8), U32Distr.Bits(32));

                if (NonserializedMetadata?.Animation.HaveTimecodes == true)
                    Timecode = (uint)br.ReadBits(32);
            }

            IsLast = FieldReader.ReadBool(br);
        }
        else
        {
            IsLast = Type == FrameType.RegularFrame;
        }

        // Save as reference
        if (Type != FrameType.DCFrame && !IsLast)
        {
            SaveAsReference = FieldReader.ReadU32(br,
                U32Distr.Val(0), U32Distr.Val(1),
                U32Distr.Val(2), U32Distr.Val(3));
        }

        // Save before color transform
        if (Type == FrameType.ReferenceOnly || Type == FrameType.RegularFrame)
        {
            if ((Encoding == FrameEncoding.VarDCT && !xybEncoded) ||
                Type == FrameType.ReferenceOnly)
            {
                SaveBeforeColorTransform = FieldReader.ReadBool(br);
            }
        }

        // Name string
        uint nameLength = FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Bits(4),
            U32Distr.BitsOffset(5, 16), U32Distr.BitsOffset(10, 48));
        if (nameLength > 0)
        {
            var nameChars = new char[nameLength];
            for (int i = 0; i < (int)nameLength; i++)
                nameChars[i] = (char)br.ReadBits(8);
            Name = new string(nameChars);
        }

        // Loop filter
        Filter.NonserializedIsModular = Encoding == FrameEncoding.Modular;
        var lfStatus = Filter.ReadFromBitStream(br);
        if (!lfStatus) return false;

        // Extensions
        Extensions = U64Coder.Read(br);

        return true;
    }

    // Computed properties
    public int GroupDimPixels => (FrameConstants.GroupDim >> 1) << (int)GroupSizeShift;

    /// <summary>
    /// Whether this frame can be stored as a reference frame.
    /// Port of FrameHeader::CanBeReferenced.
    /// </summary>
    public bool CanBeReferenced()
    {
        return Type != FrameType.SkipProgressive &&
               (SaveAsReference != 0 ||
                Type == FrameType.RegularFrame ||
                Type == FrameType.ReferenceOnly);
    }

    /// <summary>
    /// Computes the full FrameDimensions from this header and the image metadata.
    /// </summary>
    public FrameDimensions ToFrameDimensions()
    {
        var fd = new FrameDimensions();
        int xsize = NonserializedSizeHeader?.XSize ?? 0;
        int ysize = NonserializedSizeHeader?.YSize ?? 0;

        if (CustomSizeOrOrigin && FrameSizeX > 0 && FrameSizeY > 0)
        {
            xsize = (int)FrameSizeX;
            ysize = (int)FrameSizeY;
        }

        int maxHShift = ChromaSubsampling.MaxHShift;
        int maxVShift = ChromaSubsampling.MaxVShift;
        bool modular = Encoding == FrameEncoding.Modular;
        int ups = (int)Upsampling;

        fd.Set(xsize, ysize, (int)GroupSizeShift, maxHShift, maxVShift, modular, ups);
        return fd;
    }
}
