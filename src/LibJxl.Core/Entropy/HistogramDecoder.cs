// Port of histogram decoding from lib/jxl/dec_ans.cc
// Contains: DecodeVarLenUint8/16, ReadHistogram, DecodeANSCodes,
// DecodeUintConfig, DecodeUintConfigs, DecodeHistograms
using LibJxl.Base;
using LibJxl.Bitstream;

namespace LibJxl.Entropy;

/// <summary>
/// Static methods for decoding ANS histograms from the bitstream.
/// Port of the free functions in dec_ans.cc.
/// </summary>
public static class HistogramDecoder
{
    /// <summary>Decodes a number in [0..255] by reading 1-11 bits.</summary>
    public static int DecodeVarLenUint8(BitReader br)
    {
        if (br.ReadFixedBits(1) != 0)
        {
            int nbits = (int)br.ReadFixedBits(3);
            if (nbits == 0) return 1;
            return (int)br.ReadBits(nbits) + (1 << nbits);
        }
        return 0;
    }

    /// <summary>Decodes a number in [0..65535] by reading 1-21 bits.</summary>
    public static int DecodeVarLenUint16(BitReader br)
    {
        if (br.ReadFixedBits(1) != 0)
        {
            int nbits = (int)br.ReadFixedBits(4);
            if (nbits == 0) return 1;
            return (int)br.ReadBits(nbits) + (1 << nbits);
        }
        return 0;
    }

    // Hardcoded Huffman table for reading log-counts
    private static readonly byte[,] LogCountHuff = {
        {3, 10}, {7, 12}, {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
        {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
        {3, 10}, {5, 0},  {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
        {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
        {3, 10}, {6, 11}, {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
        {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
        {3, 10}, {5, 0},  {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
        {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
        {3, 10}, {7, 13}, {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
        {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
        {3, 10}, {5, 0},  {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
        {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
        {3, 10}, {6, 11}, {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
        {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
        {3, 10}, {5, 0},  {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
        {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2}
    };

    /// <summary>Reads an ANS histogram from the bitstream.</summary>
    public static JxlStatus ReadHistogram(int precisionBits, out int[] counts, BitReader br)
    {
        int range = 1 << precisionBits;
        counts = [];

        int simpleCode = (int)br.ReadBits(1);
        if (simpleCode == 1)
        {
            int[] symbols = new int[2];
            int maxSymbol = 0;
            int numSymbols = (int)br.ReadBits(1) + 1;
            for (int i = 0; i < numSymbols; i++)
            {
                symbols[i] = DecodeVarLenUint8(br);
                if (symbols[i] > maxSymbol) maxSymbol = symbols[i];
            }

            counts = new int[maxSymbol + 1];
            if (numSymbols == 1)
            {
                counts[symbols[0]] = range;
            }
            else
            {
                if (symbols[0] == symbols[1]) return false;
                counts[symbols[0]] = (int)br.ReadBits(precisionBits);
                counts[symbols[1]] = range - counts[symbols[0]];
            }
        }
        else
        {
            int isFlat = (int)br.ReadBits(1);
            if (isFlat == 1)
            {
                int alphabetSize = DecodeVarLenUint8(br) + 1;
                if (alphabetSize > range) return false;
                counts = AliasTable.CreateFlatHistogram(alphabetSize, range);
                return true;
            }

            // Read shift
            uint shift;
            {
                int upperBoundLog = BitOps.FloorLog2Nonzero((uint)(AnsParams.AnsLogTabSize + 1));
                int log = 0;
                for (; log < upperBoundLog; log++)
                {
                    if (br.ReadFixedBits(1) == 0) break;
                }
                shift = (uint)((int)br.ReadBits(log) | (1 << log)) - 1;
                if (shift > AnsParams.AnsLogTabSize + 1)
                    return false;
            }

            int length = DecodeVarLenUint8(br) + 3;
            counts = new int[length];

            int totalCount = 0;
            int[] logcounts = new int[length];
            int omitLog = -1;
            int omitPos = -1;
            int[] same = new int[length];

            for (int i = 0; i < length; i++)
            {
                br.Refill();
                int idx = (int)br.PeekFixedBits(7);
                br.Consume(LogCountHuff[idx, 0]);
                logcounts[i] = LogCountHuff[idx, 1] - 1;

                // RLE symbol
                if (logcounts[i] == AnsParams.AnsLogTabSize)
                {
                    int rleLength = DecodeVarLenUint8(br);
                    same[i] = rleLength + 5;
                    i += rleLength + 3;
                    continue;
                }
                if (logcounts[i] > omitLog)
                {
                    omitLog = logcounts[i];
                    omitPos = i;
                }
            }

            if (omitPos < 0) return false;
            if (omitPos + 1 < length && logcounts[omitPos + 1] == AnsParams.AnsLogTabSize)
                return false;

            int prev = 0;
            int numsame = 0;
            for (int i = 0; i < length; i++)
            {
                if (same[i] != 0)
                {
                    numsame = same[i] - 1;
                    prev = i > 0 ? counts[i - 1] : 0;
                }
                if (numsame > 0)
                {
                    counts[i] = prev;
                    numsame--;
                }
                else
                {
                    int code = logcounts[i];
                    if (i == omitPos || code < 0)
                    {
                        continue;
                    }
                    else if (shift == 0 || code == 0)
                    {
                        counts[i] = 1 << code;
                    }
                    else
                    {
                        int bitcount = AliasTable.GetPopulationCountPrecision(code, (int)shift);
                        counts[i] = (1 << code) +
                                    ((int)br.ReadBits(bitcount) << (code - bitcount));
                    }
                }
                totalCount += counts[i];
            }

            counts[omitPos] = range - totalCount;
            if (counts[omitPos] <= 0) return false;
        }

        return true;
    }

    /// <summary>Decodes ANS codes (Huffman or ANS histograms) from the bitstream.</summary>
    public static JxlStatus DecodeANSCodes(int numHistograms, int maxAlphabetSize,
                                            BitReader br, ANSCode result)
    {
        result.DegenerateSymbols = new int[numHistograms];
        Array.Fill(result.DegenerateSymbols, -1);

        if (result.UsePrefixCode)
        {
            if (maxAlphabetSize > (1 << AnsParams.PrefixMaxBits))
                return false;

            result.HuffmanData = new HuffmanDecoder[numHistograms];
            ushort[] alphabetSizes = new ushort[numHistograms];

            for (int c = 0; c < numHistograms; c++)
            {
                alphabetSizes[c] = (ushort)(DecodeVarLenUint16(br) + 1);
                if (alphabetSizes[c] > maxAlphabetSize)
                    return false;
            }

            for (int c = 0; c < numHistograms; c++)
            {
                result.HuffmanData[c] = new HuffmanDecoder();

                if (alphabetSizes[c] > 1)
                {
                    if (!result.HuffmanData[c].ReadFromBitStream(alphabetSizes[c], br))
                    {
                        if (!br.AllReadsWithinBounds())
                            return new JxlStatus(StatusCode.NotEnoughBytes);
                        return false;
                    }
                }
                else
                {
                    // 0-bit codes: create degenerate table
                    result.HuffmanData[c].InitSingleSymbol();
                }

                foreach (var h in result.HuffmanData[c].Table)
                {
                    if (h.Bits <= HuffmanDecoder.HuffmanTableBits)
                        result.UpdateMaxNumBits(c, h.Value);
                }
            }
        }
        else
        {
            if (maxAlphabetSize > AnsParams.AnsMaxAlphabetSize)
                return false;

            int allocSize = numHistograms * (1 << result.LogAlphaSize);
            result.AliasTables = new AliasTable.Entry[allocSize];

            for (int c = 0; c < numHistograms; c++)
            {
                var status = ReadHistogram(AnsParams.AnsLogTabSize, out var histCounts, br);
                if (!status) return false;

                if (histCounts.Length > maxAlphabetSize)
                    return false;

                // Trim trailing zeros
                int len = histCounts.Length;
                while (len > 0 && histCounts[len - 1] == 0)
                    len--;
                if (len < histCounts.Length)
                    histCounts = histCounts[..len];

                for (int s = 0; s < histCounts.Length; s++)
                {
                    if (histCounts[s] != 0)
                        result.UpdateMaxNumBits(c, s);
                }

                // Detect degenerate (single non-zero) symbols
                int degenerateSymbol = histCounts.Length == 0 ? 0 : histCounts.Length - 1;
                for (int s = 0; s < degenerateSymbol; s++)
                {
                    if (histCounts[s] != 0)
                    {
                        degenerateSymbol = -1;
                        break;
                    }
                }
                result.DegenerateSymbols[c] = degenerateSymbol;

                int tableOffset = c * (1 << result.LogAlphaSize);
                var tableSlice = new AliasTable.Entry[1 << result.LogAlphaSize];
                var initStatus = AliasTable.InitAliasTable(
                    histCounts, AnsParams.AnsLogTabSize, result.LogAlphaSize, tableSlice);
                if (!initStatus) return false;
                Array.Copy(tableSlice, 0, result.AliasTables, tableOffset, tableSlice.Length);
            }
        }

        return true;
    }

    /// <summary>Decodes a single HybridUintConfig from the bitstream.</summary>
    public static JxlStatus DecodeUintConfig(int logAlphaSize, out HybridUintConfig config, BitReader br)
    {
        br.Refill();
        int splitExponent = (int)br.ReadBits(BitOps.CeilLog2Nonzero((uint)(logAlphaSize + 1)));
        int msbInToken = 0;
        int lsbInToken = 0;

        if (splitExponent != logAlphaSize)
        {
            int nbits = BitOps.CeilLog2Nonzero((uint)(splitExponent + 1));
            msbInToken = (int)br.ReadBits(nbits);
            if (msbInToken > splitExponent)
            {
                config = default;
                return false;
            }
            nbits = BitOps.CeilLog2Nonzero((uint)(splitExponent - msbInToken + 1));
            lsbInToken = (int)br.ReadBits(nbits);
        }

        if (lsbInToken + msbInToken > splitExponent)
        {
            config = default;
            return false;
        }

        config = new HybridUintConfig((uint)splitExponent, (uint)msbInToken, (uint)lsbInToken);
        return true;
    }

    /// <summary>Decodes all HybridUintConfigs from the bitstream.</summary>
    public static JxlStatus DecodeUintConfigs(int logAlphaSize, HybridUintConfig[] configs, BitReader br)
    {
        for (int i = 0; i < configs.Length; i++)
        {
            var status = DecodeUintConfig(logAlphaSize, out configs[i], br);
            if (!status) return false;
        }
        return true;
    }

    /// <summary>
    /// Top-level function to decode histograms from the bitstream.
    /// Reads LZ77 params, context map, prefix code flag, uint configs, and ANS codes.
    /// </summary>
    public static JxlStatus DecodeHistograms(BitReader br, int numContexts, ANSCode code,
                                              out byte[] contextMap, bool disallowLz77 = false)
    {
        contextMap = new byte[numContexts];

        // Read LZ77 params
        if (!code.Lz77.ReadFromBitStream(br))
        {
            contextMap = [];
            return false;
        }

        if (code.Lz77.Enabled)
        {
            numContexts++;
            contextMap = new byte[numContexts];
            var lzStatus = DecodeUintConfig(8, out var lengthCfg, br);
            if (!lzStatus)
            {
                contextMap = [];
                return false;
            }
            code.Lz77.LengthUintConfig = lengthCfg;
        }

        if (code.Lz77.Enabled && disallowLz77)
        {
            contextMap = [];
            return false;
        }

        // Read context map
        int numHistograms = 1;
        if (numContexts > 1)
        {
            var cmStatus = ContextMapDecoder.DecodeContextMap(ref contextMap, out numHistograms, br);
            if (!cmStatus)
            {
                contextMap = [];
                return false;
            }
        }

        code.Lz77.NonserializedDistanceContext = contextMap[^1];

        // Read prefix code flag and log_alpha_size
        code.UsePrefixCode = br.ReadFixedBits(1) != 0;
        if (code.UsePrefixCode)
        {
            code.LogAlphaSize = AnsParams.PrefixMaxBits;
        }
        else
        {
            code.LogAlphaSize = (int)br.ReadFixedBits(2) + 5;
        }

        // Read uint configs
        code.UintConfig = new HybridUintConfig[numHistograms];
        var uintStatus = DecodeUintConfigs(code.LogAlphaSize, code.UintConfig, br);
        if (!uintStatus)
        {
            contextMap = [];
            return false;
        }

        // Read ANS codes
        int maxAlphabetSize = 1 << code.LogAlphaSize;
        var ansStatus = DecodeANSCodes(numHistograms, maxAlphabetSize, br, code);
        if (!ansStatus)
        {
            contextMap = [];
            return false;
        }

        return true;
    }
}
