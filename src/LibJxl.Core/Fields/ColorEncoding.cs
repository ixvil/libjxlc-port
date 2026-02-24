// Port of ColorEncoding from lib/jxl/color_encoding_internal.h/cc
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;

namespace LibJxl.Fields;

/// <summary>Custom xy chromaticity coordinate pair.</summary>
public struct Customxy
{
    public int X; // Fixed-point: actual = X / 1e6
    public int Y;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        uint ux = FieldReader.ReadU32(br,
            U32Distr.Bits(19),
            U32Distr.BitsOffset(19, 524288),
            U32Distr.BitsOffset(20, 1048576),
            U32Distr.BitsOffset(21, 2097152));
        X = SignedPack.UnpackSigned(ux);

        uint uy = FieldReader.ReadU32(br,
            U32Distr.Bits(19),
            U32Distr.BitsOffset(19, 524288),
            U32Distr.BitsOffset(20, 1048576),
            U32Distr.BitsOffset(21, 2097152));
        Y = SignedPack.UnpackSigned(uy);

        return true;
    }
}

/// <summary>Custom transfer function parameters.</summary>
public class CustomTransferFunction
{
    public bool HaveGamma;
    public uint Gamma; // gamma * 1e7
    public TransferFunction TF = TransferFunction.SRGB;
    public ColorSpace NonserializedColorSpace = ColorSpace.RGB;

    public const uint GammaMul = 10000000;

    private bool SetImplicit()
    {
        // XYB color space implies linear transfer function
        return NonserializedColorSpace == ColorSpace.XYB;
    }

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        if (SetImplicit())
        {
            TF = TransferFunction.Linear;
            return true;
        }

        HaveGamma = FieldReader.ReadBool(br);

        if (HaveGamma)
        {
            Gamma = (uint)br.ReadBits(24);
            if (Gamma > GammaMul) return false;
        }
        else
        {
            TF = EnumReader.ReadEnum(br, EnumValues.TransferFunctions);
        }

        return true;
    }
}

/// <summary>
/// Color encoding specification. Describes the color space, white point,
/// primaries, transfer function, and rendering intent.
/// Port of jxl::ColorEncoding.
/// </summary>
public class ColorEncoding
{
    public bool AllDefault;
    public bool WantICC;
    public ColorSpace Space = ColorSpace.RGB;
    public WhitePoint WP = WhitePoint.D65;
    public Primaries Prims = Primaries.SRGB;
    public RenderingIntent Intent = RenderingIntent.Relative;
    public CustomTransferFunction TF = new();
    public Customxy White;
    public Customxy Red, Green, Blue;

    private bool HasPrimaries => Space == ColorSpace.RGB || Space == ColorSpace.Unknown;
    private bool ImplicitWhitePoint => Space == ColorSpace.XYB;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        // AllDefault check
        bool allDefault = FieldReader.ReadBool(br);
        AllDefault = allDefault;
        if (allDefault)
        {
            // Set defaults: sRGB
            WantICC = false;
            Space = ColorSpace.RGB;
            WP = WhitePoint.D65;
            Prims = Primaries.SRGB;
            TF.TF = TransferFunction.SRGB;
            Intent = RenderingIntent.Relative;
            return true;
        }

        WantICC = FieldReader.ReadBool(br);

        // Color space is always sent
        Space = EnumReader.ReadEnum(br, EnumValues.ColorSpaces);

        if (!WantICC)
        {
            // White point (unless implicit)
            if (!ImplicitWhitePoint)
            {
                WP = EnumReader.ReadEnum(br, EnumValues.WhitePoints);
                if (WP == WhitePoint.Custom)
                {
                    var status = White.ReadFromBitStream(br);
                    if (!status) return false;
                }
            }

            // Primaries (only for RGB-like)
            if (HasPrimaries)
            {
                Prims = EnumReader.ReadEnum(br, EnumValues.PrimariesValues);
                if (Prims == Primaries.Custom)
                {
                    var rStatus = Red.ReadFromBitStream(br);
                    if (!rStatus) return false;
                    var gStatus = Green.ReadFromBitStream(br);
                    if (!gStatus) return false;
                    var bStatus = Blue.ReadFromBitStream(br);
                    if (!bStatus) return false;
                }
            }

            // Transfer function
            TF.NonserializedColorSpace = Space;
            var tfStatus = TF.ReadFromBitStream(br);
            if (!tfStatus) return false;

            // Rendering intent
            Intent = EnumReader.ReadEnum(br, EnumValues.RenderingIntents);
        }

        return true;
    }
}
