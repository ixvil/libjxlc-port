// Tests for JPEG reconstruction port

using LibJxl.Jpeg;
using Xunit;

namespace LibJxl.Tests.Jpeg;

public class JpegDataTests
{
    [Fact]
    public void JPEGData_DefaultState()
    {
        var data = new JPEGData();
        Assert.Equal(0, data.Width);
        Assert.Equal(0, data.Height);
        Assert.Equal(0u, data.RestartInterval);
        Assert.Empty(data.Components);
        Assert.Empty(data.Quant);
        Assert.Empty(data.HuffmanCode);
        Assert.Empty(data.ScanInfo);
        Assert.Empty(data.MarkerOrder);
        Assert.False(data.HasZeroPaddingBit);
    }

    [Fact]
    public void JPEGComponent_DefaultSamplingFactors()
    {
        var comp = new JPEGComponent();
        Assert.Equal(1, comp.HSampFactor);
        Assert.Equal(1, comp.VSampFactor);
        Assert.Equal(0u, comp.Id);
    }

    [Fact]
    public void JPEGQuantTable_DefaultIsLast()
    {
        var qt = new JPEGQuantTable();
        Assert.True(qt.IsLast);
        Assert.Equal(JpegConstants.kDCTBlockSize, qt.Values.Length);
    }

    [Fact]
    public void JPEGHuffmanCode_DefaultCounts()
    {
        var hc = new JPEGHuffmanCode();
        Assert.Equal(JpegConstants.kJpegHuffmanMaxBitLength + 1, hc.Counts.Length);
        Assert.Equal(JpegConstants.kJpegHuffmanAlphabetSize + 1, hc.Values.Length);
        Assert.True(hc.IsLast);
    }

    [Fact]
    public void Constants_NaturalOrder_Length80()
    {
        Assert.Equal(80, JpegConstants.kJPEGNaturalOrder.Length);
        // First element is DC
        Assert.Equal(0u, JpegConstants.kJPEGNaturalOrder[0]);
        // Last 16 are safety padding (63)
        for (int i = 64; i < 80; i++)
            Assert.Equal(63u, JpegConstants.kJPEGNaturalOrder[i]);
    }

    [Fact]
    public void Constants_ZigZagOrder_Length64()
    {
        Assert.Equal(64, JpegConstants.kJPEGZigZagOrder.Length);
        // DC position
        Assert.Equal(0u, JpegConstants.kJPEGZigZagOrder[0]);
    }

    [Fact]
    public void CalculateMcuSize_SingleComponent()
    {
        var jpg = new JPEGData
        {
            Width = 16,
            Height = 16,
        };
        jpg.Components.Add(new JPEGComponent { HSampFactor = 1, VSampFactor = 1 });

        var scan = new JPEGScanInfo { NumComponents = 1 };
        scan.Components[0].CompIdx = 0;

        jpg.CalculateMcuSize(scan, out int mcusPerRow, out int mcuRows);
        Assert.Equal(2, mcusPerRow); // 16 / 8 = 2
        Assert.Equal(2, mcuRows);
    }

    [Fact]
    public void CalculateMcuSize_YCbCr420()
    {
        var jpg = new JPEGData
        {
            Width = 32,
            Height = 32,
        };
        jpg.Components.Add(new JPEGComponent { HSampFactor = 2, VSampFactor = 2 }); // Y
        jpg.Components.Add(new JPEGComponent { HSampFactor = 1, VSampFactor = 1 }); // Cb
        jpg.Components.Add(new JPEGComponent { HSampFactor = 1, VSampFactor = 1 }); // Cr

        // Interleaved scan (all 3 components)
        var scan = new JPEGScanInfo { NumComponents = 3 };
        scan.Components[0].CompIdx = 0;
        scan.Components[1].CompIdx = 1;
        scan.Components[2].CompIdx = 2;

        jpg.CalculateMcuSize(scan, out int mcusPerRow, out int mcuRows);
        // 32 / (8*2) = 2 MCUs per row
        Assert.Equal(2, mcusPerRow);
        Assert.Equal(2, mcuRows);
    }
}

public class JpegHuffmanTableTests
{
    [Fact]
    public void BuildHuffmanTable_SingleSymbol()
    {
        uint[] counts = new uint[17];
        counts[1] = 1; // one symbol with 1-bit code
        uint[] symbols = { 42 };
        var lut = new HuffmanTableEntry[JpegHuffmanTableBuilder.kJpegHuffmanLutSize];

        JpegHuffmanTableBuilder.BuildJpegHuffmanTable(counts, symbols, lut);

        // All root entries should point to symbol 42
        Assert.Equal(42, lut[0].Value);
        Assert.Equal(0, lut[0].Bits); // 0 bits means single symbol
    }

    [Fact]
    public void BuildHuffmanTable_TwoSymbols()
    {
        uint[] counts = new uint[17];
        counts[1] = 2; // two symbols each with 1 bit
        uint[] symbols = { 10, 20 };
        var lut = new HuffmanTableEntry[JpegHuffmanTableBuilder.kJpegHuffmanLutSize];

        JpegHuffmanTableBuilder.BuildJpegHuffmanTable(counts, symbols, lut);

        // With 8-bit root table, symbol 10 (code 0) fills first 128 entries,
        // symbol 20 (code 1) fills next 128 entries
        Assert.Equal(10, lut[0].Value);
        Assert.Equal(1, lut[0].Bits);
        Assert.Equal(20, lut[128].Value);
        Assert.Equal(1, lut[128].Bits);
    }

    [Fact]
    public void BuildHuffmanTable_DCTable()
    {
        // Typical DC Huffman table: 0-11 bit lengths
        uint[] counts = new uint[17];
        counts[2] = 1;
        counts[3] = 5;
        counts[4] = 1;
        counts[5] = 1;
        counts[6] = 1;
        counts[7] = 1;
        counts[8] = 1;
        counts[9] = 1;

        uint[] symbols = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        var lut = new HuffmanTableEntry[JpegHuffmanTableBuilder.kJpegHuffmanLutSize];

        JpegHuffmanTableBuilder.BuildJpegHuffmanTable(counts, symbols, lut);

        // First symbol should be accessible from root table
        bool foundSymbol0 = false;
        for (int i = 0; i < 256; i++)
        {
            if (lut[i].Value == 0) { foundSymbol0 = true; break; }
        }
        Assert.True(foundSymbol0);
    }
}

public class JpegWriterTests
{
    [Fact]
    public void WriteJpeg_MinimalJpeg_ProducesValidBytes()
    {
        var jpg = CreateMinimalJpeg();
        var result = JpegWriter.WriteJpeg(jpg);

        Assert.NotNull(result);
        Assert.True(result.Length > 4);
        // Check SOI marker
        Assert.Equal(0xFF, result[0]);
        Assert.Equal(0xD8, result[1]);
        // Check EOI is somewhere in the output
        bool hasEoi = false;
        for (int i = 0; i < result.Length - 1; i++)
        {
            if (result[i] == 0xFF && result[i + 1] == 0xD9)
            {
                hasEoi = true;
                break;
            }
        }
        Assert.True(hasEoi, "Output should contain EOI marker");
    }

    [Fact]
    public void WriteJpeg_WithQuantTable_ContainsDQT()
    {
        var jpg = CreateMinimalJpeg();
        // Add DQT before SOF
        jpg.MarkerOrder.Insert(0, 0xDB);

        var qt = new JPEGQuantTable();
        for (int i = 0; i < 64; i++) qt.Values[i] = i + 1;
        qt.Index = 0;
        qt.IsLast = true;
        jpg.Quant.Add(qt);
        jpg.Components[0].QuantIdx = 0;

        var result = JpegWriter.WriteJpeg(jpg);
        Assert.NotNull(result);

        // Should contain DQT marker
        bool hasDqt = false;
        for (int i = 0; i < result.Length - 1; i++)
        {
            if (result[i] == 0xFF && result[i + 1] == 0xDB)
            {
                hasDqt = true;
                break;
            }
        }
        Assert.True(hasDqt);
    }

    [Fact]
    public void WriteJpeg_EmptyMarkerOrder_ReturnNull()
    {
        var jpg = new JPEGData();
        var result = JpegWriter.WriteJpeg(jpg);
        Assert.Null(result);
    }

    [Fact]
    public void WriteJpeg_WithDRI_ContainsRestartInterval()
    {
        var jpg = CreateMinimalJpeg();
        jpg.MarkerOrder.Insert(0, 0xDD);
        jpg.RestartInterval = 100;

        var result = JpegWriter.WriteJpeg(jpg);
        Assert.NotNull(result);

        // Find DRI marker
        bool hasDri = false;
        for (int i = 0; i < result.Length - 1; i++)
        {
            if (result[i] == 0xFF && result[i + 1] == 0xDD)
            {
                hasDri = true;
                // Check restart interval value (big-endian)
                Assert.Equal(0, result[i + 4]); // high byte
                Assert.Equal(100, result[i + 5]); // low byte
                break;
            }
        }
        Assert.True(hasDri);
    }

    private static JPEGData CreateMinimalJpeg()
    {
        var jpg = new JPEGData
        {
            Width = 8,
            Height = 8,
        };
        // Add a quant table (required for SOF marker)
        var qt = new JPEGQuantTable();
        for (int i = 0; i < 64; i++) qt.Values[i] = 1;
        qt.Index = 0;
        qt.IsLast = true;
        jpg.Quant.Add(qt);

        jpg.Components.Add(new JPEGComponent
        {
            Id = 1,
            HSampFactor = 1,
            VSampFactor = 1,
            QuantIdx = 0,
            WidthInBlocks = 1,
            HeightInBlocks = 1,
        });
        // Minimal marker order: SOF + EOI
        jpg.MarkerOrder.Add(0xC0); // SOF0
        jpg.MarkerOrder.Add(0xD9); // EOI
        return jpg;
    }
}

public class HuffmanCodeTableTests
{
    [Fact]
    public void HuffmanCodeTable_InitDepths_FillsWithValue()
    {
        var table = new HuffmanCodeTable();
        table.InitDepths(127);

        for (int i = 0; i < 256; i++)
            Assert.Equal(127, table.Depth[i]);
    }

    [Fact]
    public void HuffmanCodeTable_DefaultNotInitialized()
    {
        var table = new HuffmanCodeTable();
        Assert.False(table.Initialized);
    }
}
