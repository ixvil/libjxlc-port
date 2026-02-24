// Port of lib/jxl/dec_xyb-inl.h — XYB → linear RGB conversion

namespace LibJxl.ColorManagement;

/// <summary>
/// Converts XYB color space to linear RGB.
/// Port of XybToRgb from dec_xyb-inl.h (scalar path).
/// </summary>
public static class XybConverter
{
    /// <summary>
    /// Converts a single pixel from XYB to linear RGB.
    /// Port of the scalar path of XybToRgb from dec_xyb-inl.h.
    /// </summary>
    public static void XybToLinearRgb(
        float opsinX, float opsinY, float opsinB,
        OpsinParams opsinParams,
        out float linearR, out float linearG, out float linearB)
    {
        float[] biases = opsinParams.OpsinBiases;
        float[] biasesCbrt = opsinParams.OpsinBiasesCbrt;
        float[] matrix = opsinParams.InverseOpsinMatrix;

        // Step 1: XYB → gamma RGB
        float gammaR = opsinY + opsinX;
        float gammaG = opsinY - opsinX;
        float gammaB = opsinB;

        // Step 2: Subtract cube-root bias
        gammaR -= biasesCbrt[0];
        gammaG -= biasesCbrt[1];
        gammaB -= biasesCbrt[2];

        // Step 3: Undo gamma (cube) and add negative bias
        float mixedR = gammaR * gammaR * gammaR + biases[0];
        float mixedG = gammaG * gammaG * gammaG + biases[1];
        float mixedB = gammaB * gammaB * gammaB + biases[2];

        // Step 4: Unmix via inverse opsin matrix (SIMD layout: each entry at idx*4)
        linearR = matrix[0 * 4] * mixedR + matrix[1 * 4] * mixedG + matrix[2 * 4] * mixedB;
        linearG = matrix[3 * 4] * mixedR + matrix[4 * 4] * mixedG + matrix[5 * 4] * mixedB;
        linearB = matrix[6 * 4] * mixedR + matrix[7 * 4] * mixedG + matrix[8 * 4] * mixedB;
    }

    /// <summary>
    /// Converts a single pixel from XYB to sRGB [0,1].
    /// Combines XYB→Linear→sRGB.
    /// </summary>
    public static void XybToSrgb(
        float opsinX, float opsinY, float opsinB,
        OpsinParams opsinParams,
        out float sR, out float sG, out float sB)
    {
        XybToLinearRgb(opsinX, opsinY, opsinB, opsinParams,
            out float linearR, out float linearG, out float linearB);

        sR = SrgbTransferFunction.LinearToSrgb(linearR);
        sG = SrgbTransferFunction.LinearToSrgb(linearG);
        sB = SrgbTransferFunction.LinearToSrgb(linearB);
    }

    /// <summary>
    /// Converts a row of pixels from XYB to linear RGB.
    /// Port of OpsinToLinear row processing from dec_xyb.cc.
    /// </summary>
    public static void OpsinToLinearRow(
        ReadOnlySpan<float> rowX, ReadOnlySpan<float> rowY, ReadOnlySpan<float> rowB,
        Span<float> outR, Span<float> outG, Span<float> outB,
        int count, OpsinParams opsinParams)
    {
        float[] biases = opsinParams.OpsinBiases;
        float[] biasesCbrt = opsinParams.OpsinBiasesCbrt;
        float[] matrix = opsinParams.InverseOpsinMatrix;

        float m00 = matrix[0 * 4], m01 = matrix[1 * 4], m02 = matrix[2 * 4];
        float m10 = matrix[3 * 4], m11 = matrix[4 * 4], m12 = matrix[5 * 4];
        float m20 = matrix[6 * 4], m21 = matrix[7 * 4], m22 = matrix[8 * 4];

        float bcr0 = biasesCbrt[0], bcr1 = biasesCbrt[1], bcr2 = biasesCbrt[2];
        float nb0 = biases[0], nb1 = biases[1], nb2 = biases[2];

        for (int x = 0; x < count; x++)
        {
            float gammaR = rowY[x] + rowX[x] - bcr0;
            float gammaG = rowY[x] - rowX[x] - bcr1;
            float gammaB = rowB[x] - bcr2;

            float mixedR = gammaR * gammaR * gammaR + nb0;
            float mixedG = gammaG * gammaG * gammaG + nb1;
            float mixedB = gammaB * gammaB * gammaB + nb2;

            outR[x] = m00 * mixedR + m01 * mixedG + m02 * mixedB;
            outG[x] = m10 * mixedR + m11 * mixedG + m12 * mixedB;
            outB[x] = m20 * mixedR + m21 * mixedG + m22 * mixedB;
        }
    }

    /// <summary>
    /// Converts a row of linear RGB to sRGB bytes [0, 255].
    /// </summary>
    public static void LinearToSrgb8Row(
        ReadOnlySpan<float> rowR, ReadOnlySpan<float> rowG, ReadOnlySpan<float> rowB,
        Span<byte> outRgb, int count, bool rgba, ReadOnlySpan<float> rowA = default)
    {
        int stride = rgba ? 4 : 3;
        for (int x = 0; x < count; x++)
        {
            float sR = SrgbTransferFunction.LinearToSrgb(rowR[x]);
            float sG = SrgbTransferFunction.LinearToSrgb(rowG[x]);
            float sB = SrgbTransferFunction.LinearToSrgb(rowB[x]);

            int offset = x * stride;
            outRgb[offset + 0] = FloatToByte(sR);
            outRgb[offset + 1] = FloatToByte(sG);
            outRgb[offset + 2] = FloatToByte(sB);

            if (rgba)
            {
                float a = rowA.IsEmpty ? 1.0f : rowA[x];
                outRgb[offset + 3] = FloatToByte(a);
            }
        }
    }

    /// <summary>
    /// Converts a [0,1] float to a [0,255] byte with clamping.
    /// </summary>
    private static byte FloatToByte(float value)
    {
        int v = (int)(value * 255.0f + 0.5f);
        return (byte)Math.Clamp(v, 0, 255);
    }
}
