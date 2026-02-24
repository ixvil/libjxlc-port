// Port of lib/jxl image features — Patches, Splines, Noise
using LibJxl.Bitstream;

namespace LibJxl.Decoder;

/// <summary>
/// Noise parameters for the noise synthesis stage.
/// Port of jxl::NoiseParams from noise.h.
/// </summary>
public class NoiseParams
{
    public const int kNumNoisePoints = 8;
    public const float kNoisePrecision = 1024.0f;

    public readonly float[] Lut = new float[kNumNoisePoints];

    public bool HasAny()
    {
        foreach (var v in Lut)
            if (MathF.Abs(v) > 1e-3f) return true;
        return false;
    }

    public void Clear()
    {
        Array.Clear(Lut);
    }

    /// <summary>Decodes noise parameters from bitstream.</summary>
    public bool Decode(BitReader br)
    {
        for (int i = 0; i < kNumNoisePoints; i++)
        {
            Lut[i] = br.ReadBits(10) / kNoisePrecision;
        }
        return true;
    }
}

/// <summary>
/// Spline rendering parameters (stub).
/// Port of jxl::Splines from splines.h.
/// </summary>
public class Splines
{
    private bool _hasAny;

    public bool HasAny() => _hasAny;

    /// <summary>Decodes splines from bitstream (stub).</summary>
    public bool Decode(BitReader br, long numPixels)
    {
        // Full implementation requires ANS decoding of quantized spline control points
        // For now: mark that splines exist if the bitstream says so
        _hasAny = false; // TODO: implement full spline decoding
        return true;
    }

    public void Clear() => _hasAny = false;
}

/// <summary>
/// Patch dictionary for referencing and blending image patches (stub).
/// Port of jxl::PatchDictionary from dec_patch_dictionary.h.
/// </summary>
public class PatchDictionary
{
    private bool _hasAny;

    public bool HasAny() => _hasAny;

    /// <summary>Decodes patch dictionary from bitstream (stub).</summary>
    public bool Decode(BitReader br, int xsize, int ysize, int numExtraChannels)
    {
        // Full implementation requires ANS decoding of positions, references, blending
        _hasAny = false; // TODO: implement full patch decoding
        return true;
    }

    public void Clear() => _hasAny = false;
}

/// <summary>
/// Container for all image-level features decoded from the DC global section.
/// Port of jxl::ImageFeatures.
/// </summary>
public class ImageFeatures
{
    public PatchDictionary Patches = new();
    public Splines Splines = new();
    public NoiseParams Noise = new();
}
