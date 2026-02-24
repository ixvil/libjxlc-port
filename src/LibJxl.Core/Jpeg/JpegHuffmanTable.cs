// Port of lib/jxl/jpeg/enc_jpeg_huffman_decode.{h,cc} — Huffman table building

namespace LibJxl.Jpeg;

/// <summary>Entry in a Huffman lookup table.</summary>
public struct HuffmanTableEntry
{
    public byte Bits;
    public ushort Value;

    public HuffmanTableEntry()
    {
        Bits = 0;
        Value = 0xFFFF;
    }

    public HuffmanTableEntry(byte bits, ushort value)
    {
        Bits = bits;
        Value = value;
    }
}

/// <summary>
/// Huffman table builder for JPEG decoding.
/// Port of jxl::jpeg::BuildJpegHuffmanTable.
/// </summary>
public static class JpegHuffmanTableBuilder
{
    public const int kJpegHuffmanRootTableBits = 8;
    public const int kJpegHuffmanLutSize = 1024;

    /// <summary>
    /// Returns the table width of the next 2nd level table.
    /// </summary>
    private static int NextTableBitSize(int[] count, int len)
    {
        int left = 1 << (len - kJpegHuffmanRootTableBits);
        while (len < JpegConstants.kJpegHuffmanMaxBitLength)
        {
            left -= count[len];
            if (left <= 0) break;
            ++len;
            left <<= 1;
        }
        return len - kJpegHuffmanRootTableBits;
    }

    /// <summary>
    /// Builds jpeg-style Huffman lookup table from the given symbols.
    /// The symbols are in order of increasing bit lengths.
    /// counts[n] gives the number of symbols with bit length n.
    /// </summary>
    public static void BuildJpegHuffmanTable(uint[] counts, uint[] symbols, HuffmanTableEntry[] lut)
    {
        // Make a local copy of the input bit length histogram
        int[] tmpCount = new int[JpegConstants.kJpegHuffmanMaxBitLength + 1];
        int totalCount = 0;
        for (int len = 1; len <= JpegConstants.kJpegHuffmanMaxBitLength; ++len)
        {
            tmpCount[len] = (int)counts[len];
            totalCount += tmpCount[len];
        }

        int tableOffset = 0;
        int tableBits = kJpegHuffmanRootTableBits;
        int tableSize = 1 << tableBits;

        // Special case: single value
        if (totalCount == 1)
        {
            var code = new HuffmanTableEntry(0, (ushort)symbols[0]);
            for (int key = 0; key < tableSize; ++key)
                lut[key] = code;
            return;
        }

        // Fill root table
        int key2 = 0;
        int idx = 0;
        for (int len = 1; len <= kJpegHuffmanRootTableBits; ++len)
        {
            for (; tmpCount[len] > 0; --tmpCount[len])
            {
                var code = new HuffmanTableEntry((byte)len, (ushort)symbols[idx++]);
                int reps = 1 << (kJpegHuffmanRootTableBits - len);
                while (reps-- > 0)
                    lut[key2++] = code;
            }
        }

        // Fill 2nd level tables
        int tableStart = tableSize;
        tableSize = 0;
        int low = 0;
        for (int len = kJpegHuffmanRootTableBits + 1; len <= JpegConstants.kJpegHuffmanMaxBitLength; ++len)
        {
            for (; tmpCount[len] > 0; --tmpCount[len])
            {
                if (low >= tableSize)
                {
                    tableStart += tableSize;
                    tableBits = NextTableBitSize(tmpCount, len);
                    tableSize = 1 << tableBits;
                    low = 0;
                    lut[key2] = new HuffmanTableEntry(
                        (byte)(tableBits + kJpegHuffmanRootTableBits),
                        (ushort)(tableStart - key2));
                    ++key2;
                }
                var code = new HuffmanTableEntry(
                    (byte)(len - kJpegHuffmanRootTableBits),
                    (ushort)symbols[idx++]);
                int reps = 1 << (tableBits - code.Bits);
                while (reps-- > 0)
                    lut[tableStart + low++] = code;
            }
        }
    }
}
