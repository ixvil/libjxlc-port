// Port of ANSCode from lib/jxl/dec_ans.h — ANS code container
namespace LibJxl.Entropy;

/// <summary>
/// Container for all data needed to decode an entropy-coded stream.
/// Holds alias tables (for ANS), Huffman tables, hybrid uint configs,
/// and LZ77 parameters. Port of jxl::ANSCode.
/// </summary>
public class ANSCode
{
    /// <summary>Alias table entries (flat array, indexed by [histo_idx &lt;&lt; log_alpha_size | value]).</summary>
    public AliasTable.Entry[] AliasTables = [];

    /// <summary>Huffman decoding data, one per histogram.</summary>
    public HuffmanDecoder[] HuffmanData = [];

    /// <summary>Hybrid uint configs, one per histogram.</summary>
    public HybridUintConfig[] UintConfig = [];

    /// <summary>Degenerate symbols per histogram (-1 if non-degenerate).</summary>
    public int[] DegenerateSymbols = [];

    /// <summary>Whether prefix (Huffman) codes are used instead of ANS.</summary>
    public bool UsePrefixCode;

    /// <summary>Log2 of alpha size for ANS tables.</summary>
    public int LogAlphaSize;

    /// <summary>LZ77 parameters.</summary>
    public LZ77Params Lz77 = new();

    /// <summary>Maximum number of bits needed for a ReadHybridUint call.</summary>
    public int MaxNumBits;

    /// <summary>Updates max_num_bits based on a symbol appearing in context ctx.</summary>
    public void UpdateMaxNumBits(int ctx, int symbol)
    {
        var cfg = UintConfig[ctx];

        // LZ77 symbols use a different uint config
        if (Lz77.Enabled && Lz77.NonserializedDistanceContext != ctx &&
            symbol >= (int)Lz77.MinSymbol)
        {
            symbol -= (int)Lz77.MinSymbol;
            cfg = Lz77.LengthUintConfig;
        }

        uint splitToken = cfg.SplitToken;
        uint msbInToken = cfg.MsbInToken;
        uint lsbInToken = cfg.LsbInToken;
        uint splitExponent = cfg.SplitExponent;

        if ((uint)symbol < splitToken)
        {
            MaxNumBits = Math.Max(MaxNumBits, (int)splitExponent);
            return;
        }

        uint nExtraBits = splitExponent - (msbInToken + lsbInToken) +
                          (((uint)symbol - splitToken) >> (int)(msbInToken + lsbInToken));
        int totalBits = (int)(msbInToken + lsbInToken + nExtraBits + 1);
        MaxNumBits = Math.Max(MaxNumBits, totalBits);
    }
}
