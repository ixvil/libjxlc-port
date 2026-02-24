// Port of lib/jxl/dec_huffman.h — Huffman decoder
using System.Runtime.CompilerServices;
using LibJxl.Bitstream;

namespace LibJxl.Entropy;

/// <summary>
/// Huffman decoding data with a 2-level lookup table.
/// Port of jxl::HuffmanDecodingData from dec_huffman.h.
/// </summary>
public sealed class HuffmanDecoder
{
    public const int HuffmanTableBits = 8;

    private HuffmanCode[] _table = [];

    /// <summary>Access to the internal lookup table (used by ANSSymbolReader).</summary>
    public HuffmanCode[] Table => _table;

    /// <summary>
    /// Initializes the table for a single/degenerate symbol (all entries map to symbol 0, 0 bits).
    /// Used when alphabet_size &lt;= 1.
    /// </summary>
    public void InitSingleSymbol()
    {
        int tableSize = 1 << HuffmanTableBits;
        _table = new HuffmanCode[tableSize];
        // All entries: bits=0, value=0
    }

    /// <summary>
    /// Reads Huffman code lengths from a BitReader and builds the decoding table.
    /// Returns false if the code lengths cannot be decoded.
    /// </summary>
    public bool ReadFromBitStream(int alphabetSize, BitReader br)
    {
        byte[] codeLengths = new byte[alphabetSize];
        ushort[] count = new ushort[AnsParams.PrefixMaxBits + 1];

        if (alphabetSize <= 1)
        {
            // Degenerate case: create full table with symbol 0
            int tableSize = 1 << HuffmanTableBits;
            _table = new HuffmanCode[tableSize];
            if (alphabetSize == 1)
            {
                // All entries decode to symbol 0 with 0 bits
                for (int i = 0; i < tableSize; i++)
                    _table[i] = new HuffmanCode { Bits = 0, Value = 0 };
            }
            return true;
        }

        // Read number of code length codes
        int simpleCodeOrSkip = (int)br.ReadBits(2);

        if (simpleCodeOrSkip == 1)
        {
            // Simple prefix code
            int maxBits = 0;
            for (int v = alphabetSize; v > 1; v >>= 1) maxBits++;
            int numSymbols = (int)br.ReadBits(2) + 1;

            ushort[] symbols = new ushort[4];
            for (int i = 0; i < numSymbols; i++)
            {
                symbols[i] = (ushort)br.ReadBits(maxBits);
                if (symbols[i] >= alphabetSize) return false;
            }

            if (numSymbols == 1)
            {
                int tableSize = 1 << HuffmanTableBits;
                _table = new HuffmanCode[tableSize];
                for (int i = 0; i < tableSize; i++)
                    _table[i] = new HuffmanCode { Bits = 0, Value = symbols[0] };
                return true;
            }

            // Build simple code
            Array.Clear(codeLengths);
            if (numSymbols == 2)
            {
                codeLengths[symbols[0]] = 1;
                codeLengths[symbols[1]] = 1;
                count[1] = 2;
            }
            else if (numSymbols == 3)
            {
                codeLengths[symbols[0]] = 1;
                codeLengths[symbols[1]] = 2;
                codeLengths[symbols[2]] = 2;
                count[1] = 1;
                count[2] = 2;
            }
            else
            {
                // 4 symbols
                int treeSelect = (int)br.ReadBits(1);
                if (treeSelect == 0)
                {
                    codeLengths[symbols[0]] = 2;
                    codeLengths[symbols[1]] = 2;
                    codeLengths[symbols[2]] = 2;
                    codeLengths[symbols[3]] = 2;
                    count[2] = 4;
                }
                else
                {
                    codeLengths[symbols[0]] = 1;
                    codeLengths[symbols[1]] = 2;
                    codeLengths[symbols[2]] = 3;
                    codeLengths[symbols[3]] = 3;
                    count[1] = 1;
                    count[2] = 1;
                    count[3] = 2;
                }
            }
        }
        else
        {
            // Complex prefix code — read code length codes, then code lengths
            int numCodeLenCodes = (int)br.ReadBits(4) + 4;
            if (numCodeLenCodes > 18) return false;

            int[] codeLenCodeOrder = [
                1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15
            ];
            byte[] codeLenCodeLengths = new byte[18];
            for (int i = 0; i < numCodeLenCodes; i++)
            {
                codeLenCodeLengths[codeLenCodeOrder[i]] = (byte)br.ReadBits(3);
            }

            // Build the code length decoder table
            ushort[] clCount = new ushort[AnsParams.PrefixMaxBits + 1];
            for (int i = 0; i < 18; i++)
            {
                if (codeLenCodeLengths[i] > 0)
                    clCount[codeLenCodeLengths[i]]++;
            }

            HuffmanCode[] clTable = new HuffmanCode[1 << HuffmanTableBits];
            int clTableSize = HuffmanTable.BuildTable(clTable, 0, HuffmanTableBits,
                codeLenCodeLengths, 18, clCount);
            if (clTableSize == 0) return false;

            // Decode the actual code lengths
            int prevCodeLen = 8;
            int repeat = 0;
            int repeatCodeLen = 0;
            int space = 1 << 15;
            int idx = 0;

            while (idx < alphabetSize && space > 0)
            {
                br.Refill();
                var entry = clTable[br.PeekBits(HuffmanTableBits)];
                br.Consume(entry.Bits);
                int sym = entry.Value;

                if (sym < 16)
                {
                    codeLengths[idx] = (byte)sym;
                    if (sym != 0)
                    {
                        prevCodeLen = sym;
                        space -= 1 << (15 - sym);
                        count[sym]++;
                    }
                    idx++;
                    repeat = 0;
                }
                else
                {
                    int extraBits;
                    int newLen;
                    if (sym == 16)
                    {
                        extraBits = 2;
                        newLen = prevCodeLen;
                    }
                    else // sym == 17
                    {
                        extraBits = 3;
                        newLen = 0;
                    }
                    if (repeatCodeLen != newLen)
                    {
                        repeat = 0;
                        repeatCodeLen = newLen;
                    }
                    int oldRepeat = repeat;
                    if (repeat > 0) repeat -= 2;
                    repeat += (int)br.ReadBits(extraBits) + 3;
                    int repeatDelta = repeat - oldRepeat;
                    for (int j = 0; j < repeatDelta && idx < alphabetSize; j++)
                    {
                        codeLengths[idx] = (byte)repeatCodeLen;
                        if (repeatCodeLen != 0)
                        {
                            space -= 1 << (15 - repeatCodeLen);
                            count[repeatCodeLen]++;
                        }
                        idx++;
                    }
                }
            }

            if (space != 0 && idx != 1) return false;
        }

        // Build the final Huffman table
        int maxTableSize = alphabetSize + 276; // conservative estimate
        _table = new HuffmanCode[maxTableSize];
        int built = HuffmanTable.BuildTable(_table, 0, HuffmanTableBits,
            codeLengths, alphabetSize, count);
        if (built == 0) return false;

        // Trim to actual size
        if (built < _table.Length)
            Array.Resize(ref _table, built);

        return true;
    }

    /// <summary>Decodes the next Huffman-coded symbol (with refill).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadSymbol(BitReader br)
    {
        br.Refill();
        return ReadSymbolWithoutRefill(br);
    }

    /// <summary>
    /// Decodes the next Huffman-coded symbol without refilling the buffer.
    /// The caller must ensure the buffer has been refilled.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadSymbolWithoutRefill(BitReader br)
    {
        int idx = (int)br.PeekBits(HuffmanTableBits);
        ref HuffmanCode entry = ref _table[idx];
        int nBits = entry.Bits;

        if (nBits > HuffmanTableBits)
        {
            br.Consume(HuffmanTableBits);
            nBits -= HuffmanTableBits;
            int offset = entry.Value;
            idx = offset + (int)br.PeekBits(nBits);
            entry = ref _table[idx];
        }

        br.Consume(entry.Bits);
        return entry.Value;
    }
}
