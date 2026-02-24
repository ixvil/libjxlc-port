// Port of ImageMetadata and nested types from lib/jxl/image_metadata.h/headers.h
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;

namespace LibJxl.Fields;

/// <summary>Bit depth information. Port of jxl::BitDepth.</summary>
public class BitDepthInfo
{
    public bool FloatingPointSample;
    public uint BitsPerSample = 8;
    public uint ExponentBitsPerSample;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        FloatingPointSample = FieldReader.ReadBool(br);

        if (!FloatingPointSample)
        {
            BitsPerSample = FieldReader.ReadU32(br,
                U32Distr.Val(8), U32Distr.Val(10), U32Distr.Val(12),
                U32Distr.BitsOffset(6, 1));
            ExponentBitsPerSample = 0;
        }
        else
        {
            BitsPerSample = FieldReader.ReadU32(br,
                U32Distr.Val(32), U32Distr.Val(16), U32Distr.Val(24),
                U32Distr.BitsOffset(6, 1));
            const uint offset = 1;
            uint exp = ExponentBitsPerSample >= offset ? ExponentBitsPerSample - offset : 0;
            exp = (uint)br.ReadBits(4);
            ExponentBitsPerSample = exp + offset;
        }

        return true;
    }
}

/// <summary>Tone mapping parameters. Port of jxl::ToneMapping.</summary>
public class ToneMapping
{
    public bool AllDefault;
    public float IntensityTarget = JxlConstants.DefaultIntensityTarget;
    public float MinNits;
    public bool RelativeToMaxDisplay;
    public float LinearBelow;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        AllDefault = FieldReader.ReadBool(br);
        if (AllDefault) return true;

        F16Coder.Read(br, out IntensityTarget);
        F16Coder.Read(br, out MinNits);
        RelativeToMaxDisplay = FieldReader.ReadBool(br);
        F16Coder.Read(br, out LinearBelow);
        return true;
    }
}

/// <summary>Extra channel information. Port of jxl::ExtraChannelInfo.</summary>
public class ExtraChannelInfo
{
    public bool AllDefault;
    public ExtraChannelType Type = ExtraChannelType.Alpha;
    public BitDepthInfo BitDepth = new();
    public uint DimShift;
    public string Name = "";
    public bool AlphaAssociated;
    public float[] SpotColor = new float[4];
    public uint CfaChannel;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        AllDefault = FieldReader.ReadBool(br);
        if (AllDefault)
        {
            Type = ExtraChannelType.Alpha;
            return true;
        }

        Type = EnumReader.ReadEnum(br, EnumValues.ExtraChannelTypes);

        var bdStatus = BitDepth.ReadFromBitStream(br);
        if (!bdStatus) return false;

        DimShift = FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Val(3), U32Distr.Val(4),
            U32Distr.BitsOffset(3, 1));

        // Name string
        uint nameLength = FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Bits(4),
            U32Distr.BitsOffset(5, 16), U32Distr.BitsOffset(10, 48));
        var nameChars = new char[nameLength];
        for (int i = 0; i < (int)nameLength; i++)
            nameChars[i] = (char)br.ReadBits(8);
        Name = new string(nameChars);

        // Conditional fields
        if (Type == ExtraChannelType.Alpha)
            AlphaAssociated = FieldReader.ReadBool(br);

        if (Type == ExtraChannelType.SpotColor)
        {
            for (int i = 0; i < 4; i++)
                F16Coder.Read(br, out SpotColor[i]);
        }

        if (Type == ExtraChannelType.CFA)
        {
            CfaChannel = FieldReader.ReadU32(br,
                U32Distr.Val(1), U32Distr.Bits(2),
                U32Distr.BitsOffset(4, 3), U32Distr.BitsOffset(8, 19));
        }

        return true;
    }
}

/// <summary>Animation header. Port of jxl::AnimationHeader.</summary>
public class AnimationHeader
{
    public uint TpsNumerator = 100;
    public uint TpsDenominator = 1;
    public uint NumLoops;
    public bool HaveTimecodes;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        TpsNumerator = FieldReader.ReadU32(br,
            U32Distr.Val(100), U32Distr.Val(1000),
            U32Distr.BitsOffset(10, 1), U32Distr.BitsOffset(30, 1));

        TpsDenominator = FieldReader.ReadU32(br,
            U32Distr.Val(1), U32Distr.Val(1001),
            U32Distr.BitsOffset(8, 1), U32Distr.BitsOffset(10, 1));

        NumLoops = FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Bits(3),
            U32Distr.Bits(16), U32Distr.Bits(32));

        HaveTimecodes = FieldReader.ReadBool(br);
        return true;
    }
}

/// <summary>Preview header. Port of jxl::PreviewHeader.</summary>
public class PreviewHeader
{
    private bool _div8;
    private uint _ysizeDiv8;
    private uint _ysize;
    private uint _ratio;
    private uint _xsizeDiv8;
    private uint _xsize;

    public int XSize => _div8 ? (int)(_xsizeDiv8 * 8) : (int)_xsize;
    public int YSize => _div8 ? (int)(_ysizeDiv8 * 8) : (int)_ysize;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        _div8 = FieldReader.ReadBool(br);

        if (_div8)
        {
            _ysizeDiv8 = FieldReader.ReadU32(br,
                U32Distr.Val(16), U32Distr.Val(32),
                U32Distr.BitsOffset(5, 1), U32Distr.BitsOffset(9, 33));
        }
        else
        {
            _ysize = FieldReader.ReadU32(br,
                U32Distr.BitsOffset(6, 1), U32Distr.BitsOffset(8, 65),
                U32Distr.BitsOffset(10, 321), U32Distr.BitsOffset(12, 1345));
        }

        _ratio = (uint)br.ReadBits(3);

        if (_ratio == 0)
        {
            if (_div8)
            {
                _xsizeDiv8 = FieldReader.ReadU32(br,
                    U32Distr.Val(16), U32Distr.Val(32),
                    U32Distr.BitsOffset(5, 1), U32Distr.BitsOffset(9, 33));
            }
            else
            {
                _xsize = FieldReader.ReadU32(br,
                    U32Distr.BitsOffset(6, 1), U32Distr.BitsOffset(8, 65),
                    U32Distr.BitsOffset(10, 321), U32Distr.BitsOffset(12, 1345));
            }
        }

        return true;
    }
}

/// <summary>
/// Image metadata. Contains bit depth, color encoding, tone mapping,
/// extra channels, animation, and preview information.
/// Port of jxl::ImageMetadata.
/// </summary>
public class ImageMetadata
{
    public bool AllDefault;
    public uint Orientation = 1; // EXIF 1-8
    public bool HaveIntrinsicSize;
    public SizeHeader IntrinsicSize = new();
    public bool HavePreview;
    public PreviewHeader PreviewSize = new();
    public bool HaveAnimation;
    public AnimationHeader Animation = new();
    public BitDepthInfo BitDepth = new();
    public bool Modular16BitBufferSufficient = true;
    public uint NumExtraChannels;
    public ExtraChannelInfo[] ExtraChannelInfos = [];
    public bool XybEncoded = true;
    public ColorEncoding ColorEnc = new();
    public ToneMapping Tone = new();
    public ulong Extensions;
    public bool NonserializedOnlyParseBasicInfo;

    // Derived
    public CustomTransformData CustomTransform = new();

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        // AllDefault check
        AllDefault = FieldReader.ReadBool(br);
        if (AllDefault)
        {
            // All defaults: 8-bit sRGB, no extra channels
            return true;
        }

        // Extra fields flag
        bool extraFields = FieldReader.ReadBool(br);

        if (extraFields)
        {
            // Orientation (3 bits, stored as 0-7, actual is 1-8)
            Orientation = (uint)br.ReadBits(3) + 1;

            // Intrinsic size
            HaveIntrinsicSize = FieldReader.ReadBool(br);
            if (HaveIntrinsicSize)
            {
                var status = IntrinsicSize.ReadFromBitStream(br);
                if (!status) return false;
            }

            // Preview
            HavePreview = FieldReader.ReadBool(br);
            if (HavePreview)
            {
                var status = PreviewSize.ReadFromBitStream(br);
                if (!status) return false;
            }

            // Animation
            HaveAnimation = FieldReader.ReadBool(br);
            if (HaveAnimation)
            {
                var status = Animation.ReadFromBitStream(br);
                if (!status) return false;
            }
        }

        // Bit depth
        var bdStatus = BitDepth.ReadFromBitStream(br);
        if (!bdStatus) return false;

        // Modular 16-bit flag
        Modular16BitBufferSufficient = FieldReader.ReadBool(br);

        // Extra channels
        NumExtraChannels = FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Val(1),
            U32Distr.BitsOffset(4, 2), U32Distr.BitsOffset(12, 1));

        if (NumExtraChannels != 0)
        {
            ExtraChannelInfos = new ExtraChannelInfo[NumExtraChannels];
            for (int i = 0; i < (int)NumExtraChannels; i++)
            {
                ExtraChannelInfos[i] = new ExtraChannelInfo();
                var status = ExtraChannelInfos[i].ReadFromBitStream(br);
                if (!status) return false;
            }
        }

        // XYB encoded
        XybEncoded = FieldReader.ReadBool(br);

        // Color encoding
        var ceStatus = ColorEnc.ReadFromBitStream(br);
        if (!ceStatus) return false;

        // Tone mapping (if extra fields)
        if (extraFields)
        {
            var tmStatus = Tone.ReadFromBitStream(br);
            if (!tmStatus) return false;
        }

        if (NonserializedOnlyParseBasicInfo) return true;

        // Extensions
        Extensions = U64Coder.Read(br);
        // Skip unknown extensions
        return true;
    }
}

/// <summary>Custom transform data (opsin inverse matrix, upsampling weights).</summary>
public class CustomTransformData
{
    public bool AllDefault;
    public OpsinInverseMatrix OpsinInverse = new();
    public uint CustomWeightsMask;
    public float[] Upsampling2Weights = new float[15];
    public float[] Upsampling4Weights = new float[55];
    public float[] Upsampling8Weights = new float[210];

    public bool NonserializedXybEncoded;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        AllDefault = FieldReader.ReadBool(br);
        if (AllDefault) return true;

        if (NonserializedXybEncoded)
        {
            var status = OpsinInverse.ReadFromBitStream(br);
            if (!status) return false;
        }

        CustomWeightsMask = (uint)br.ReadBits(3);

        if ((CustomWeightsMask & 0x1) != 0)
        {
            for (int i = 0; i < 15; i++)
                F16Coder.Read(br, out Upsampling2Weights[i]);
        }

        if ((CustomWeightsMask & 0x2) != 0)
        {
            for (int i = 0; i < 55; i++)
                F16Coder.Read(br, out Upsampling4Weights[i]);
        }

        if ((CustomWeightsMask & 0x4) != 0)
        {
            for (int i = 0; i < 210; i++)
                F16Coder.Read(br, out Upsampling8Weights[i]);
        }

        return true;
    }
}

/// <summary>Opsin inverse matrix (3x3 color transform).</summary>
public class OpsinInverseMatrix
{
    public bool AllDefault;
    public float[,] InverseMatrix = new float[3, 3];
    public float[] OpsinBiases = new float[3];
    public float[] QuantBiases = new float[4];

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        AllDefault = FieldReader.ReadBool(br);
        if (AllDefault) return true;

        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
                F16Coder.Read(br, out InverseMatrix[j, i]);

        for (int i = 0; i < 3; i++)
            F16Coder.Read(br, out OpsinBiases[i]);

        for (int i = 0; i < 4; i++)
            F16Coder.Read(br, out QuantBiases[i]);

        return true;
    }
}
