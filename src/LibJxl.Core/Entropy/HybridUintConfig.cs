// Port of HybridUintConfig from lib/jxl/dec_ans.h — hybrid unsigned integer encoding
using System.Diagnostics;
using System.Runtime.CompilerServices;
using LibJxl.Base;
using LibJxl.Bitstream;

namespace LibJxl.Entropy;

/// <summary>
/// Hybrid unsigned integer encoding/decoding configuration.
/// Splits numbers into a token part (encoded with ANS/Huffman) and
/// a raw bits part. Port of jxl::HybridUintConfig.
/// </summary>
public struct HybridUintConfig
{
    public uint SplitExponent;
    public uint SplitToken;
    public uint MsbInToken;
    public uint LsbInToken;

    public HybridUintConfig(uint splitExponent, uint msbInToken, uint lsbInToken)
    {
        Debug.Assert(splitExponent >= msbInToken + lsbInToken);
        SplitExponent = splitExponent;
        SplitToken = 1u << (int)splitExponent;
        MsbInToken = msbInToken;
        LsbInToken = lsbInToken;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Encode(uint value, out uint token, out uint nbits, out uint bits)
    {
        if (value < SplitToken)
        {
            token = value;
            nbits = 0;
            bits = 0;
        }
        else
        {
            uint n = (uint)BitOps.FloorLog2Nonzero(value);
            uint m = value - (1u << (int)n);
            token = SplitToken +
                    ((n - SplitExponent) << (int)(MsbInToken + LsbInToken)) +
                    ((m >> (int)(n - MsbInToken)) << (int)LsbInToken) +
                    (m & ((1u << (int)LsbInToken) - 1));
            nbits = n - MsbInToken - LsbInToken;
            bits = (value >> (int)LsbInToken) & ((1u << (int)nbits) - 1);
        }
    }

    /// <summary>
    /// Decodes a hybrid uint given the token (already read from entropy coder)
    /// and reads extra bits from the BitReader. The BitReader must already be refilled.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint DecodeHybridUint(in HybridUintConfig config, uint token, BitReader br)
    {
        uint splitToken = config.SplitToken;
        uint msbInToken = config.MsbInToken;
        uint lsbInToken = config.LsbInToken;
        uint splitExponent = config.SplitExponent;

        if (token < splitToken) return token;

        uint nbits = splitExponent - (msbInToken + lsbInToken) +
                     ((token - splitToken) >> (int)(msbInToken + lsbInToken));
        // Clamp to valid range (stream may be invalid)
        nbits &= 31u;

        uint low = token & ((1u << (int)lsbInToken) - 1);
        token >>= (int)lsbInToken;

        uint bits = (uint)br.PeekBits((int)nbits);
        br.Consume((int)nbits);

        uint ret = (((((1u << (int)msbInToken) | (token & ((1u << (int)msbInToken) - 1)))
                      << (int)nbits) | bits)
                    << (int)lsbInToken) | low;

        return ret;
    }
}
