// Port of lib/jxl/huffman_table.h/cc — Huffman lookup table builder
using System.Runtime.CompilerServices;

namespace LibJxl.Entropy;

/// <summary>A single entry in a Huffman lookup table.</summary>
public struct HuffmanCode
{
    /// <summary>Number of bits used for this symbol.</summary>
    public byte Bits;

    /// <summary>Symbol value or table offset.</summary>
    public ushort Value;
}

/// <summary>
/// Builds Huffman lookup tables from code lengths.
/// Port of jxl::BuildHuffmanTable from huffman_table.cc.
/// </summary>
public static class HuffmanTable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetNextKey(int key, int len)
    {
        int step = 1 << (len - 1);
        while ((key & step) != 0)
            step >>= 1;
        return (key & (step - 1)) + step;
    }

    private static void ReplicateValue(HuffmanCode[] table, int tableOffset,
                                        int step, int end, HuffmanCode code)
    {
        do
        {
            end -= step;
            table[tableOffset + end] = code;
        } while (end > 0);
    }

    private static int NextTableBitSize(ushort[] count, int len, int rootBits)
    {
        int left = 1 << (len - rootBits);
        while (len < AnsParams.PrefixMaxBits)
        {
            if (left <= count[len]) break;
            left -= count[len];
            len++;
            left <<= 1;
        }
        return len - rootBits;
    }

    /// <summary>
    /// Builds a Huffman lookup table assuming code lengths are in symbol order.
    /// Returns 0 on error, otherwise the populated table size.
    /// </summary>
    public static int BuildTable(HuffmanCode[] rootTable, int rootTableOffset,
                                  int rootBits, byte[] codeLengths,
                                  int codeLengthsSize, ushort[] count)
    {
        if (codeLengthsSize > (1 << AnsParams.PrefixMaxBits))
            return 0;

        ushort[] sorted = new ushort[codeLengthsSize];

        int maxLength = 1;

        // Generate offsets into sorted symbol table by code length
        ushort[] offset = new ushort[AnsParams.PrefixMaxBits + 1];
        {
            ushort sum = 0;
            for (int len = 1; len <= AnsParams.PrefixMaxBits; len++)
            {
                offset[len] = sum;
                if (count[len] != 0)
                {
                    sum = (ushort)(sum + count[len]);
                    maxLength = len;
                }
            }
        }

        // Sort symbols by length, by symbol order within each length
        for (int symbol = 0; symbol < codeLengthsSize; symbol++)
        {
            if (codeLengths[symbol] != 0)
            {
                sorted[offset[codeLengths[symbol]]++] = (ushort)symbol;
            }
        }

        int tableBits = rootBits;
        int tableSize = 1 << tableBits;
        int totalSize = tableSize;
        int tableOffset = rootTableOffset;

        // Special case: only one value
        if (offset[AnsParams.PrefixMaxBits] == 1)
        {
            HuffmanCode code = new() { Bits = 0, Value = sorted[0] };
            for (int key = 0; key < totalSize; key++)
                rootTable[tableOffset + key] = code;
            return totalSize;
        }

        // Reduce table size if possible
        if (tableBits > maxLength)
        {
            tableBits = maxLength;
            tableSize = 1 << tableBits;
        }

        int currentKey = 0;
        int symbolIdx = 0;
        HuffmanCode currentCode = new() { Bits = 1 };
        int step = 2;

        // Fill root table
        do
        {
            for (; count[currentCode.Bits] != 0; count[currentCode.Bits]--)
            {
                currentCode.Value = sorted[symbolIdx++];
                ReplicateValue(rootTable, tableOffset + currentKey, step, tableSize, currentCode);
                currentKey = GetNextKey(currentKey, currentCode.Bits);
            }
            step <<= 1;
        } while (++currentCode.Bits <= tableBits);

        // Replicate if needed
        while (totalSize != tableSize)
        {
            Array.Copy(rootTable, tableOffset, rootTable, tableOffset + tableSize,
                       tableSize);
            tableSize <<= 1;
        }

        // Fill 2nd level tables
        int mask = totalSize - 1;
        int low = -1;
        step = 2;
        for (int len = rootBits + 1; len <= maxLength; len++, step <<= 1)
        {
            for (; count[len] != 0; count[len]--)
            {
                if ((currentKey & mask) != low)
                {
                    tableOffset += tableSize;
                    tableBits = NextTableBitSize(count, len, rootBits);
                    tableSize = 1 << tableBits;
                    totalSize += tableSize;
                    low = currentKey & mask;
                    rootTable[rootTableOffset + low].Bits = (byte)(tableBits + rootBits);
                    rootTable[rootTableOffset + low].Value =
                        (ushort)(tableOffset - rootTableOffset - low);
                }
                currentCode.Bits = (byte)(len - rootBits);
                currentCode.Value = sorted[symbolIdx++];
                ReplicateValue(rootTable, tableOffset + (currentKey >> rootBits),
                               step, tableSize, currentCode);
                currentKey = GetNextKey(currentKey, len);
            }
        }

        return totalSize;
    }
}
