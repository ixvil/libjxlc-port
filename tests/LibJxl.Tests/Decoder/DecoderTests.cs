using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Decoder;
using LibJxl.Entropy;
using LibJxl.Fields;
using Xunit;

namespace LibJxl.Tests.Decoder;

public class FrameDimensionsTests
{
    [Fact]
    public void Set_SmallVarDCT()
    {
        var fd = new FrameDimensions();
        fd.Set(64, 64, 1, 0, 0, false, 1);
        Assert.Equal(64, fd.XSize);
        Assert.Equal(64, fd.YSize);
        Assert.Equal(8, fd.XSizeBlocks); // 64/8
        Assert.Equal(8, fd.YSizeBlocks);
        Assert.Equal(64, fd.XSizePadded);
        Assert.Equal(64, fd.YSizePadded);
        Assert.Equal(1, fd.NumGroups);
        Assert.Equal(1, fd.NumDcGroups);
    }

    [Fact]
    public void Set_LargeImage()
    {
        var fd = new FrameDimensions();
        fd.Set(1920, 1080, 1, 0, 0, false, 1);
        Assert.Equal(1920, fd.XSize);
        Assert.Equal(1080, fd.YSize);
        Assert.Equal(240, fd.XSizeBlocks); // 1920/8
        Assert.Equal(135, fd.YSizeBlocks); // 1080/8 = 135
        // group_dim = (256/2) << 1 = 256
        Assert.Equal(256, fd.GroupDimValue);
        Assert.Equal(8, fd.XSizeGroups); // ceil(1920/256) = 8
        Assert.Equal(5, fd.YSizeGroups); // ceil(1080/256) = 5 (but let's check: 1080/256=4.2, so ceil=5)
        Assert.Equal(40, fd.NumGroups);
    }

    [Fact]
    public void Set_Modular_NoPadding()
    {
        var fd = new FrameDimensions();
        fd.Set(100, 50, 1, 0, 0, true, 1);
        Assert.Equal(100, fd.XSize);
        Assert.Equal(50, fd.YSize);
        // Modular mode: no block-level padding
        Assert.Equal(100, fd.XSizePadded);
        Assert.Equal(50, fd.YSizePadded);
    }

    [Fact]
    public void Set_WithUpsampling()
    {
        var fd = new FrameDimensions();
        fd.Set(512, 512, 1, 0, 0, false, 2);
        // XSize = 512/2 = 256
        Assert.Equal(256, fd.XSize);
        Assert.Equal(256, fd.YSize);
        Assert.Equal(512, fd.XSizeUpsampled);
        Assert.Equal(512, fd.YSizeUpsampled);
    }

    [Fact]
    public void GroupRect_SingleGroup()
    {
        var fd = new FrameDimensions();
        fd.Set(64, 64, 1, 0, 0, false, 1);
        var rect = fd.GroupRect(0);
        Assert.Equal(0, rect.X0);
        Assert.Equal(0, rect.Y0);
    }
}

public class LehmerCodeTests
{
    [Fact]
    public void Identity_Permutation()
    {
        // Lehmer code [0,0,0,0] = identity permutation
        uint[] code = [0, 0, 0, 0];
        int[] perm = new int[4];
        var status = LehmerCode.DecodeLehmerCode(code, 4, perm);
        Assert.True(status);
        for (int i = 0; i < 4; i++)
            Assert.Equal(i, perm[i]);
    }

    [Fact]
    public void Reverse_Permutation()
    {
        // Lehmer code [3,2,1,0] = reverse permutation [3,2,1,0]
        uint[] code = [3, 2, 1, 0];
        int[] perm = new int[4];
        var status = LehmerCode.DecodeLehmerCode(code, 4, perm);
        Assert.True(status);
        Assert.Equal(3, perm[0]);
        Assert.Equal(2, perm[1]);
        Assert.Equal(1, perm[2]);
        Assert.Equal(0, perm[3]);
    }

    [Fact]
    public void Swap_Permutation()
    {
        // Lehmer code [1,0,0] = swap first two: [1,0,2]
        uint[] code = [1, 0, 0];
        int[] perm = new int[3];
        var status = LehmerCode.DecodeLehmerCode(code, 3, perm);
        Assert.True(status);
        Assert.Equal(1, perm[0]);
        Assert.Equal(0, perm[1]);
        Assert.Equal(2, perm[2]);
    }

    [Fact]
    public void Invalid_Code_Fails()
    {
        // Code[0] + 0 must be < n, so code[0]=4 for n=4 is invalid
        uint[] code = [4, 0, 0, 0];
        int[] perm = new int[4];
        var status = LehmerCode.DecodeLehmerCode(code, 4, perm);
        Assert.False(status);
    }

    [Fact]
    public void SingleElement()
    {
        uint[] code = [0];
        int[] perm = new int[1];
        var status = LehmerCode.DecodeLehmerCode(code, 1, perm);
        Assert.True(status);
        Assert.Equal(0, perm[0]);
    }
}

public class TocReaderTests
{
    [Fact]
    public void NumTocEntries_SingleGroupSinglePass()
    {
        Assert.Equal(1, TocReader.NumTocEntries(1, 1, 1));
    }

    [Fact]
    public void NumTocEntries_MultiGroup()
    {
        // num_groups=4, num_dc_groups=1, num_passes=1
        // = 2 + 1 + 0*4 + 4*1 = 7 entries
        // DC global(1) + DC groups(1) + AC global(1) + AC groups(4) = 7
        Assert.Equal(7, TocReader.NumTocEntries(4, 1, 1));
    }

    [Fact]
    public void NumTocEntries_MultiPass()
    {
        // num_groups=2, num_dc_groups=1, num_passes=3
        // = 2 + 1 + 2*3 = 9 entries
        Assert.Equal(9, TocReader.NumTocEntries(2, 1, 3));
    }

    [Fact]
    public void AcGroupIndex_Basic()
    {
        // pass=0, group=0, num_groups=4, num_dc_groups=1
        // = 2 + 1 + 0*4 + 0 = 3
        Assert.Equal(3, TocReader.AcGroupIndex(0, 0, 4, 1));
    }

    [Fact]
    public void ReadToc_SimpleNoPermutation()
    {
        // Build a TOC with 1 entry, no permutation
        var writer = new BitWriter();
        writer.Write(1, 0); // no permutation
        writer.ZeroPadToByte();
        // Entry 0: kTocDist selector 0 = Bits(10), value = 42
        writer.Write(2, 0); // selector 0
        writer.Write(10, 42); // 42 bytes
        writer.ZeroPadToByte();

        var data = writer.GetSpan().ToArray();
        using var reader = new BitReader(data);
        var status = TocReader.ReadToc(1, reader, out uint[] sizes, out int[]? perm);
        Assert.True(status);
        Assert.Null(perm);
        Assert.Single(sizes);
        Assert.Equal(42u, sizes[0]);
        reader.Close();
    }

    [Fact]
    public void ReadToc_MultipleEntries()
    {
        var writer = new BitWriter();
        writer.Write(1, 0); // no permutation
        writer.ZeroPadToByte();
        // Entry 0: selector 0, Bits(10) = 100
        writer.Write(2, 0);
        writer.Write(10, 100);
        // Entry 1: selector 0, Bits(10) = 200
        writer.Write(2, 0);
        writer.Write(10, 200);
        // Entry 2: selector 1, BitsOffset(14, 1024) = 1024 (bits=0)
        writer.Write(2, 1);
        writer.Write(14, 0); // 0+1024=1024
        writer.ZeroPadToByte();

        var data = writer.GetSpan().ToArray();
        using var reader = new BitReader(data);
        var status = TocReader.ReadToc(3, reader, out uint[] sizes, out int[]? perm);
        Assert.True(status);
        Assert.Null(perm);
        Assert.Equal(3, sizes.Length);
        Assert.Equal(100u, sizes[0]);
        Assert.Equal(200u, sizes[1]);
        Assert.Equal(1024u, sizes[2]);
        reader.Close();
    }
}

public class FrameDecoderTests
{
    [Fact]
    public void InitFrame_AllDefault_SingleSection()
    {
        var meta = new ImageMetadata();
        var size = new SizeHeader();

        // Build a minimal codestream: small image, all-default frame header, simple TOC
        var writer = new BitWriter();

        // Frame header: all_default = true
        writer.Write(1, 1);
        writer.ZeroPadToByte();

        // For all-default frame header: single group, single pass → 1 TOC entry
        // TOC: no permutation bit
        writer.Write(1, 0);
        writer.ZeroPadToByte();
        // TOC entry: selector 0, Bits(10) = 10 bytes
        writer.Write(2, 0);
        writer.Write(10, 10);
        writer.ZeroPadToByte();

        // Small image: set size manually on SizeHeader
        // Since all_default FrameHeader has 1 group by default, we need xsize/ysize small enough
        // Use direct setting
        size.ReadFromBitStream(BuildSmallSizeReader());

        var data = writer.GetSpan().ToArray();
        using var reader = new BitReader(data);

        var decoder = new FrameDecoder(meta, size);
        var status = decoder.InitFrame(reader);
        Assert.True(status);
        Assert.Equal(FrameType.RegularFrame, decoder.Header.Type);
        Assert.Equal(FrameEncoding.VarDCT, decoder.Header.Encoding);
        Assert.True(decoder.Header.IsLast);
        reader.Close();
    }

    private static BitReader BuildSmallSizeReader()
    {
        var w = new BitWriter();
        w.Write(1, 1); // small
        w.Write(5, 0); // ysize_div8_minus1 = 0 -> y=8
        w.Write(3, 1); // ratio 1:1 -> x=8
        w.ZeroPadToByte();
        return new BitReader(w.GetSpan().ToArray());
    }
}

public class JxlDecoderTests
{
    [Fact]
    public void ReadBasicInfo_InvalidSignature()
    {
        var decoder = new JxlDecoder();
        var status = decoder.ReadBasicInfo([0x00, 0x00, 0x00, 0x00]);
        Assert.False(status);
    }

    [Fact]
    public void ReadBasicInfo_ValidSignature_SmallImage()
    {
        var writer = new BitWriter();
        // JXL signature
        writer.Write(8, 0xFF);
        writer.Write(8, 0x0A);
        // SizeHeader: small=1, ysize_div8_minus1=0 -> y=8, ratio=1 (1:1)
        writer.Write(1, 1); // small
        writer.Write(5, 0); // ysize_div8_minus1 = 0 -> y=8
        writer.Write(3, 1); // ratio = 1 (1:1)
        // ImageMetadata: all_default=1
        writer.Write(1, 1);
        writer.ZeroPadToByte();

        var data = writer.GetSpan().ToArray();
        var decoder = new JxlDecoder();
        var status = decoder.ReadBasicInfo(data);
        Assert.True(status);
        Assert.Equal(8, decoder.Width);
        Assert.Equal(8, decoder.Height);
        Assert.Equal(DecoderStage.Started, decoder.Stage);
    }

    [Fact]
    public void ReadBasicInfo_TooShort()
    {
        var decoder = new JxlDecoder();
        var status = decoder.ReadBasicInfo([0xFF]);
        Assert.False(status);
    }
}
