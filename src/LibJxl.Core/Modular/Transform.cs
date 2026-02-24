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
    public bool Horizontal;
    public bool InPlace;
    public int BeginC;
    public int NumC;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        Horizontal = FieldReader.ReadBool(br);
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
        if (Squeezes.Length == 0)
        {
            // Auto-squeeze: use default squeeze parameters
            // TODO: generate default squeezes based on image dimensions
            return true;
        }

        // Process squeezes in reverse order
        for (int s = Squeezes.Length - 1; s >= 0; s--)
        {
            var sp = Squeezes[s];

            for (int c = sp.BeginC; c < sp.BeginC + sp.NumC; c++)
            {
                // Find residual channel index (after the data channels)
                int residIdx = sp.BeginC + sp.NumC + (c - sp.BeginC);
                if (residIdx >= image.Channels.Count) return false;
                if (c >= image.Channels.Count) return false;

                var avg = image.Channels[c];
                var resid = image.Channels[residIdx];

                if (sp.Horizontal)
                {
                    InvHSqueeze(avg, resid);
                }
                else
                {
                    InvVSqueeze(avg, resid);
                }
            }

            // Remove residual channels
            for (int i = 0; i < sp.NumC; i++)
            {
                int residIdx = sp.BeginC + sp.NumC;
                if (residIdx < image.Channels.Count)
                    image.Channels.RemoveAt(residIdx);
            }
        }

        return true;
    }

    /// <summary>
    /// Smooth tendency for Haar-like wavelet reconstruction.
    /// Port of SmoothTendency from squeeze.h.
    /// </summary>
    private static int SmoothTendency(int B, int a, int n)
    {
        // Only apply tendency in smooth gradients
        if (B >= a && a >= n)
        {
            int diff = (4 * B - 3 * n - a + 6) / 12;
            // Clamp to preserve monotonicity
            if (diff - (diff & 1) > 2 * (B - a)) diff = 2 * (B - a) + 1;
            if (diff + (diff & 1) > 2 * (a - n)) diff = 2 * (a - n);
            return diff;
        }
        else if (B <= a && a <= n)
        {
            int diff = (4 * B - 3 * n - a - 6) / 12;
            // Clamp for ascending gradient
            if (diff + (diff & 1) < 2 * (B - a)) diff = 2 * (B - a) - 1;
            if (diff - (diff & 1) < 2 * (a - n)) diff = 2 * (a - n);
            return diff;
        }
        return 0; // Not smooth: use zero tendency
    }

    /// <summary>Inverse horizontal squeeze.</summary>
    private static void InvHSqueeze(Channel avgCh, Channel residCh)
    {
        int outW = avgCh.W + residCh.W;
        int h = avgCh.H;

        var result = Channel.Create(outW, h, avgCh.HShift - 1, avgCh.VShift);

        for (int y = 0; y < h; y++)
        {
            var avg = avgCh.Row(y);
            var res = residCh.Row(y);
            var outRow = result.Row(y);

            int avgW = avgCh.W;
            int resW = residCh.W;

            for (int x = 0; x < resW; x++)
            {
                int a = avg[x];
                int nextAvg = (x + 1 < avgW) ? avg[x + 1] : a;
                int left = (x > 0) ? outRow[2 * x - 1] : a;

                int tendency = SmoothTendency(left, a, nextAvg);
                int diff = res[x] + tendency;

                int outA = a + (diff / 2);
                int outB = outA - diff;

                outRow[2 * x] = outA;
                if (2 * x + 1 < outW)
                    outRow[2 * x + 1] = outB;
            }

            // If output has odd width, last pixel = last average
            if (outW > 2 * resW && avgW > resW)
            {
                outRow[outW - 1] = avg[avgW - 1];
            }
        }

        // Copy result back to average channel
        avgCh.Pixels = result.Pixels;
        avgCh.W = result.W;
        avgCh.H = result.H;
        avgCh.HShift = result.HShift;
    }

    /// <summary>Inverse vertical squeeze.</summary>
    private static void InvVSqueeze(Channel avgCh, Channel residCh)
    {
        int w = avgCh.W;
        int outH = avgCh.H + residCh.H;

        var result = Channel.Create(w, outH, avgCh.HShift, avgCh.VShift - 1);

        int avgH = avgCh.H;
        int resH = residCh.H;

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < resH; y++)
            {
                int a = avgCh.Row(y)[x];
                int nextAvg = (y + 1 < avgH) ? avgCh.Row(y + 1)[x] : a;
                int top = (y > 0) ? result.Row(2 * y - 1)[x] : a;

                int tendency = SmoothTendency(top, a, nextAvg);
                int diff = residCh.Row(y)[x] + tendency;

                int outA = a + (diff / 2);
                int outB = outA - diff;

                result.Row(2 * y)[x] = outA;
                if (2 * y + 1 < outH)
                    result.Row(2 * y + 1)[x] = outB;
            }

            // If output has odd height, last pixel = last average
            if (outH > 2 * resH && avgH > resH)
            {
                result.Row(outH - 1)[x] = avgCh.Row(avgH - 1)[x];
            }
        }

        // Copy result back
        avgCh.Pixels = result.Pixels;
        avgCh.W = result.W;
        avgCh.H = result.H;
        avgCh.VShift = result.VShift;
    }

    private JxlStatus InversePalette(ModularImage image)
    {
        if (BeginC >= image.Channels.Count) return false;

        // Channel 0 (or BeginC) = palette data, next channel = indices
        int palIdx = BeginC;
        int idxChannel = palIdx + 1;
        if (idxChannel >= image.Channels.Count) return false;

        var palette = image.Channels[palIdx];
        var indices = image.Channels[idxChannel];

        int paletteSize = NbColors;
        int nbChannels = NumC;
        int onerow = palette.W; // palette width = nb_colors + nb_deltas
        int bitDepth = image.BitDepth;

        // Create output channels
        var outputChannels = new Channel[nbChannels];
        for (int c = 0; c < nbChannels; c++)
        {
            outputChannels[c] = Channel.Create(indices.W, indices.H,
                                                indices.HShift, indices.VShift);
        }

        // Expand palette indices to pixel values
        for (int y = 0; y < indices.H; y++)
        {
            var idxRow = indices.Row(y);
            for (int x = 0; x < indices.W; x++)
            {
                int index = idxRow[x];

                for (int c = 0; c < nbChannels; c++)
                {
                    outputChannels[c].Row(y)[x] = GetPaletteValue(
                        palette, index, c, paletteSize, onerow, bitDepth);
                }
            }
        }

        // Replace palette + indices channels with output channels
        image.Channels.RemoveAt(idxChannel);
        image.Channels.RemoveAt(palIdx);
        for (int c = 0; c < nbChannels; c++)
        {
            image.Channels.Insert(palIdx + c, outputChannels[c]);
        }

        if (image.NbMetaChannels > 0)
            image.NbMetaChannels--;

        return true;
    }

    /// <summary>Delta palette table (72 pre-computed color deltas).</summary>
    private static readonly int[,] kDeltaPalette = new int[72, 3]
    {
        { 0, 0, 0 },   { 4, 4, 4 },   { 11, 0, 0 },  { 0, 0, -13 },
        { 0, -12, 0 }, { -10, -10, -10 }, { -18, -18, -18 }, { -27, -27, -27 },
        { -18, -18, 0 }, { 0, 0, -25 }, { -10, -10, 0 }, { -10, 0, -10 },
        { 0, -10, -10 }, { -5, -5, -5 }, { -5, -5, 0 },  { -5, 0, -5 },
        { 0, -5, -5 }, { 0, -5, 0 },  { -5, 0, 0 },  { 0, 0, -5 },
        { -2, -2, -2 }, { -1, -1, -1 }, { -4, -4, -4 }, { -7, -7, -7 },
        { -3, -3, -3 }, { -12, -12, -12 }, { -1, 0, 0 }, { 0, -1, 0 },
        { 0, 0, -1 },  { -1, -1, 0 }, { -1, 0, -1 }, { 0, -1, -1 },
        { -2, -2, 0 }, { -2, 0, -2 }, { 0, -2, -2 }, { -4, -4, 0 },
        { -4, 0, -4 }, { 0, -4, -4 }, { -7, 0, 0 },  { 0, -7, 0 },
        { 0, 0, -7 },  { -7, -7, 0 }, { -7, 0, -7 }, { 0, -7, -7 },
        { -13, -13, 0 }, { -13, 0, -13 }, { 0, -13, -13 }, { -18, 0, 0 },
        { 0, -18, 0 }, { 0, 0, -18 }, { -22, 0, 0 },  { 0, -22, 0 },
        { 0, 0, -22 }, { -22, -22, 0 }, { -22, 0, -22 }, { 0, -22, -22 },
        { -30, -30, -30 }, { -30, 0, 0 }, { 0, -30, 0 }, { 0, 0, -30 },
        { -30, -30, 0 }, { -30, 0, -30 }, { 0, -30, -30 }, { -40, -40, -40 },
        { -40, 0, 0 }, { 0, -40, 0 },  { 0, 0, -40 }, { -40, -40, 0 },
        { -40, 0, -40 }, { 0, -40, -40 }, { -40, 0, -40 }, { 0, -40, -40 },
    };

    /// <summary>
    /// Gets a palette value for the given index and channel.
    /// Port of GetPaletteValue from palette.h.
    /// </summary>
    private static int GetPaletteValue(Channel palette, int index, int c,
                                        int paletteSize, int onerow, int bitDepth)
    {
        const int kSmallCube = 4;
        const int kLargeCube = 5;

        if (index < 0)
        {
            // Delta palette
            int di = -(index + 1);
            int numDeltas = 2 * 72 - 1; // 143
            di %= numDeltas;

            int baseIdx = (di + 1) >> 1;
            if (baseIdx >= 72) baseIdx = 71;
            int multiplier = ((di & 1) != 0) ? 1 : -1;

            int result = kDeltaPalette[baseIdx, Math.Min(c, 2)] * multiplier;
            if (bitDepth > 8)
                result *= (1 << (bitDepth - 8));

            return result;
        }

        if (index < paletteSize)
        {
            // Explicit palette lookup
            if (c < palette.H && index < palette.W)
                return palette.Row(c)[index];
            return 0;
        }

        // Implicit palettes
        int implicitIdx = index - paletteSize;
        int smallCubeSize = kSmallCube * kSmallCube * kSmallCube; // 64
        int largeCubeSize = kLargeCube * kLargeCube * kLargeCube; // 125

        if (implicitIdx < smallCubeSize)
        {
            // Small cube (4×4×4)
            int val = (implicitIdx >> (c * 2)) % kSmallCube;
            int maxVal = (1 << bitDepth) - 1;
            return (val * maxVal) / (kSmallCube - 1) + (1 << Math.Max(0, bitDepth - 3));
        }

        implicitIdx -= smallCubeSize;
        if (implicitIdx < largeCubeSize)
        {
            // Large cube (5×5×5)
            int divisor = 1;
            for (int i = 0; i < c; i++) divisor *= kLargeCube;
            int val = (implicitIdx / divisor) % kLargeCube;
            int maxVal = (1 << bitDepth) - 1;
            return (val * maxVal) / (kLargeCube - 1);
        }

        return 0;
    }
}
