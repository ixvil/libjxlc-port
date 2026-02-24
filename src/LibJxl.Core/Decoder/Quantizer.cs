// Port of lib/jxl/quantizer.h + quantizer.cc — Quantization parameters

using LibJxl.Bitstream;

namespace LibJxl.Decoder;

/// <summary>
/// Quantization parameters for JXL frames.
/// Port of jxl::Quantizer from quantizer.h.
/// </summary>
public class Quantizer
{
    // Constants from quantizer.h
    public const int kGlobalScaleDenom = 1 << 16; // 65536
    public const int kGlobalScaleNumerator = 4096;
    public const int kDefaultQuant = 64;
    public const int kQuantMax = 256;

    public static readonly float[] kZeroBiasDefault = { 0.5f, 0.5f, 0.5f };
    public const float kBiasNumerator = 0.145f;

    public static readonly float[] kDefaultQuantBias =
    {
        1.0f - 0.05465007330715401f,
        1.0f - 0.07005449891748593f,
        1.0f - 0.049935103337343655f,
        0.145f,
    };

    // Serialized fields
    private int _globalScale;
    private int _quantDc;

    // Derived fields
    private float _invGlobalScale;
    private float _globalScaleFloat;
    private float _invQuantDc;
    private readonly float[] _zeroBias = new float[3];
    private readonly float[] _mulDc = new float[4];
    private readonly float[] _invMulDc = new float[4];

    // Dequant matrices reference
    private readonly DequantMatrices _dequant;

    public Quantizer(DequantMatrices dequant)
        : this(dequant, kDefaultQuant, kGlobalScaleDenom / kDefaultQuant)
    {
    }

    public Quantizer(DequantMatrices dequant, int quantDc, int globalScale)
    {
        _dequant = dequant;
        _globalScale = globalScale;
        _quantDc = quantDc;
        Array.Copy(kZeroBiasDefault, _zeroBias, 3);
        RecomputeFromGlobalScale();
    }

    // Properties
    public int GlobalScale => _globalScale;
    public int QuantDc => _quantDc;
    public float Scale => _globalScaleFloat;
    public float InvGlobalScale => _invGlobalScale;
    public float InvQuantDc => _invQuantDc;
    public DequantMatrices Dequant => _dequant;

    /// <summary>
    /// Reads quantizer params from bitstream.
    /// Port of Quantizer::Decode from quantizer.cc.
    /// global_scale: U32(BitsOffset(11,1), BitsOffset(11,2049), BitsOffset(12,4097), BitsOffset(16,8193))
    /// quant_dc: U32(Val(16), BitsOffset(5,1), BitsOffset(8,1), BitsOffset(16,1))
    /// </summary>
    public bool ReadFromBitStream(BitReader br)
    {
        // Read global_scale
        int selector = (int)br.ReadBits(2);
        switch (selector)
        {
            case 0: _globalScale = (int)br.ReadBits(11) + 1; break;
            case 1: _globalScale = (int)br.ReadBits(11) + 2049; break;
            case 2: _globalScale = (int)br.ReadBits(12) + 4097; break;
            case 3: _globalScale = (int)br.ReadBits(16) + 8193; break;
        }

        // Read quant_dc
        selector = (int)br.ReadBits(2);
        switch (selector)
        {
            case 0: _quantDc = 16; break;
            case 1: _quantDc = (int)br.ReadBits(5) + 1; break;
            case 2: _quantDc = (int)br.ReadBits(8) + 1; break;
            case 3: _quantDc = (int)br.ReadBits(16) + 1; break;
        }

        RecomputeFromGlobalScale();
        return true;
    }

    /// <summary>Inverse quant factor for AC coefficients at given quant value.</summary>
    public float InvQuantAc(int quant)
    {
        return _invGlobalScale / quant;
    }

    /// <summary>DC quantization step for a channel.</summary>
    public float GetDcStep(int channel)
    {
        return _invQuantDc * _dequant.DCQuant(channel);
    }

    /// <summary>Inverse DC step for a channel.</summary>
    public float GetInvDcStep(int channel)
    {
        return _dequant.InvDCQuant(channel) * (_globalScaleFloat * _quantDc);
    }

    public ReadOnlySpan<float> MulDC => _mulDc;
    public ReadOnlySpan<float> InvMulDC => _invMulDc;

    private void RecomputeFromGlobalScale()
    {
        _globalScaleFloat = _globalScale * (1.0f / kGlobalScaleDenom);
        _invGlobalScale = 1.0f * kGlobalScaleDenom / _globalScale;
        _invQuantDc = _invGlobalScale / _quantDc;

        for (int c = 0; c < 3; c++)
        {
            _mulDc[c] = GetDcStep(c);
            _invMulDc[c] = GetInvDcStep(c);
        }
    }
}
