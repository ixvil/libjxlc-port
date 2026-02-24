using LibJxl.ColorManagement;
using Xunit;

namespace LibJxl.Tests.ColorManagement;

public class OpsinConstantsTests
{
    [Fact]
    public void ForwardMatrix_RowsSumToOne()
    {
        // Each row of the opsin absorbance matrix should sum to ~1.0
        var m = OpsinConstants.OpsinAbsorbanceMatrix;
        for (int r = 0; r < 3; r++)
        {
            float sum = m[r, 0] + m[r, 1] + m[r, 2];
            Assert.InRange(sum, 0.999f, 1.001f);
        }
    }

    [Fact]
    public void InverseMatrix_IsInverseOfForward()
    {
        // M * M_inv should be close to identity
        var m = OpsinConstants.OpsinAbsorbanceMatrix;
        var inv = OpsinConstants.DefaultInverseOpsinAbsorbanceMatrix;

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                float sum = 0;
                for (int k = 0; k < 3; k++)
                {
                    sum += m[i, k] * inv[k, j];
                }

                float expected = i == j ? 1.0f : 0.0f;
                Assert.InRange(sum, expected - 0.001f, expected + 0.001f);
            }
        }
    }

    [Fact]
    public void BiasValues_ArePositive()
    {
        Assert.True(OpsinConstants.kOpsinAbsorbanceBias0 > 0);
        Assert.True(OpsinConstants.kOpsinAbsorbanceBias1 > 0);
        Assert.True(OpsinConstants.kOpsinAbsorbanceBias2 > 0);
    }
}

public class OpsinParamsTests
{
    [Fact]
    public void Init_DefaultIntensity_ScaleIsOne()
    {
        var p = new OpsinParams();
        p.Init(255.0f);

        // With intensity_target=255, scale = 255/255 = 1.0
        // So the inverse matrix entries should match the default inverse matrix
        float expected00 = OpsinConstants.DefaultInverseOpsinAbsorbanceMatrix[0, 0];
        Assert.InRange(p.InverseOpsinMatrix[0 * 4], expected00 - 0.001f, expected00 + 0.001f);
    }

    [Fact]
    public void Init_DoubleIntensity_ScaleIsHalf()
    {
        var p = new OpsinParams();
        p.Init(510.0f);

        // With intensity_target=510, scale = 255/510 = 0.5
        float expected00 = OpsinConstants.DefaultInverseOpsinAbsorbanceMatrix[0, 0] * 0.5f;
        Assert.InRange(p.InverseOpsinMatrix[0 * 4], expected00 - 0.001f, expected00 + 0.001f);
    }

    [Fact]
    public void Init_BiasesCbrt_AreComputed()
    {
        var p = new OpsinParams();
        p.Init(255.0f);

        // cbrt(-0.00379...) should be about -0.1559...
        Assert.InRange(p.OpsinBiasesCbrt[0], -0.157f, -0.155f);
    }

    [Fact]
    public void Init_SIMDLayout_Broadcast()
    {
        var p = new OpsinParams();
        p.Init(255.0f);

        // Each matrix entry should be broadcast to 4 consecutive floats
        for (int entry = 0; entry < 9; entry++)
        {
            int idx = entry * 4;
            Assert.Equal(p.InverseOpsinMatrix[idx], p.InverseOpsinMatrix[idx + 1]);
            Assert.Equal(p.InverseOpsinMatrix[idx], p.InverseOpsinMatrix[idx + 2]);
            Assert.Equal(p.InverseOpsinMatrix[idx], p.InverseOpsinMatrix[idx + 3]);
        }
    }
}

public class SrgbTransferFunctionTests
{
    [Fact]
    public void LinearToSrgb_Zero()
    {
        Assert.Equal(0.0f, SrgbTransferFunction.LinearToSrgb(0.0f));
    }

    [Fact]
    public void SrgbToLinear_Zero()
    {
        Assert.Equal(0.0f, SrgbTransferFunction.SrgbToLinear(0.0f));
    }

    [Fact]
    public void LinearToSrgb_One_IsApproxOne()
    {
        float result = SrgbTransferFunction.LinearToSrgb(1.0f);
        Assert.InRange(result, 0.999f, 1.001f);
    }

    [Fact]
    public void SrgbToLinear_One_IsApproxOne()
    {
        float result = SrgbTransferFunction.SrgbToLinear(1.0f);
        Assert.InRange(result, 0.999f, 1.001f);
    }

    [Fact]
    public void Roundtrip_LinearToSrgbToLinear()
    {
        float[] testValues = { 0.0f, 0.001f, 0.01f, 0.1f, 0.25f, 0.5f, 0.75f, 1.0f };
        foreach (float linear in testValues)
        {
            float srgb = SrgbTransferFunction.LinearToSrgb(linear);
            float back = SrgbTransferFunction.SrgbToLinear(srgb);
            Assert.InRange(back, linear - 0.001f, linear + 0.001f);
        }
    }

    [Fact]
    public void Roundtrip_SrgbToLinearToSrgb()
    {
        float[] testValues = { 0.0f, 0.01f, 0.04045f, 0.1f, 0.5f, 0.75f, 1.0f };
        foreach (float srgb in testValues)
        {
            float linear = SrgbTransferFunction.SrgbToLinear(srgb);
            float back = SrgbTransferFunction.LinearToSrgb(linear);
            Assert.InRange(back, srgb - 0.001f, srgb + 0.001f);
        }
    }

    [Fact]
    public void PolyApprox_MatchesExact_LinearToSrgb()
    {
        // Test that rational polynomial approximation matches the exact formula
        for (float x = 0.0f; x <= 1.0f; x += 0.01f)
        {
            float approx = SrgbTransferFunction.LinearToSrgb(x);
            float exact = SrgbTransferFunction.LinearToSrgbExact(x);
            Assert.InRange(approx, exact - 0.001f, exact + 0.001f);
        }
    }

    [Fact]
    public void PolyApprox_MatchesExact_SrgbToLinear()
    {
        for (float x = 0.0f; x <= 1.0f; x += 0.01f)
        {
            float approx = SrgbTransferFunction.SrgbToLinear(x);
            float exact = SrgbTransferFunction.SrgbToLinearExact(x);
            Assert.InRange(approx, exact - 0.001f, exact + 0.001f);
        }
    }

    [Fact]
    public void NegativeValues_MirrorSymmetry()
    {
        float pos = SrgbTransferFunction.LinearToSrgb(0.5f);
        float neg = SrgbTransferFunction.LinearToSrgb(-0.5f);
        Assert.InRange(neg, -pos - 0.0001f, -pos + 0.0001f);
    }
}

public class XybConverterTests
{
    private static OpsinParams DefaultParams()
    {
        var p = new OpsinParams();
        p.InitDefault();
        return p;
    }

    [Fact]
    public void XybToLinearRgb_BlackPixel()
    {
        // Black in XYB is (0, 0, 0).
        // Forward encoding: gamma[i] = cbrt(L[i] + bias) - cbrt(bias)
        // So for L=0: gamma = cbrt(bias) - cbrt(bias) = 0
        // XYB x=0, y=0, b=0
        var p = DefaultParams();

        XybConverter.XybToLinearRgb(0, 0, 0, p,
            out float r, out float g, out float b);

        Assert.InRange(r, -0.01f, 0.01f);
        Assert.InRange(g, -0.01f, 0.01f);
        Assert.InRange(b, -0.01f, 0.01f);
    }

    [Fact]
    public void XybToLinearRgb_ResultsAreFinite()
    {
        var p = DefaultParams();
        XybConverter.XybToLinearRgb(0.01f, 0.5f, 0.3f, p,
            out float r, out float g, out float b);

        Assert.True(float.IsFinite(r));
        Assert.True(float.IsFinite(g));
        Assert.True(float.IsFinite(b));
    }

    [Fact]
    public void OpsinToLinearRow_ProcessesMultiplePixels()
    {
        var p = DefaultParams();
        int n = 4;

        // Black in XYB = (0, 0, 0)
        float[] rowX = new float[n];
        float[] rowY = new float[n];
        float[] rowB = new float[n];
        float[] outR = new float[n];
        float[] outG = new float[n];
        float[] outB = new float[n];

        XybConverter.OpsinToLinearRow(rowX, rowY, rowB, outR, outG, outB, n, p);

        for (int i = 0; i < n; i++)
        {
            Assert.InRange(outR[i], -0.01f, 0.01f);
            Assert.InRange(outG[i], -0.01f, 0.01f);
            Assert.InRange(outB[i], -0.01f, 0.01f);
        }
    }

    [Fact]
    public void XybToSrgb_BlackPixel_IsNearZero()
    {
        // Black in XYB = (0, 0, 0)
        var p = DefaultParams();

        XybConverter.XybToSrgb(0, 0, 0, p,
            out float sR, out float sG, out float sB);

        Assert.InRange(sR, -0.02f, 0.02f);
        Assert.InRange(sG, -0.02f, 0.02f);
        Assert.InRange(sB, -0.02f, 0.02f);
    }

    [Fact]
    public void LinearToSrgb8Row_ClampsValues()
    {
        float[] r = { -0.1f, 0.0f, 0.5f, 1.5f };
        float[] g = { 0.0f, 0.0f, 0.5f, 1.0f };
        float[] b = { 0.0f, 0.0f, 0.5f, 0.0f };
        byte[] output = new byte[4 * 3];

        XybConverter.LinearToSrgb8Row(r, g, b, output, 4, false);

        // Check clamping: negative → 0, >1.0 → 255
        Assert.Equal(0, output[0]); // r[0] clamped
        Assert.Equal(255, output[9]); // r[3] clamped
    }

    [Fact]
    public void LinearToSrgb8Row_RGBA_IncludesAlpha()
    {
        float[] r = { 0.5f };
        float[] g = { 0.5f };
        float[] b = { 0.5f };
        float[] a = { 0.75f };
        byte[] output = new byte[4];

        XybConverter.LinearToSrgb8Row(r, g, b, output, 1, true, a);

        // Alpha should be ~191 (0.75 * 255)
        Assert.InRange(output[3], (byte)190, (byte)192);
    }
}
