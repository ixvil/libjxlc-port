// Port of lib/jxl/lehmer_code.h — Lehmer code (factorial base) for permutations
using LibJxl.Base;

namespace LibJxl.Decoder;

/// <summary>
/// Decodes Lehmer codes into permutations using an implicit order-statistics tree.
/// Port of jxl::DecodeLehmerCode.
/// </summary>
public static class LehmerCode
{
    /// <summary>
    /// Decodes the Lehmer code in code[0..n) into permutation[0..n).
    /// </summary>
    public static JxlStatus DecodeLehmerCode(uint[] code, int n, int[] permutation)
    {
        if (n == 0) return false;

        int log2n = BitOps.CeilLog2Nonzero((uint)n);
        int paddedN = 1 << log2n;

        uint[] temp = new uint[paddedN];
        for (int i = 0; i < paddedN; i++)
        {
            int i1 = i + 1;
            temp[i] = (uint)(i1 & -i1); // ValueOfLowest1Bit
        }

        for (int i = 0; i < n; i++)
        {
            if (code[i] + (uint)i >= (uint)n) return false;
            uint rank = code[i] + 1;

            // Extract i-th unused element via implicit order-statistics tree
            int bit = paddedN;
            int next = 0;
            for (int b = 0; b <= log2n; b++)
            {
                int cand = next + bit;
                bit >>= 1;
                if (cand >= 1 && temp[cand - 1] < rank)
                {
                    next = cand;
                    rank -= temp[cand - 1];
                }
            }

            permutation[i] = next;

            // Mark as used
            int j = next + 1;
            while (j <= paddedN)
            {
                temp[j - 1] -= 1;
                j += j & -j; // j += ValueOfLowest1Bit(j)
            }
        }

        return true;
    }
}
