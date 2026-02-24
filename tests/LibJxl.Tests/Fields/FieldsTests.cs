using LibJxl.Bitstream;
using LibJxl.Entropy;
using LibJxl.Fields;
using Xunit;

namespace LibJxl.Tests.Fields;

public class U64CoderTests
{
    [Fact]
    public void Read_Zero()
    {
        // selector=0 (00) -> 0
        var writer = new BitWriter();
        writer.Write(2, 0);
        writer.ZeroPadToByte();
        using var reader = new BitReader(writer.GetSpan().ToArray());
        Assert.Equal(0UL, U64Coder.Read(reader));
        reader.Close();
    }

    [Fact]
    public void Read_SmallValues()
    {
        // selector=1 (01), 4 bits = 7 -> 1+7 = 8
        var writer = new BitWriter();
        writer.Write(2, 1);
        writer.Write(4, 7);
        writer.ZeroPadToByte();
        using var reader = new BitReader(writer.GetSpan().ToArray());
        Assert.Equal(8UL, U64Coder.Read(reader));
        reader.Close();
    }

    [Fact]
    public void Read_MediumValues()
    {
        // selector=2 (10), 8 bits = 100 -> 17+100 = 117
        var writer = new BitWriter();
        writer.Write(2, 2);
        writer.Write(8, 100);
        writer.ZeroPadToByte();
        using var reader = new BitReader(writer.GetSpan().ToArray());
        Assert.Equal(117UL, U64Coder.Read(reader));
        reader.Close();
    }

    [Fact]
    public void Read_Selector3_SingleGroup()
    {
        // selector=3 (11), 12 bits = 42, then continuation bit = 0
        var writer = new BitWriter();
        writer.Write(2, 3);
        writer.Write(12, 42);
        writer.Write(1, 0); // no more groups
        writer.ZeroPadToByte();
        using var reader = new BitReader(writer.GetSpan().ToArray());
        Assert.Equal(42UL, U64Coder.Read(reader));
        reader.Close();
    }
}

public class F16CoderTests
{
    [Fact]
    public void Read_Zero()
    {
        // All 16 bits zero -> +0.0
        var writer = new BitWriter();
        writer.Write(16, 0);
        writer.ZeroPadToByte();
        using var reader = new BitReader(writer.GetSpan().ToArray());
        var status = F16Coder.Read(reader, out float value);
        Assert.True(status);
        Assert.Equal(0.0f, value);
        reader.Close();
    }

    [Fact]
    public void Read_One()
    {
        // 1.0 in half-precision: sign=0, exp=15, mantissa=0
        // bits = 0_01111_0000000000 = 0x3C00
        var writer = new BitWriter();
        writer.Write(16, 0x3C00);
        writer.ZeroPadToByte();
        using var reader = new BitReader(writer.GetSpan().ToArray());
        var status = F16Coder.Read(reader, out float value);
        Assert.True(status);
        Assert.Equal(1.0f, value);
        reader.Close();
    }

    [Fact]
    public void Read_NegativeOne()
    {
        // -1.0: sign=1, exp=15, mantissa=0
        // bits = 1_01111_0000000000 = 0xBC00
        var writer = new BitWriter();
        writer.Write(16, 0xBC00);
        writer.ZeroPadToByte();
        using var reader = new BitReader(writer.GetSpan().ToArray());
        var status = F16Coder.Read(reader, out float value);
        Assert.True(status);
        Assert.Equal(-1.0f, value);
        reader.Close();
    }

    [Fact]
    public void Read_Infinity_Fails()
    {
        // Infinity: exp=31, mantissa=0
        // bits = 0_11111_0000000000 = 0x7C00
        var writer = new BitWriter();
        writer.Write(16, 0x7C00);
        writer.ZeroPadToByte();
        using var reader = new BitReader(writer.GetSpan().ToArray());
        var status = F16Coder.Read(reader, out _);
        Assert.False(status);
        reader.Close();
    }

    [Fact]
    public void Read_Half()
    {
        // 0.5 in half-precision: sign=0, exp=14, mantissa=0
        // bits = 0_01110_0000000000 = 0x3800
        var writer = new BitWriter();
        writer.Write(16, 0x3800);
        writer.ZeroPadToByte();
        using var reader = new BitReader(writer.GetSpan().ToArray());
        var status = F16Coder.Read(reader, out float value);
        Assert.True(status);
        Assert.Equal(0.5f, value);
        reader.Close();
    }
}

public class SignedPackTests
{
    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 2u)]
    [InlineData(-1, 1u)]
    [InlineData(2, 4u)]
    [InlineData(-2, 3u)]
    [InlineData(100, 200u)]
    [InlineData(-100, 199u)]
    public void PackUnpack_Roundtrip(int value, uint packed)
    {
        Assert.Equal(packed, SignedPack.PackSigned(value));
        Assert.Equal(value, SignedPack.UnpackSigned(packed));
    }
}

public class SizeHeaderTests
{
    [Fact]
    public void Read_SmallSquare()
    {
        // small=1, ysize_div8_minus1=7 (64/8-1=7), ratio=1 (1:1)
        var writer = new BitWriter();
        writer.Write(1, 1); // small
        writer.Write(5, 7); // ysize_div8_minus1 = 7 -> y=64
        writer.Write(3, 1); // ratio = 1 (1:1)
        writer.ZeroPadToByte();

        using var reader = new BitReader(writer.GetSpan().ToArray());
        var header = new SizeHeader();
        var status = header.ReadFromBitStream(reader);
        Assert.True(status);
        Assert.Equal(64, header.YSize);
        Assert.Equal(64, header.XSize); // 1:1 ratio
        reader.Close();
    }

    [Fact]
    public void Read_SmallCustomRatio()
    {
        // small=1, ysize=16 (div8_minus1=1), ratio=0 (custom), xsize=32 (div8_minus1=3)
        var writer = new BitWriter();
        writer.Write(1, 1); // small
        writer.Write(5, 1); // ysize_div8_minus1 = 1 -> y=16
        writer.Write(3, 0); // ratio = 0 (custom)
        writer.Write(5, 3); // xsize_div8_minus1 = 3 -> x=32
        writer.ZeroPadToByte();

        using var reader = new BitReader(writer.GetSpan().ToArray());
        var header = new SizeHeader();
        var status = header.ReadFromBitStream(reader);
        Assert.True(status);
        Assert.Equal(16, header.YSize);
        Assert.Equal(32, header.XSize);
        reader.Close();
    }

    [Fact]
    public void Read_LargeImage()
    {
        // small=0, ysize via U32(BitsOffset(13,1) selector=1), ratio=0, xsize via U32
        var writer = new BitWriter();
        writer.Write(1, 0); // not small
        // U32: selector=1 -> BitsOffset(13, 1), value=1920 -> bits=1919
        writer.Write(2, 1);
        writer.Write(13, 1919); // ysize = 1920
        writer.Write(3, 0); // ratio = 0 (custom)
        // U32: selector=1 -> BitsOffset(13, 1), value=1080 -> bits=1079
        writer.Write(2, 1);
        writer.Write(13, 1079); // xsize = 1080
        writer.ZeroPadToByte();

        using var reader = new BitReader(writer.GetSpan().ToArray());
        var header = new SizeHeader();
        var status = header.ReadFromBitStream(reader);
        Assert.True(status);
        Assert.Equal(1920, header.YSize);
        Assert.Equal(1080, header.XSize);
        reader.Close();
    }
}

public class ImageMetadataTests
{
    [Fact]
    public void Read_AllDefault()
    {
        var writer = new BitWriter();
        writer.Write(1, 1); // all_default = true
        writer.ZeroPadToByte();

        using var reader = new BitReader(writer.GetSpan().ToArray());
        var meta = new ImageMetadata();
        var status = meta.ReadFromBitStream(reader);
        Assert.True(status);
        Assert.True(meta.AllDefault);
        Assert.Equal(1u, meta.Orientation);
        Assert.True(meta.XybEncoded);
        reader.Close();
    }
}

public class LoopFilterTests
{
    [Fact]
    public void Read_AllDefault()
    {
        var writer = new BitWriter();
        writer.Write(1, 1); // all_default = true
        writer.ZeroPadToByte();

        using var reader = new BitReader(writer.GetSpan().ToArray());
        var lf = new LoopFilter();
        var status = lf.ReadFromBitStream(reader);
        Assert.True(status);
        Assert.True(lf.AllDefault);
        Assert.True(lf.Gab);
        Assert.Equal(2u, lf.EpfIters);
        reader.Close();
    }

    [Fact]
    public void Read_NoGabNoEpf()
    {
        var writer = new BitWriter();
        writer.Write(1, 0); // not all default
        writer.Write(1, 0); // gab = false
        writer.Write(2, 0); // epf_iters = 0
        // Extensions = 0 (U64: selector=0)
        writer.Write(2, 0);
        writer.ZeroPadToByte();

        using var reader = new BitReader(writer.GetSpan().ToArray());
        var lf = new LoopFilter();
        var status = lf.ReadFromBitStream(reader);
        Assert.True(status);
        Assert.False(lf.AllDefault);
        Assert.False(lf.Gab);
        Assert.Equal(0u, lf.EpfIters);
        reader.Close();
    }
}

public class FrameHeaderTests
{
    [Fact]
    public void Read_AllDefault()
    {
        var writer = new BitWriter();
        writer.Write(1, 1); // all_default = true
        writer.ZeroPadToByte();

        var meta = new ImageMetadata();
        using var reader = new BitReader(writer.GetSpan().ToArray());
        var fh = new FrameHeader { NonserializedMetadata = meta };
        var status = fh.ReadFromBitStream(reader);
        Assert.True(status);
        Assert.Equal(FrameType.RegularFrame, fh.Type);
        Assert.Equal(FrameEncoding.VarDCT, fh.Encoding);
        Assert.True(fh.IsLast);
        reader.Close();
    }
}
