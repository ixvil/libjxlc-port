// Port of lib/jxl/render_pipeline/stage_noise.cc — Noise synthesis stages

namespace LibJxl.RenderPipeline;

/// <summary>
/// Adds synthesized noise to the image for perceptual quality.
/// Port of jxl::AddNoiseStage.
/// Uses a noise LUT indexed by pixel intensity to determine noise strength.
/// Noise channels are read from firstNoiseChannel..firstNoiseChannel+2.
/// </summary>
public class StageNoise : RenderPipelineStage
{
    private const float kRGCorr = 0.9921875f;    // 127/128
    private const float kRGNCorr = 0.0078125f;   // 1/128
    private const float kNormConst = 0.22f;       // Laplacian3 normalization

    private readonly float[] _noiseLut;
    private readonly int _firstNoiseChannel;
    private readonly float _ytox;
    private readonly float _ytob;

    public StageNoise(float[] noiseLut, int firstNoiseChannel = 3,
                      float ytox = 0.0f, float ytob = 1.0f)
    {
        _noiseLut = noiseLut;
        _firstNoiseChannel = firstNoiseChannel;
        _ytox = ytox;
        _ytob = ytob;
        Settings = StageSettings.None;
    }

    public override string Name => "AddNoise";

    public override ChannelMode GetChannelMode(int channel)
    {
        if (channel < 3) return ChannelMode.InPlace;
        if (channel >= _firstNoiseChannel && channel < _firstNoiseChannel + 3)
            return ChannelMode.Input;
        return ChannelMode.Ignored;
    }

    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        // Need at least 3 noise channels
        if (inputRows.Length <= _firstNoiseChannel + 2)
            return true; // No noise channels — pass through

        float[] rowX = inputRows[0];
        float[] rowY = inputRows[1];
        float[] rowB = inputRows[2];
        float[] rndR = inputRows[_firstNoiseChannel];
        float[] rndG = inputRows[_firstNoiseChannel + 1];
        float[] rndC = inputRows[_firstNoiseChannel + 2];

        for (int x = 0; x < xsize; x++)
        {
            float vx = rowX[x];
            float vy = rowY[x];

            float inG = vy - vx;
            float inR = vy + vx;
            float strengthG = NoiseStrength(inG * 0.5f);
            float strengthR = NoiseStrength(inR * 0.5f);

            float noiseR = kNormConst * rndR[x];
            float noiseG = kNormConst * rndG[x];
            float noiseC = kNormConst * rndC[x];

            float redNoise = strengthR * (kRGNCorr * noiseR + kRGCorr * noiseC);
            float greenNoise = strengthG * (kRGNCorr * noiseG + kRGCorr * noiseC);

            float rgNoise = redNoise + greenNoise;
            rowX[x] += _ytox * rgNoise + (redNoise - greenNoise);
            rowY[x] += rgNoise;
            rowB[x] += _ytob * rgNoise;
        }
        return true;
    }

    /// <summary>
    /// Evaluates noise strength from pixel intensity using the noise LUT.
    /// Port of StrengthEvalLut from stage_noise.cc.
    /// </summary>
    public float EvalNoiseStrength(float intensity)
    {
        return NoiseStrength(intensity);
    }

    private float NoiseStrength(float x)
    {
        const int kScale = 6; // kNumNoisePoints - 2
        float scaled = MathF.Max(0, x * kScale);
        int idx = (int)scaled;
        if (idx >= kScale)
        {
            idx = kScale;
            scaled = kScale + 1;
        }
        float frac = scaled - idx;
        float low = _noiseLut[Math.Min(idx, _noiseLut.Length - 1)];
        float high = _noiseLut[Math.Min(idx + 1, _noiseLut.Length - 1)];
        float result = low + (high - low) * frac;
        return Math.Clamp(result, 0, 1.0f);
    }
}

/// <summary>
/// Pre-processing stage that convolves raw noise with a 5×5 subtract-box kernel.
/// Port of jxl::ConvolveNoiseStage.
/// Computes: output = center * (-3.84) + others * 0.16
/// This is 4 * (delta - box_kernel), creating Laplacian-like noise.
/// </summary>
public class StageConvolveNoise : RenderPipelineStage
{
    private readonly int _firstNoiseChannel;

    private const float kCenter = -3.84f;
    private const float kOther = 0.16f;

    public StageConvolveNoise(int firstNoiseChannel = 3)
    {
        _firstNoiseChannel = firstNoiseChannel;
        Settings = StageSettings.SymmetricBorderOnly(2);
    }

    public override string Name => "ConvolveNoise";

    public override ChannelMode GetChannelMode(int channel)
    {
        if (channel >= _firstNoiseChannel && channel < _firstNoiseChannel + 3)
            return ChannelMode.InOut;
        return ChannelMode.Ignored;
    }

    /// <summary>Simple ProcessRow (no border): pass-through.</summary>
    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        for (int c = _firstNoiseChannel; c < _firstNoiseChannel + 3; c++)
        {
            if (c < inputRows.Length && inputRows[c] != outputRows[c])
                Array.Copy(inputRows[c], outputRows[c], xsize);
        }
        return true;
    }

    /// <summary>
    /// Full 5×5 subtract-box convolution with border access.
    /// Port of ConvolveNoiseStage::ProcessRow.
    /// </summary>
    public override bool ProcessRowWithBorder(
        float[][][] inputRows, float[][][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        const int border = 2;

        for (int c = _firstNoiseChannel; c < _firstNoiseChannel + 3; c++)
        {
            if (c >= inputRows.Length) continue;

            float[][] rows = inputRows[c];
            float[] rowOut = outputRows[c][0];

            for (int x = 0; x < xsize; x++)
            {
                int px = x + xpos;
                float center = rows[border][px];

                // Sum all 24 neighbors in 5×5 box
                float others = 0;
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dy == 0 && dx == 0) continue;
                        others += rows[border + dy][px + dx];
                    }
                }

                // 4 * (1 - box_kernel)
                rowOut[x] = center * kCenter + others * kOther;
            }
        }

        return true;
    }
}
