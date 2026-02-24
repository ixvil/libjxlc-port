using LibJxl.Bitstream;
using LibJxl.Entropy;
using Xunit;

namespace LibJxl.Tests.Entropy;

public class HybridUintConfigTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(7u)]
    [InlineData(15u)]
    public void SmallValues_TokenEqualsValue(uint value)
    {
        var config = new HybridUintConfig(4, 2, 0);
        config.Encode(value, out uint token, out uint nbits, out uint bits);
        Assert.Equal(value, token);
        Assert.Equal(0u, nbits);
        Assert.Equal(0u, bits);
    }

    [Fact]
    public void Encode_ValueAboveSplitToken()
    {
        var config = new HybridUintConfig(4, 2, 0);
        // N = 16 (10000): token=16, nbits=2, bits='00'
        config.Encode(16, out uint token, out uint nbits, out uint bits);
        Assert.Equal(16u, token);
        Assert.Equal(2u, nbits);
        Assert.Equal(0u, bits);
    }

    [Fact]
    public void Encode_ValueAboveSplitToken_17()
    {
        var config = new HybridUintConfig(4, 2, 0);
        // N = 17 (10001): token=16, nbits=2, bits='01'
        config.Encode(17, out uint token, out uint nbits, out uint bits);
        Assert.Equal(16u, token);
        Assert.Equal(2u, nbits);
        Assert.Equal(1u, bits);
    }

    [Fact]
    public void Encode_Decode_Roundtrip()
    {
        var config = new HybridUintConfig(4, 2, 0);

        for (uint value = 0; value < 256; value++)
        {
            config.Encode(value, out uint token, out uint nbits, out uint bits);

            // Build a bitstream with the extra bits
            var writer = new BitWriter();
            writer.Write((int)nbits, bits);
            writer.ZeroPadToByte();
            var data = writer.GetSpan();

            using var reader = new BitReader(data.ToArray());
            uint decoded = HybridUintConfig.DecodeHybridUint(in config, token, reader);
            Assert.Equal(value, decoded);
            reader.Close();
        }
    }

    [Fact]
    public void ConfigWithLsb_Roundtrip()
    {
        var config = new HybridUintConfig(4, 1, 1);

        for (uint value = 0; value < 128; value++)
        {
            config.Encode(value, out uint token, out uint nbits, out uint bits);

            var writer = new BitWriter();
            writer.Write((int)nbits, bits);
            writer.ZeroPadToByte();
            var data = writer.GetSpan();

            using var reader = new BitReader(data.ToArray());
            uint decoded = HybridUintConfig.DecodeHybridUint(in config, token, reader);
            Assert.Equal(value, decoded);
            reader.Close();
        }
    }

    [Fact]
    public void ZeroSplitExponent_Roundtrip()
    {
        var config = new HybridUintConfig(0, 0, 0);

        for (uint value = 0; value < 64; value++)
        {
            config.Encode(value, out uint token, out uint nbits, out uint bits);

            var writer = new BitWriter();
            writer.Write((int)nbits, bits);
            writer.ZeroPadToByte();
            var data = writer.GetSpan();

            using var reader = new BitReader(data.ToArray());
            uint decoded = HybridUintConfig.DecodeHybridUint(in config, token, reader);
            Assert.Equal(value, decoded);
            reader.Close();
        }
    }
}

public class InverseMtfTests
{
    [Fact]
    public void Identity_WhenAllZeros()
    {
        byte[] data = [0, 0, 0, 0, 0];
        InverseMtf.InverseMoveToFrontTransform(data, data.Length);
        // All zeros: always picks index 0, which starts as 0
        for (int i = 0; i < data.Length; i++)
            Assert.Equal(0, data[i]);
    }

    [Fact]
    public void SimpleTransform()
    {
        // Input: [0, 1, 2, 3] means:
        // Step 0: index=0 -> value=0 (mtf = [0,1,2,3,...])
        // Step 1: index=1 -> value=1 (mtf = [0,1,2,3,...])
        // Step 2: index=2 -> value=2 (mtf = [1,0,2,3,...])  after step 1 moved 1 to front
        // Actually let me trace through more carefully
        byte[] data = [0, 0, 0, 0];
        InverseMtf.InverseMoveToFrontTransform(data, data.Length);
        // All zeros -> always returns mtf[0] which is always 0
        Assert.Equal(0, data[0]);
        Assert.Equal(0, data[1]);
        Assert.Equal(0, data[2]);
        Assert.Equal(0, data[3]);
    }

    [Fact]
    public void DifferentIndices()
    {
        // Start: mtf = [0, 1, 2, 3, ...]
        // index=1 -> value=1, move 1 to front -> mtf = [1, 0, 2, 3, ...]
        // index=0 -> value=1 (mtf[0]=1)
        // index=2 -> value=2 (mtf = [1, 0, 2, ...]), mtf[2]=2, move to front -> [2, 1, 0, 3, ...]
        byte[] data = [1, 0, 2];
        InverseMtf.InverseMoveToFrontTransform(data, data.Length);
        Assert.Equal(1, data[0]);
        Assert.Equal(1, data[1]);
        Assert.Equal(2, data[2]);
    }

    [Fact]
    public void Sequential_ProducesIdentity()
    {
        // If input is sequential indices [0, 1, 2, 3, ...], the output should be the identity
        byte[] data = [0, 1, 2, 3, 4, 5];
        InverseMtf.InverseMoveToFrontTransform(data, data.Length);
        for (int i = 0; i < data.Length; i++)
            Assert.Equal(i, data[i]);
    }
}

public class FieldReaderTests
{
    [Fact]
    public void ReadU32_DirectValue()
    {
        // Selector = 0 (2 bits: 00), Val(42) -> should return 42
        var writer = new BitWriter();
        writer.Write(2, 0); // selector 0
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        uint result = FieldReader.ReadU32(reader,
            U32Distr.Val(42), U32Distr.Val(0), U32Distr.Val(0), U32Distr.Val(0));
        Assert.Equal(42u, result);
        reader.Close();
    }

    [Fact]
    public void ReadU32_BitsOffset()
    {
        // Selector = 3 (2 bits: 11), BitsOffset(4, 10) -> read 4 bits + 10
        var writer = new BitWriter();
        writer.Write(2, 3); // selector 3
        writer.Write(4, 5); // 4 bits = 5
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        uint result = FieldReader.ReadU32(reader,
            U32Distr.Val(0), U32Distr.Val(0), U32Distr.Val(0), U32Distr.BitsOffset(4, 10));
        Assert.Equal(15u, result); // 5 + 10
        reader.Close();
    }

    [Fact]
    public void ReadBool_True()
    {
        var writer = new BitWriter();
        writer.Write(1, 1);
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        Assert.True(FieldReader.ReadBool(reader));
        reader.Close();
    }

    [Fact]
    public void ReadBool_False()
    {
        var writer = new BitWriter();
        writer.Write(1, 0);
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        Assert.False(FieldReader.ReadBool(reader));
        reader.Close();
    }
}

public class VarLenUintTests
{
    [Fact]
    public void DecodeVarLenUint8_Zero()
    {
        // First bit = 0 -> return 0
        var writer = new BitWriter();
        writer.Write(1, 0);
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        int result = HistogramDecoder.DecodeVarLenUint8(reader);
        Assert.Equal(0, result);
        reader.Close();
    }

    [Fact]
    public void DecodeVarLenUint8_One()
    {
        // First bit = 1, nbits = 0 -> return 1
        var writer = new BitWriter();
        writer.Write(1, 1); // flag
        writer.Write(3, 0); // nbits = 0
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        int result = HistogramDecoder.DecodeVarLenUint8(reader);
        Assert.Equal(1, result);
        reader.Close();
    }

    [Fact]
    public void DecodeVarLenUint8_SmallValues()
    {
        // First bit = 1, nbits = 1, value = 0 -> 1 << 1 + 0 = 2
        var writer = new BitWriter();
        writer.Write(1, 1); // flag
        writer.Write(3, 1); // nbits = 1
        writer.Write(1, 0); // value = 0
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        int result = HistogramDecoder.DecodeVarLenUint8(reader);
        Assert.Equal(2, result);
        reader.Close();
    }

    [Fact]
    public void DecodeVarLenUint8_ThreeValue()
    {
        // First bit = 1, nbits = 1, value = 1 -> 1 << 1 + 1 = 3
        var writer = new BitWriter();
        writer.Write(1, 1); // flag
        writer.Write(3, 1); // nbits = 1
        writer.Write(1, 1); // value = 1
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        int result = HistogramDecoder.DecodeVarLenUint8(reader);
        Assert.Equal(3, result);
        reader.Close();
    }

    [Fact]
    public void DecodeVarLenUint16_Zero()
    {
        var writer = new BitWriter();
        writer.Write(1, 0);
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        int result = HistogramDecoder.DecodeVarLenUint16(reader);
        Assert.Equal(0, result);
        reader.Close();
    }

    [Fact]
    public void DecodeVarLenUint16_One()
    {
        var writer = new BitWriter();
        writer.Write(1, 1); // flag
        writer.Write(4, 0); // nbits = 0
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        int result = HistogramDecoder.DecodeVarLenUint16(reader);
        Assert.Equal(1, result);
        reader.Close();
    }
}

public class HistogramDecoderTests
{
    [Fact]
    public void ReadHistogram_SimpleCode_SingleSymbol()
    {
        // simple_code=1, num_symbols=1 (bit=0), symbol via VarLenUint8 = 0
        var writer = new BitWriter();
        writer.Write(1, 1); // simple_code = 1
        writer.Write(1, 0); // num_symbols = 1
        // Symbol 0: VarLenUint8 = 0 (first bit = 0)
        writer.Write(1, 0);
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        var status = HistogramDecoder.ReadHistogram(AnsParams.AnsLogTabSize, out var counts, reader);
        Assert.True(status);
        Assert.Single(counts);
        Assert.Equal(AnsParams.AnsTabSize, counts[0]);
        reader.Close();
    }

    [Fact]
    public void ReadHistogram_SimpleCode_TwoSymbols()
    {
        // simple_code=1, num_symbols=2 (bit=1), symbols 0 and 1, counts split
        var writer = new BitWriter();
        writer.Write(1, 1); // simple_code = 1
        writer.Write(1, 1); // num_symbols = 2
        // Symbol 0: VarLenUint8 = 0 (first bit = 0)
        writer.Write(1, 0);
        // Symbol 1: VarLenUint8 = 1 (first bit = 1, nbits = 0)
        writer.Write(1, 1);
        writer.Write(3, 0);
        // counts[0] = precision_bits bits
        writer.Write(AnsParams.AnsLogTabSize, 2048); // half
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        var status = HistogramDecoder.ReadHistogram(AnsParams.AnsLogTabSize, out var counts, reader);
        Assert.True(status);
        Assert.Equal(2, counts.Length);
        Assert.Equal(2048, counts[0]);
        Assert.Equal(2048, counts[1]);
        reader.Close();
    }

    [Fact]
    public void ReadHistogram_FlatHistogram()
    {
        // simple_code=0, is_flat=1, alphabet_size via VarLenUint8
        var writer = new BitWriter();
        writer.Write(1, 0); // not simple
        writer.Write(1, 1); // is_flat
        // alphabet_size = 4 -> VarLenUint8(3): first bit=1, nbits=1, value=1 -> 2+1=3; +1=4
        writer.Write(1, 1); // flag
        writer.Write(3, 1); // nbits = 1
        writer.Write(1, 1); // value = 1
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        var status = HistogramDecoder.ReadHistogram(AnsParams.AnsLogTabSize, out var counts, reader);
        Assert.True(status);
        Assert.Equal(4, counts.Length);
        int sum = counts.Sum();
        Assert.Equal(AnsParams.AnsTabSize, sum);
        reader.Close();
    }
}

public class LZ77ParamsTests
{
    [Fact]
    public void ReadDisabled()
    {
        var writer = new BitWriter();
        writer.Write(1, 0); // enabled = false
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        var lz77 = new LZ77Params();
        Assert.True(lz77.ReadFromBitStream(reader));
        Assert.False(lz77.Enabled);
        reader.Close();
    }

    [Fact]
    public void ReadEnabled_DefaultValues()
    {
        var writer = new BitWriter();
        writer.Write(1, 1); // enabled = true
        writer.Write(2, 0); // min_symbol selector 0 -> Val(224)
        writer.Write(2, 0); // min_length selector 0 -> Val(3)
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data.ToArray());
        var lz77 = new LZ77Params();
        Assert.True(lz77.ReadFromBitStream(reader));
        Assert.True(lz77.Enabled);
        Assert.Equal(224u, lz77.MinSymbol);
        Assert.Equal(3u, lz77.MinLength);
        reader.Close();
    }
}

public class ANSCodeTests
{
    [Fact]
    public void UpdateMaxNumBits_SmallSymbol()
    {
        var code = new ANSCode();
        code.UintConfig = [new HybridUintConfig(4, 2, 0)];
        code.UpdateMaxNumBits(0, 5); // 5 < 16 (split_token)
        Assert.True(code.MaxNumBits >= 4); // at least split_exponent
    }

    [Fact]
    public void UpdateMaxNumBits_LargeSymbol()
    {
        var code = new ANSCode();
        code.UintConfig = [new HybridUintConfig(4, 2, 0)];
        code.UpdateMaxNumBits(0, 20); // 20 >= 16 (split_token)
        Assert.True(code.MaxNumBits > 4);
    }
}
