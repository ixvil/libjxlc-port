// Port of lib/jxl/modular/transform/transform.h/cc — modular transforms
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;

namespace LibJxl.Modular;

/// <summary>Transform types. Port of jxl::TransformId.</summary>
public enum TransformId : uint
{
    RCT = 0,      // Reversible Color Transform
    Palette = 1,  // Palette reduction
    Squeeze = 2,  // Haar-like squeezing
    Invalid = 3,
}

/// <summary>Parameters for a squeeze operation.</summary>
public struct SqueezeParams
{
    public bool InPlace;
    public int BeginC;
    public int NumC;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        InPlace = FieldReader.ReadBool(br);
        BeginC = (int)FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Val(1),
            U32Distr.Val(2), U32Distr.BitsOffset(4, 3));
        NumC = (int)FieldReader.ReadU32(br,
            U32Distr.Val(1), U32Distr.Val(2),
            U32Distr.Val(3), U32Distr.BitsOffset(4, 4));
        return true;
    }
}

/// <summary>
/// A modular transform (serializable).
/// Port of jxl::Transform.
/// </summary>
public class Transform
{
    public TransformId Id;
    public int BeginC;
    public int RctType = 6; // Default: YCoCg
    public int NumC = 3;
    public int NbColors;
    public int NbDeltas;
    public Predictor PalettePredictor = Predictor.Zero;
    public bool OrderedPalette;
    public bool LossyPalette;
    public SqueezeParams[] Squeezes = [];

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        Id = (TransformId)FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Val(1),
            U32Distr.Val(2), U32Distr.Val(3));

        if (Id > TransformId.Squeeze) return false;

        BeginC = (int)FieldReader.ReadU32(br,
            U32Distr.Val(0), U32Distr.Val(1),
            U32Distr.Val(2), U32Distr.BitsOffset(6, 3));

        if (Id == TransformId.RCT)
        {
            RctType = (int)FieldReader.ReadU32(br,
                U32Distr.Val(6), U32Distr.BitsOffset(2, 2),
                U32Distr.BitsOffset(4, 10), U32Distr.BitsOffset(6, 26));
        }

        if (Id == TransformId.Palette)
        {
            NumC = (int)FieldReader.ReadU32(br,
                U32Distr.Val(1), U32Distr.Val(3),
                U32Distr.Val(4), U32Distr.BitsOffset(13, 1));
            NbColors = (int)FieldReader.ReadU32(br,
                U32Distr.BitsOffset(8, 0), U32Distr.BitsOffset(10, 256),
                U32Distr.BitsOffset(12, 1280), U32Distr.BitsOffset(16, 5376));
            NbDeltas = (int)FieldReader.ReadU32(br,
                U32Distr.Val(0), U32Distr.BitsOffset(8, 1),
                U32Distr.BitsOffset(10, 257), U32Distr.BitsOffset(16, 1281));
            PalettePredictor = (Predictor)(int)FieldReader.ReadU32(br,
                U32Distr.Val(0), U32Distr.Val(1),
                U32Distr.Val(2), U32Distr.BitsOffset(4, 3));
        }

        if (Id == TransformId.Squeeze)
        {
            int numSqueezes = (int)FieldReader.ReadU32(br,
                U32Distr.Val(0), U32Distr.BitsOffset(4, 1),
                U32Distr.BitsOffset(6, 9), U32Distr.BitsOffset(8, 41));
            if (numSqueezes > 0)
            {
                Squeezes = new SqueezeParams[numSqueezes];
                for (int i = 0; i < numSqueezes; i++)
                {
                    var s = Squeezes[i].ReadFromBitStream(br);
                    if (!s) return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Applies the inverse transform to the image (decoder path).
    /// </summary>
    public JxlStatus Inverse(ModularImage image, WeightedHeader wpHeader)
    {
        switch (Id)
        {
            case TransformId.RCT:
                return InverseRCT(image);
            case TransformId.Squeeze:
                return InverseSqueeze(image);
            case TransformId.Palette:
                return InversePalette(image);
            default:
                return false;
        }
    }

    private JxlStatus InverseRCT(ModularImage image)
    {
        if (BeginC + 2 >= image.Channels.Count) return false;

        var c0 = image.Channels[BeginC];
        var c1 = image.Channels[BeginC + 1];
        var c2 = image.Channels[BeginC + 2];

        int permutation = RctType / 7;
        int rctType = RctType % 7;

        for (int y = 0; y < c0.H; y++)
        {
            var r0 = c0.Row(y);
            var r1 = c1.Row(y);
            var r2 = c2.Row(y);

            for (int x = 0; x < c0.W; x++)
            {
                int a = r0[x], b = r1[x], c = r2[x];
                int ra, rb, rc;

                switch (rctType)
                {
                    case 0: // None
                        ra = a; rb = b; rc = c; break;
                    case 1: // YCoCg
                        rb = a - (c >> 1);
                        ra = c + rb;
                        rc = rb - (b >> 1);
                        rb = b + rc;
                        break;
                    case 2:
                        rb = a + b;
                        ra = a; rc = c;
                        break;
                    case 3:
                        ra = a + b;
                        rb = b; rc = c;
                        break;
                    case 4:
                        rb = b;
                        rc = a + c;
                        ra = a;
                        break;
                    case 5:
                        rb = a + b;
                        rc = a + c;
                        ra = a;
                        break;
                    case 6: // YCoCg (most common)
                        ra = a - (b >> 1);
                        rc = ra - c;
                        rb = rc + b;
                        ra = a; // Actually: a
                        // Re-derive: tmp = a - floor(b/2); B = tmp - c; G = b + B; R = tmp
                        // So: R = a - floor(b/2), B = R - c, G = b + B
                        ra = a - (b >> 1);
                        rc = ra - c;
                        rb = b + rc;
                        break;
                    default:
                        ra = a; rb = b; rc = c; break;
                }

                // Apply permutation
                int v0, v1, v2;
                switch (permutation)
                {
                    case 0: v0 = ra; v1 = rb; v2 = rc; break;
                    case 1: v0 = ra; v1 = rc; v2 = rb; break;
                    case 2: v0 = rb; v1 = ra; v2 = rc; break;
                    case 3: v0 = rb; v1 = rc; v2 = ra; break;
                    case 4: v0 = rc; v1 = ra; v2 = rb; break;
                    case 5: v0 = rc; v1 = rb; v2 = ra; break;
                    default: v0 = ra; v1 = rb; v2 = rc; break;
                }

                r0[x] = v0;
                r1[x] = v1;
                r2[x] = v2;
            }
        }

        return true;
    }

    private JxlStatus InverseSqueeze(ModularImage image)
    {
        // Squeeze undoing is complex — stub for now
        // In full implementation: for each squeeze params in reverse,
        // merge the residual and low-pass channels
        return true;
    }

    private JxlStatus InversePalette(ModularImage image)
    {
        // Palette expansion: use palette channel to look up colors
        // Stub for now — full implementation needed for palette images
        return true;
    }
}
