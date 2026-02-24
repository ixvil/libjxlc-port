// Port of lib/jxl/cms/opsin_params.h + lib/jxl/dec_xyb.h — XYB color space constants and parameters

namespace LibJxl.ColorManagement;

/// <summary>
/// Constants defining the XYB color space.
/// Port of jxl::cms::opsin_params.h constants.
/// </summary>
public static class OpsinConstants
{
    // Forward opsin absorbance matrix
    public const float kM02 = 0.078f;
    public const float kM00 = 0.30f;
    public const float kM01 = 1.0f - kM02 - kM00; // 0.622f

    public const float kM12 = 0.078f;
    public const float kM10 = 0.23f;
    public const float kM11 = 1.0f - kM12 - kM10; // 0.692f

    public const float kM20 = 0.24342268924547819f;
    public const float kM21 = 0.20476744424496821f;
    public const float kM22 = 1.0f - kM20 - kM21; // 0.5518098665595536f

    public const float kBScale = 1.0f;
    public const float kYToBRatio = 1.0f;
    public const float kBToYRatio = 1.0f / kYToBRatio;

    public const float kOpsinAbsorbanceBias0 = 0.0037930732552754493f;
    public const float kOpsinAbsorbanceBias1 = kOpsinAbsorbanceBias0;
    public const float kOpsinAbsorbanceBias2 = kOpsinAbsorbanceBias0;

    /// <summary>Forward opsin absorbance matrix (3x3, row-major).</summary>
    public static readonly float[,] OpsinAbsorbanceMatrix =
    {
        { kM00, kM01, kM02 },
        { kM10, kM11, kM12 },
        { kM20, kM21, kM22 },
    };

    /// <summary>Default inverse opsin absorbance matrix (3x3, row-major).</summary>
    public static readonly float[,] DefaultInverseOpsinAbsorbanceMatrix =
    {
        { 11.031566901960783f, -9.866943921568629f, -0.16462299647058826f },
        { -3.254147380392157f, 4.418770392156863f, -0.16462299647058826f },
        { -3.6588512862745097f, 2.7129230470588235f, 1.9459282392156863f },
    };

    /// <summary>Negative opsin biases (with SIMD padding).</summary>
    public static readonly float[] NegOpsinAbsorbanceBiasRGB =
    {
        -kOpsinAbsorbanceBias0, -kOpsinAbsorbanceBias1, -kOpsinAbsorbanceBias2, 1.0f
    };

    // Scaled XYB constants
    public const float kScaledXYBOffset0 = 0.015386134f;
    public const float kScaledXYBOffset1 = 0.0f;
    public const float kScaledXYBOffset2 = 0.27770459f;

    public const float kScaledXYBScale0 = 22.995788804f;
    public const float kScaledXYBScale1 = 1.183000077f;
    public const float kScaledXYBScale2 = 1.502141333f;
}

/// <summary>
/// Parameters for XYB→sRGB conversion.
/// Port of jxl::OpsinParams from dec_xyb.h.
/// </summary>
public class OpsinParams
{
    /// <summary>Inverse opsin matrix (9 entries, each broadcast to 4 floats for SIMD).</summary>
    public readonly float[] InverseOpsinMatrix = new float[9 * 4];

    /// <summary>Negative opsin biases [4] (r, g, b, 1.0).</summary>
    public readonly float[] OpsinBiases = new float[4];

    /// <summary>Cube roots of opsin biases [4].</summary>
    public readonly float[] OpsinBiasesCbrt = new float[4];

    /// <summary>Quantization biases [4].</summary>
    public readonly float[] QuantBiases = new float[4];

    /// <summary>
    /// Initialize parameters for the given intensity target.
    /// Port of OpsinParams::Init from dec_xyb.cc.
    /// </summary>
    public void Init(float intensityTarget)
    {
        InitSIMDInverseMatrix(OpsinConstants.DefaultInverseOpsinAbsorbanceMatrix,
            InverseOpsinMatrix, intensityTarget);

        Array.Copy(OpsinConstants.NegOpsinAbsorbanceBiasRGB, OpsinBiases, 4);

        for (int c = 0; c < 4; c++)
        {
            OpsinBiasesCbrt[c] = MathF.Cbrt(OpsinBiases[c]);
        }
    }

    /// <summary>
    /// Initialize with default intensity target (255).
    /// </summary>
    public void InitDefault()
    {
        Init(255.0f);
    }

    /// <summary>
    /// Initializes the SIMD-padded inverse matrix from a 3x3 matrix.
    /// Port of jxl::InitSIMDInverseMatrix from opsin_params.cc.
    /// Each entry is broadcast 4 times and scaled by 255/intensity_target.
    /// </summary>
    public static void InitSIMDInverseMatrix(float[,] inverse, float[] simdInverse, float intensityTarget)
    {
        float scale = 255.0f / intensityTarget;
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++)
            {
                int idx = (j * 3 + i) * 4;
                float val = inverse[j, i] * scale;
                simdInverse[idx] = val;
                simdInverse[idx + 1] = val;
                simdInverse[idx + 2] = val;
                simdInverse[idx + 3] = val;
            }
        }
    }
}
