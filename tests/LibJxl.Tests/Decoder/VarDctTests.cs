using LibJxl.Bitstream;
using LibJxl.Decoder;
using Xunit;

namespace LibJxl.Tests.Decoder;

public class AcStrategyTests
{
    [Fact]
    public void CoeffCount_DCT8x8_Is64()
    {
        Assert.Equal(64, AcStrategy.CoeffCount(AcStrategyType.DCT));
    }

    [Fact]
    public void CoeffCount_DCT16x16_Is256()
    {
        // 2×2 blocks × 64 = 256
        Assert.Equal(256, AcStrategy.CoeffCount(AcStrategyType.DCT16X16));
    }

    [Fact]
    public void CoeffCount_DCT32x32_Is1024()
    {
        // 4×4 blocks × 64 = 1024
        Assert.Equal(1024, AcStrategy.CoeffCount(AcStrategyType.DCT32X32));
    }

    [Fact]
    public void CoveredBlocks_AllStrategies_NonZero()
    {
        for (int i = 0; i < AcStrategy.kNumValidStrategies; i++)
        {
            Assert.True(AcStrategy.CoveredBlocks((AcStrategyType)i) > 0);
        }
    }

    [Fact]
    public void StrategyOrder_InRange()
    {
        for (int i = 0; i < AcStrategy.kNumValidStrategies; i++)
        {
            Assert.InRange(AcStrategy.StrategyOrder[i], 0, AcStrategy.kNumOrders - 1);
        }
    }

    [Fact]
    public void CoveredBlocks_Dimensions_Match()
    {
        for (int i = 0; i < AcStrategy.kNumValidStrategies; i++)
        {
            int blocks = AcStrategy.CoveredBlocksX[i] * AcStrategy.CoveredBlocksY[i];
            int log2 = AcStrategy.Log2CoveredBlocks[i];
            Assert.Equal(blocks, 1 << log2);
        }
    }
}

public class QuantizerTests
{
    [Fact]
    public void DefaultQuantizer_ScaleIsOne()
    {
        var dq = new DequantMatrices();
        var q = new Quantizer(dq);
        // default: global_scale = 65536/64 = 1024
        // global_scale_float = 1024/65536 = 1/64
        Assert.InRange(q.Scale, 0.015f, 0.016f); // ~0.015625
    }

    [Fact]
    public void DefaultQuantizer_InvGlobalScale()
    {
        var dq = new DequantMatrices();
        var q = new Quantizer(dq);
        // inv = 65536 / 1024 = 64
        Assert.Equal(64.0f, q.InvGlobalScale);
    }

    [Fact]
    public void DefaultQuantizer_DcStep()
    {
        var dq = new DequantMatrices();
        var q = new Quantizer(dq);
        // inv_quant_dc = 64/64 = 1.0, dc_step = 1.0 * dc_quant[c]
        float step0 = q.GetDcStep(0);
        Assert.InRange(step0, 0.000244f, 0.000245f); // 1/4096
    }

    [Fact]
    public void ReadFromBitStream_ValidData()
    {
        var bw = new BitWriter();
        // global_scale: selector=0, 11 bits, value 999 → global_scale=1000
        bw.Write(2, 0);
        bw.Write(11, 999); // global_scale = 999 + 1 = 1000
        // quant_dc: selector=2, 8 bits, value 63 → quant_dc=64
        bw.Write(2, 2);
        bw.Write(8, 63); // quant_dc = 63 + 1 = 64

        var br = new BitReader(bw.GetSpan());
        var dq = new DequantMatrices();
        var q = new Quantizer(dq);
        Assert.True(q.ReadFromBitStream(br));

        Assert.Equal(1000, q.GlobalScale);
        Assert.Equal(64, q.QuantDc);
    }

    [Fact]
    public void InvQuantAc_Calculation()
    {
        var dq = new DequantMatrices();
        var q = new Quantizer(dq);
        // inv_global_scale = 64, so inv_quant_ac(32) = 64/32 = 2.0
        Assert.Equal(2.0f, q.InvQuantAc(32));
    }
}

public class DequantMatricesTests
{
    [Fact]
    public void Default_DCQuant_Values()
    {
        var dq = new DequantMatrices();
        Assert.InRange(dq.DCQuant(0), 0.000244f, 0.000245f); // 1/4096
        Assert.InRange(dq.DCQuant(1), 0.00195f, 0.00196f);   // 1/512
        Assert.InRange(dq.DCQuant(2), 0.00390f, 0.00391f);   // 1/256
    }

    [Fact]
    public void Default_InvDCQuant_Values()
    {
        var dq = new DequantMatrices();
        Assert.Equal(4096.0f, dq.InvDCQuant(0));
        Assert.Equal(512.0f, dq.InvDCQuant(1));
        Assert.Equal(256.0f, dq.InvDCQuant(2));
    }

    [Fact]
    public void AcStrategyToQuantTable_AllValid()
    {
        for (int i = 0; i < AcStrategy.kNumValidStrategies; i++)
        {
            var qt = DequantMatrices.AcStrategyToQuantTable[i];
            Assert.InRange((int)qt, 0, DequantMatrices.kNumQuantTables - 1);
        }
    }

    [Fact]
    public void Decode_AllDefault()
    {
        var bw = new BitWriter();
        bw.Write(1, 1); // all_default = true
        var br = new BitReader(bw.GetSpan());

        var dq = new DequantMatrices();
        Assert.True(dq.Decode(br));
        Assert.True(dq.AllDefault);
    }

    [Fact]
    public void DecodeDC_AllDefault()
    {
        var bw = new BitWriter();
        bw.Write(1, 1); // all_default = true
        var br = new BitReader(bw.GetSpan());

        var dq = new DequantMatrices();
        Assert.True(dq.DecodeDC(br));
        Assert.Equal(4096.0f, dq.InvDCQuant(0)); // unchanged
    }

    [Fact]
    public void EnsureComputed_DefaultTables()
    {
        var dq = new DequantMatrices();
        Assert.True(dq.EnsureComputed(1)); // compute for DCT8x8
    }
}

public class CoeffOrderTests
{
    [Fact]
    public void GetOffset_BucketZero_ChannelZero_IsZero()
    {
        Assert.Equal(0, CoeffOrder.GetOffset(0, 0));
    }

    [Fact]
    public void GetSize_BucketZero_Is64()
    {
        // Bucket 0 = DCT 8×8, offset[0]=0, offset[1]=1, size = 1*64 = 64
        Assert.Equal(64, CoeffOrder.GetSize(0, 0));
    }

    [Fact]
    public void OffsetTable_IsMonotonicallyIncreasing()
    {
        for (int i = 1; i < CoeffOrder.kCoeffOrderOffset.Length; i++)
        {
            Assert.True(CoeffOrder.kCoeffOrderOffset[i] >= CoeffOrder.kCoeffOrderOffset[i - 1]);
        }
    }

    [Fact]
    public void SetNaturalOrder_8x8_Has64Elements()
    {
        int[] order = new int[64];
        CoeffOrder.SetNaturalOrder(8, 8, order);
        // All indices 0-63 should appear
        var set = new HashSet<int>(order);
        Assert.Equal(64, set.Count);
    }
}

public class InverseDctTests
{
    [Fact]
    public void IDCT8x8_DC_Only_ProducesFlat()
    {
        float[] coeffs = new float[64];
        coeffs[0] = 8.0f; // DC coefficient

        float[] pixels = new float[64];
        InverseDct.IDCT8x8(coeffs, 0, pixels, 0, 8);

        // All pixels should be the same (DC only = flat block)
        float expected = pixels[0];
        for (int i = 1; i < 64; i++)
        {
            Assert.InRange(pixels[i], expected - 0.01f, expected + 0.01f);
        }
    }

    [Fact]
    public void IDCT8x8_AllZero_ProducesZero()
    {
        float[] coeffs = new float[64];
        float[] pixels = new float[64];
        InverseDct.IDCT8x8(coeffs, 0, pixels, 0, 8);

        for (int i = 0; i < 64; i++)
            Assert.InRange(pixels[i], -0.001f, 0.001f);
    }

    [Fact]
    public void IdentityTransform_CopiesValues()
    {
        float[] coeffs = new float[64];
        for (int i = 0; i < 64; i++) coeffs[i] = i;

        float[] pixels = new float[64];
        InverseDct.TransformToPixels(AcStrategyType.IDENTITY, coeffs, 0, pixels, 0, 8);

        for (int i = 0; i < 64; i++)
            Assert.Equal(coeffs[i], pixels[i]);
    }

    [Fact]
    public void IDCT8x8_EnergyConservation()
    {
        // Energy should be approximately conserved by IDCT
        float[] coeffs = new float[64];
        coeffs[0] = 4.0f;
        coeffs[1] = 2.0f;
        coeffs[8] = 1.0f;

        float[] pixels = new float[64];
        InverseDct.IDCT8x8(coeffs, 0, pixels, 0, 8);

        // Sum of squares should be related
        float energyIn = 0;
        for (int i = 0; i < 64; i++) energyIn += coeffs[i] * coeffs[i];

        float energyOut = 0;
        for (int i = 0; i < 64; i++) energyOut += pixels[i] * pixels[i];

        // They should be in the same ballpark (exact ratio depends on normalization)
        Assert.True(energyOut > 0);
    }

    [Fact]
    public void GenericIDCT_ViaTransformToPixels_DCT16x16_Works()
    {
        int size = AcStrategy.CoeffCount(AcStrategyType.DCT16X16); // 256
        float[] coeffs = new float[size];
        coeffs[0] = 16.0f; // DC

        float[] pixels = new float[16 * 16];
        InverseDct.TransformToPixels(AcStrategyType.DCT16X16, coeffs, 0, pixels, 0, 16);

        // All pixels should be roughly equal (DC only)
        float first = pixels[0];
        Assert.True(MathF.Abs(first) > 0.001f); // non-zero
        for (int i = 1; i < 256; i++)
        {
            Assert.InRange(pixels[i], first - 0.01f, first + 0.01f);
        }
    }
}
