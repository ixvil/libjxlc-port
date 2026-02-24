using LibJxl.Bitstream;
using Xunit;

namespace LibJxl.Tests.Bitstream;

public class BitWriterTests
{
    [Fact]
    public void WriteSingleByte()
    {
        var writer = new BitWriter();
        writer.Write(8, 0xAB);
        var data = writer.GetSpan();
        Assert.Single(data);
        Assert.Equal(0xAB, data[0]);
    }

    [Fact]
    public void WriteMultipleSmallValues()
    {
        var writer = new BitWriter();
        writer.Write(3, 0b101);  // 5
        writer.Write(5, 0b11010); // 26
        var data = writer.GetSpan();
        // Combined: 11010_101 = 0b11010101 = 0xD5
        Assert.Single(data);
        Assert.Equal(0xD5, data[0]);
    }

    [Fact]
    public void Write16Bits()
    {
        var writer = new BitWriter();
        writer.Write(16, 0x1234);
        var data = writer.GetSpan();
        Assert.Equal(2, data.Length);
        // Little-endian byte order from the bit buffer
        Assert.Equal(0x34, data[0]);
        Assert.Equal(0x12, data[1]);
    }

    [Fact]
    public void ZeroPadToByte()
    {
        var writer = new BitWriter();
        writer.Write(5, 0b10101);
        writer.ZeroPadToByte();
        var data = writer.GetSpan();
        Assert.Single(data);
        Assert.Equal(0b00010101, data[0]); // 5 bits + 3 zero bits
    }

    [Fact]
    public void BitsWritten_TracksCorrectly()
    {
        var writer = new BitWriter();
        Assert.Equal(0, writer.BitsWritten);
        writer.Write(7, 0);
        Assert.Equal(7, writer.BitsWritten);
        writer.Write(13, 0);
        Assert.Equal(20, writer.BitsWritten);
    }
}

public class BitReaderTests
{
    [Fact]
    public void ReadSingleByte()
    {
        byte[] data = [0xAB];
        using var reader = new BitReader(data);
        Assert.Equal(0xABUL, reader.ReadBits(8));
        reader.Close();
    }

    [Fact]
    public void ReadSmallValues()
    {
        // Write 3 bits (0b101) then 5 bits (0b11010) = byte 0xD5
        byte[] data = [0xD5];
        using var reader = new BitReader(data);
        Assert.Equal(0b101UL, reader.ReadBits(3));
        Assert.Equal(0b11010UL, reader.ReadBits(5));
        reader.Close();
    }

    [Fact]
    public void Read16Bits()
    {
        byte[] data = [0x34, 0x12];
        using var reader = new BitReader(data);
        Assert.Equal(0x1234UL, reader.ReadBits(16));
        reader.Close();
    }

    [Fact]
    public void ReadFixedBits()
    {
        byte[] data = [0xFF, 0x00];
        using var reader = new BitReader(data);
        Assert.Equal(0xFFUL, reader.ReadFixedBits(8));
        Assert.Equal(0x00UL, reader.ReadFixedBits(8));
        reader.Close();
    }

    [Fact]
    public void TotalBitsConsumed_TracksCorrectly()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        using var reader = new BitReader(data);
        Assert.Equal(0, reader.TotalBitsConsumed);
        reader.ReadBits(5);
        Assert.Equal(5, reader.TotalBitsConsumed);
        reader.ReadBits(11);
        Assert.Equal(16, reader.TotalBitsConsumed);
        reader.Close();
    }

    [Fact]
    public void JumpToByteBoundary()
    {
        byte[] data = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        using var reader = new BitReader(data);
        reader.ReadBits(3);
        var status = reader.JumpToByteBoundary();
        Assert.True(status);
        Assert.Equal(8, reader.TotalBitsConsumed);
        reader.Close();
    }

    [Fact]
    public void Roundtrip_WriterReader()
    {
        var writer = new BitWriter();
        writer.Write(7, 42);
        writer.Write(13, 1234);
        writer.Write(32, 0xDEADBEEF);
        writer.ZeroPadToByte();
        var data = writer.GetSpan();

        using var reader = new BitReader(data);
        Assert.Equal(42UL, reader.ReadBits(7));
        Assert.Equal(1234UL, reader.ReadBits(13));
        Assert.Equal(0xDEADBEEFUL, reader.ReadBits(32));
        reader.Close();
    }

    [Fact]
    public void SkipBits()
    {
        byte[] data = new byte[16];
        data[2] = 0xFF; // byte at offset 2
        using var reader = new BitReader(data);
        reader.SkipBits(16); // skip 2 bytes
        Assert.Equal(0xFFUL, reader.ReadBits(8));
        reader.Close();
    }

    [Fact]
    public void AllReadsWithinBounds_True()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        using var reader = new BitReader(data);
        reader.ReadBits(32);
        Assert.True(reader.AllReadsWithinBounds());
        reader.Close();
    }

    [Fact]
    public void Close_ReturnsOk_WhenValid()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        var reader = new BitReader(data);
        reader.ReadBits(8);
        reader.AllReadsWithinBounds();
        var status = reader.Close();
        Assert.True(status);
    }
}
