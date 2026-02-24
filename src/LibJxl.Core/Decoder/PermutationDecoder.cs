// Port of lib/jxl/coeff_order.cc — permutation decoding via ANS + Lehmer codes
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;

namespace LibJxl.Decoder;

/// <summary>
/// Decodes permutations from the bitstream using ANS entropy coding and Lehmer codes.
/// Port of jxl::DecodePermutation from coeff_order.cc.
/// </summary>
public static class PermutationDecoder
{
    private const uint PermutationContexts = 8;

    /// <summary>
    /// Computes the context for a coefficient order value.
    /// </summary>
    public static uint CoeffOrderContext(uint val)
    {
        var config = new HybridUintConfig(0, 0, 0);
        config.Encode(val, out uint token, out _, out _);
        return Math.Min(token, PermutationContexts - 1);
    }

    /// <summary>
    /// Reads a permutation from the bitstream.
    /// </summary>
    private static JxlStatus ReadPermutation(int skip, int size, int[]? order,
                                              BitReader br, ANSSymbolReader reader,
                                              byte[] contextMap)
    {
        uint[] lehmer = new uint[size];

        uint end = reader.ReadHybridUint((int)CoeffOrderContext((uint)size), br, contextMap) + (uint)skip;
        if (end > (uint)size)
            return false;

        uint last = 0;
        for (int i = skip; i < (int)end; i++)
        {
            lehmer[i] = reader.ReadHybridUint((int)CoeffOrderContext(last), br, contextMap);
            last = lehmer[i];
            if (lehmer[i] >= (uint)(size - i))
                return false;
        }

        if (order == null) return true;
        return LehmerCode.DecodeLehmerCode(lehmer, size, order);
    }

    /// <summary>
    /// Decodes a permutation from the bitstream. Top-level entry point.
    /// </summary>
    public static JxlStatus DecodePermutation(int skip, int size, int[] order, BitReader br)
    {
        var code = new ANSCode();
        byte[] contextMap;

        var histStatus = HistogramDecoder.DecodeHistograms(
            br, (int)PermutationContexts, code, out contextMap);
        if (!histStatus) return false;

        var reader = ANSSymbolReader.Create(code, br);
        var status = ReadPermutation(skip, size, order, br, reader, contextMap);
        if (!status) return false;

        if (!reader.CheckANSFinalState())
            return false;

        return true;
    }
}
