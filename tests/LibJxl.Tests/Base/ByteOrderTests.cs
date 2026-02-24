using LibJxl.Base;
using Xunit;

namespace LibJxl.Tests.Base;

public class ByteOrderTests
{
    [Fact]
    public void LoadLE32_ReadsCorrectly()
    {
        byte[] data = [0x78, 0x56, 0x34, 0x12];
        Assert.Equal(0x12345678u, ByteOrder.LoadLE32(data));
    }

    [Fact]
    public void LoadBE32_ReadsCorrectly()
    {
        byte[] data = [0x12, 0x34, 0x56, 0x78];
        Assert.Equal(0x12345678u, ByteOrder.LoadBE32(data));
    }

    [Fact]
    public void LoadLE64_ReadsCorrectly()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        Assert.Equal(0x0807060504030201UL, ByteOrder.LoadLE64(data));
    }

    [Fact]
    public void LoadBE64_ReadsCorrectly()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        Assert.Equal(0x0102030405060708UL, ByteOrder.LoadBE64(data));
    }

    [Fact]
    public void StoreBE32_WritesCorrectly()
    {
        byte[] buf = new byte[4];
        ByteOrder.StoreBE32(0x12345678, buf);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, buf);
    }

    [Fact]
    public void StoreLE32_WritesCorrectly()
    {
        byte[] buf = new byte[4];
        ByteOrder.StoreLE32(0x12345678, buf);
        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, buf);
    }

    [Fact]
    public void LoadLE16_ReadsCorrectly()
    {
        byte[] data = [0xCD, 0xAB];
        Assert.Equal((ushort)0xABCD, ByteOrder.LoadLE16(data));
    }

    [Fact]
    public void BSwapFloat_RoundTrips()
    {
        float original = 3.14f;
        float swapped = ByteOrder.BSwapFloat(original);
        float restored = ByteOrder.BSwapFloat(swapped);
        Assert.Equal(original, restored);
    }
}
