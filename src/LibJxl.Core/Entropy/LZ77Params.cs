// Port of LZ77Params from lib/jxl/dec_ans.h
using LibJxl.Bitstream;

namespace LibJxl.Entropy;

/// <summary>
/// LZ77 encoding parameters. Port of jxl::LZ77Params.
/// </summary>
public class LZ77Params
{
    public bool Enabled;
    public uint MinSymbol = 224;
    public uint MinLength = 3;
    public HybridUintConfig LengthUintConfig = new(0, 0, 0);
    public int NonserializedDistanceContext;

    /// <summary>
    /// Reads LZ77 params from the bitstream.
    /// Equivalent to Bundle::Read(br, &amp;code-&gt;lz77).
    /// </summary>
    public bool ReadFromBitStream(BitReader br)
    {
        // Bool(false, &enabled)
        Enabled = FieldReader.ReadBool(br);
        if (!Enabled) return true;

        // U32(Val(224), Val(512), Val(4096), BitsOffset(15, 8), 224, &min_symbol)
        MinSymbol = FieldReader.ReadU32(br,
            U32Distr.Val(224),
            U32Distr.Val(512),
            U32Distr.Val(4096),
            U32Distr.BitsOffset(15, 8));

        // U32(Val(3), Val(4), BitsOffset(2, 5), BitsOffset(8, 9), 3, &min_length)
        MinLength = FieldReader.ReadU32(br,
            U32Distr.Val(3),
            U32Distr.Val(4),
            U32Distr.BitsOffset(2, 5),
            U32Distr.BitsOffset(8, 9));

        return true;
    }
}
