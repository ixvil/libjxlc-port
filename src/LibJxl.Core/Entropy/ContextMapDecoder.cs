// Port of lib/jxl/dec_context_map.cc — context map decoding
using LibJxl.Base;
using LibJxl.Bitstream;

namespace LibJxl.Entropy;

/// <summary>
/// Decodes the context map from the bitstream.
/// Port of jxl::DecodeContextMap from dec_context_map.cc.
/// </summary>
public static class ContextMapDecoder
{
    private const int MaxClusters = 256;

    /// <summary>
    /// Decodes a context map from the bitstream.
    /// On entry, contextMap.Length must be the number of possible context ids.
    /// Sets numHtrees to the number of different histogram ids.
    /// </summary>
    public static JxlStatus DecodeContextMap(ref byte[] contextMap, out int numHtrees, BitReader br)
    {
        numHtrees = 1;
        bool isSimple = br.ReadFixedBits(1) != 0;

        if (isSimple)
        {
            int bitsPerEntry = (int)br.ReadFixedBits(2);
            if (bitsPerEntry != 0)
            {
                for (int i = 0; i < contextMap.Length; i++)
                    contextMap[i] = (byte)br.ReadBits(bitsPerEntry);
            }
            else
            {
                Array.Fill(contextMap, (byte)0);
            }
        }
        else
        {
            bool useMtf = br.ReadFixedBits(1) != 0;

            var code = new ANSCode();
            var sinkCtxMap = Array.Empty<byte>();

            // Usage of LZ77 is disallowed if decoding only two symbols
            var histStatus = HistogramDecoder.DecodeHistograms(
                br, 1, code, out sinkCtxMap,
                disallowLz77: contextMap.Length <= 2);
            if (!histStatus) return false;

            var reader = ANSSymbolReader.Create(code, br);
            uint maxsym = 0;

            for (int i = 0; i < contextMap.Length; i++)
            {
                uint sym = reader.ReadHybridUintInlined(0, br, sinkCtxMap, usesLz77: true);
                maxsym = sym > maxsym ? sym : maxsym;
                contextMap[i] = (byte)sym;
            }

            if (maxsym >= MaxClusters) return false;

            if (!reader.CheckANSFinalState()) return false;

            if (useMtf)
                InverseMtf.InverseMoveToFrontTransform(contextMap, contextMap.Length);
        }

        // Count histograms
        numHtrees = 0;
        for (int i = 0; i < contextMap.Length; i++)
        {
            if (contextMap[i] >= numHtrees)
                numHtrees = contextMap[i] + 1;
        }

        return VerifyContextMap(contextMap, numHtrees);
    }

    private static JxlStatus VerifyContextMap(byte[] contextMap, int numHtrees)
    {
        bool[] haveHtree = new bool[numHtrees];
        int numFound = 0;

        foreach (byte htree in contextMap)
        {
            if (htree >= numHtrees) return false;
            if (!haveHtree[htree])
            {
                haveHtree[htree] = true;
                numFound++;
            }
        }

        if (numFound != numHtrees) return false;
        return true;
    }
}
