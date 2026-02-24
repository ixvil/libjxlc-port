using LibJxl.Base;
using Xunit;

namespace LibJxl.Tests.Base;

public class BitOpsTests
{
    [Theory]
    [InlineData(1u, 31)]
    [InlineData(2u, 30)]
    [InlineData(0x80000000u, 0)]
    [InlineData(0xFFFFFFFFu, 0)]
    [InlineData(0x100u, 23)]
    public void LeadingZeros_Uint32(uint x, int expected)
    {
        Assert.Equal(expected, BitOps.Num0BitsAboveMS1Bit_Nonzero(x));
    }

    [Theory]
    [InlineData(1UL, 63)]
    [InlineData(0x8000000000000000UL, 0)]
    public void LeadingZeros_Uint64(ulong x, int expected)
    {
        Assert.Equal(expected, BitOps.Num0BitsAboveMS1Bit_Nonzero(x));
    }

    [Theory]
    [InlineData(1u, 0)]
    [InlineData(2u, 1)]
    [InlineData(4u, 2)]
    [InlineData(0x80000000u, 31)]
    [InlineData(12u, 2)] // 1100 -> trailing zeros = 2
    public void TrailingZeros_Uint32(uint x, int expected)
    {
        Assert.Equal(expected, BitOps.Num0BitsBelowLS1Bit_Nonzero(x));
    }

    [Fact]
    public void LeadingZeros_Zero_Returns32()
    {
        Assert.Equal(32, BitOps.Num0BitsAboveMS1Bit(0u));
    }

    [Fact]
    public void TrailingZeros_Zero_Returns32()
    {
        Assert.Equal(32, BitOps.Num0BitsBelowLS1Bit(0u));
    }

    [Theory]
    [InlineData(1u, 0)]
    [InlineData(2u, 1)]
    [InlineData(3u, 1)]
    [InlineData(4u, 2)]
    [InlineData(8u, 3)]
    [InlineData(255u, 7)]
    public void FloorLog2Nonzero_Uint32(uint x, int expected)
    {
        Assert.Equal(expected, BitOps.FloorLog2Nonzero(x));
    }

    [Theory]
    [InlineData(1u, 0)]
    [InlineData(2u, 1)]
    [InlineData(3u, 2)]
    [InlineData(4u, 2)]
    [InlineData(5u, 3)]
    [InlineData(8u, 3)]
    [InlineData(255u, 8)]
    public void CeilLog2Nonzero_Uint32(uint x, int expected)
    {
        Assert.Equal(expected, BitOps.CeilLog2Nonzero(x));
    }
}
