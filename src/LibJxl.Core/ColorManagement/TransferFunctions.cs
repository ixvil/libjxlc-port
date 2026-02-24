// Port of lib/jxl/cms/transfer_functions-inl.h — sRGB transfer functions (scalar)

namespace LibJxl.ColorManagement;

/// <summary>
/// sRGB transfer functions (gamma encoding/decoding).
/// Port of jxl::TF_SRGB from transfer_functions-inl.h.
/// Uses rational polynomial approximation for high performance.
/// </summary>
public static class SrgbTransferFunction
{
    private const float kThreshSRGBToLinear = 0.04045f;
    private const float kThreshLinearToSRGB = 0.0031308f;
    private const float kLowDiv = 12.92f;
    private const float kLowDivInv = 1.0f / kLowDiv;

    // Rational polynomial coefficients for sRGB→Linear (4/4 degree)
    private static readonly float[] SrgbToLinearP =
    {
        2.200248328e-04f, 1.043637593e-02f, 1.624820318e-01f,
        7.961564959e-01f, 8.210152774e-01f,
    };

    private static readonly float[] SrgbToLinearQ =
    {
        2.631846970e-01f, 1.076976492e+00f, 4.987528350e-01f,
        -5.512498495e-02f, 6.521209011e-03f,
    };

    // Rational polynomial coefficients for Linear→sRGB (4/4 degree)
    private static readonly float[] LinearToSrgbP =
    {
        -5.135152395e-04f, 5.287254571e-03f, 3.903842876e-01f,
        1.474205315e+00f, 7.352629620e-01f,
    };

    private static readonly float[] LinearToSrgbQ =
    {
        1.004519624e-02f, 3.036675394e-01f, 1.340816930e+00f,
        9.258482155e-01f, 2.424867759e-02f,
    };

    /// <summary>
    /// Converts sRGB encoded value to linear light.
    /// Port of TF_SRGB::DisplayFromEncoded.
    /// </summary>
    public static float SrgbToLinear(float x)
    {
        float sign = MathF.CopySign(1.0f, x);
        x = MathF.Abs(x);

        float result;
        if (x > kThreshSRGBToLinear)
        {
            result = EvalRationalPolynomial(x, SrgbToLinearP, SrgbToLinearQ);
        }
        else
        {
            result = x * kLowDivInv;
        }

        return sign * result;
    }

    /// <summary>
    /// Converts linear light to sRGB encoded value.
    /// Port of TF_SRGB::EncodedFromDisplay.
    /// </summary>
    public static float LinearToSrgb(float x)
    {
        float sign = MathF.CopySign(1.0f, x);
        x = MathF.Abs(x);

        float result;
        if (x > kThreshLinearToSRGB)
        {
            result = EvalRationalPolynomial(MathF.Sqrt(x), LinearToSrgbP, LinearToSrgbQ);
        }
        else
        {
            result = x * kLowDiv;
        }

        return sign * result;
    }

    /// <summary>
    /// Exact sRGB→Linear using the standard formula (for validation).
    /// </summary>
    public static float SrgbToLinearExact(float x)
    {
        float sign = MathF.CopySign(1.0f, x);
        x = MathF.Abs(x);

        float result;
        if (x <= kThreshSRGBToLinear)
        {
            result = x / 12.92f;
        }
        else
        {
            result = MathF.Pow((x + 0.055f) / 1.055f, 2.4f);
        }

        return sign * result;
    }

    /// <summary>
    /// Exact Linear→sRGB using the standard formula (for validation).
    /// </summary>
    public static float LinearToSrgbExact(float x)
    {
        float sign = MathF.CopySign(1.0f, x);
        x = MathF.Abs(x);

        float result;
        if (x <= kThreshLinearToSRGB)
        {
            result = x * 12.92f;
        }
        else
        {
            result = 1.055f * MathF.Pow(x, 1.0f / 2.4f) - 0.055f;
        }

        return sign * result;
    }

    /// <summary>
    /// Evaluates a 4/4 rational polynomial p(x)/q(x).
    /// Port of EvalRationalPolynomial from rational_polynomial-inl.h (scalar).
    /// p and q are coefficient arrays of length 5 (degree 4).
    /// </summary>
    private static float EvalRationalPolynomial(float x, float[] p, float[] q)
    {
        // Horner's method for numerator
        float num = p[4];
        num = num * x + p[3];
        num = num * x + p[2];
        num = num * x + p[1];
        num = num * x + p[0];

        // Horner's method for denominator
        float den = q[4];
        den = den * x + q[3];
        den = den * x + q[2];
        den = den * x + q[1];
        den = den * x + q[0];

        return num / den;
    }
}
