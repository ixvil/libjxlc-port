// Port of lib/jxl/render_pipeline/stage_epf.cc — Edge Preserving Filter stages

namespace LibJxl.RenderPipeline;

/// <summary>
/// Edge-preserving filter stage for adaptive deblocking.
/// Port of jxl::EPF0Stage/EPF1Stage/EPF2Stage.
/// Uses sum of absolute differences (SAD) to weight neighbors,
/// preserving edges while smoothing flat areas.
/// Weight function: max(0, 1 + sad * inv_sigma).
/// </summary>
public class StageEpf : RenderPipelineStage
{
    // 4 * (sqrt(0.5)-1), so that Weight(sigma) = 0.5
    private const float kInvSigmaNum = -1.1715728752538099024f;
    // kInvSigmaNum / 0.3
    private const float kMinSigma = -3.90524291751269967465540850526868f;

    private readonly int _stage; // 0, 1, or 2
    private readonly float _sigmaScale;
    private readonly float[] _channelScale;
    private readonly float _borderSadMul;

    /// <summary>Per-block sigma image. If null, uses UniformSigma.</summary>
    public float[,]? SigmaImage { get; set; }

    /// <summary>Uniform sigma when SigmaImage is null.</summary>
    public float UniformSigma { get; set; } = 1.0f;

    private StageEpf(int stage, float sigmaScale, float[] channelScale, float borderSadMul)
    {
        _stage = stage;
        _sigmaScale = sigmaScale;
        _channelScale = channelScale;
        _borderSadMul = borderSadMul;

        int border = stage switch
        {
            0 => 3,
            1 => 2,
            2 => 1,
            _ => 1,
        };
        Settings = StageSettings.SymmetricBorderOnly(border);
    }

    /// <summary>Creates EPF stage 0 (7×7 filter via 5×5+3×3 SAD).</summary>
    public static StageEpf CreateStage0(float sigmaScale = 1.65f)
    {
        return new StageEpf(0, sigmaScale, [1.0f, 0.4f, 0.4f], 2.0f);
    }

    /// <summary>Creates EPF stage 1 (5×5 filter via 3×3+3×3 SAD).</summary>
    public static StageEpf CreateStage1(float sigmaScale = 0.78f)
    {
        return new StageEpf(1, sigmaScale, [1.0f, 0.4f, 0.4f], 2.0f);
    }

    /// <summary>Creates EPF stage 2 (3×3 filter via 1 SAD per neighbor).</summary>
    public static StageEpf CreateStage2(float sigmaScale = 0.45f)
    {
        return new StageEpf(2, sigmaScale, [1.0f, 0.4f, 0.4f], 2.0f);
    }

    public override string Name => $"EPF{_stage}";

    public override ChannelMode GetChannelMode(int channel)
    {
        return channel < 3 ? ChannelMode.InOut : ChannelMode.Ignored;
    }

    /// <summary>Simplified ProcessRow (no border): pass-through.</summary>
    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        for (int c = 0; c < 3; c++)
        {
            if (inputRows[c] != outputRows[c])
                Array.Copy(inputRows[c], outputRows[c], xsize);
        }
        return true;
    }

    /// <summary>
    /// Full EPF with multi-row border access.
    /// Dispatches to the appropriate stage implementation.
    /// </summary>
    public override bool ProcessRowWithBorder(
        float[][][] inputRows, float[][][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        return _stage switch
        {
            0 => ProcessEpf0(inputRows, outputRows, xsize, xpos, ypos),
            1 => ProcessEpf1(inputRows, outputRows, xsize, xpos, ypos),
            _ => ProcessEpf2(inputRows, outputRows, xsize, xpos, ypos),
        };
    }

    /// <summary>
    /// EPF Stage 0: 12 neighbors in plus-shaped 5×5, each with 5-point SAD.
    /// Border = 3. Port of EPF0Stage::ProcessRow.
    /// </summary>
    private bool ProcessEpf0(float[][][] inputRows, float[][][] outputRows,
                             int xsize, int xpos, int ypos)
    {
        const int border = 3;
        // 12 neighbor offsets (dy, dx) — plus-shaped 5×5 minus center
        ReadOnlySpan<int> ndy = [-2, -1, -1, -1, 0, 0, 0, 0, 1, 1, 1, 2];
        ReadOnlySpan<int> ndx = [0, -1, 0, 1, -2, -1, 1, 2, -1, 0, 1, 0];
        // 5 SAD offsets — plus-shaped 3×3
        ReadOnlySpan<int> sdy = [0, -1, 0, 1, 0];
        ReadOnlySpan<int> sdx = [0, 0, -1, 0, 1];

        Span<float> sads = stackalloc float[12];
        for (int x = 0; x < xsize; x++)
        {
            int px = x + xpos;
            float invSigma = GetInvSigma(x, ypos);
            if (invSigma < kMinSigma)
            {
                for (int c = 0; c < 3; c++)
                    outputRows[c][0][x] = inputRows[c][border][px];
                continue;
            }

            sads.Clear();
            for (int c = 0; c < 3; c++)
            {
                float scale = _channelScale[c];
                for (int n = 0; n < 12; n++)
                {
                    float sad = 0;
                    for (int s = 0; s < 5; s++)
                    {
                        float v0 = inputRows[c][border + sdy[s]][px + sdx[s]];
                        float v1 = inputRows[c][border + ndy[n] + sdy[s]][px + ndx[n] + sdx[s]];
                        sad += MathF.Abs(v0 - v1);
                    }
                    sads[n] += sad * scale;
                }
            }

            float w = 1.0f;
            float sX = inputRows[0][border][px];
            float sY = inputRows[1][border][px];
            float sB = inputRows[2][border][px];

            for (int n = 0; n < 12; n++)
            {
                float weight = Weight(sads[n], invSigma);
                if (weight <= 0) continue;
                w += weight;
                sX += weight * inputRows[0][border + ndy[n]][px + ndx[n]];
                sY += weight * inputRows[1][border + ndy[n]][px + ndx[n]];
                sB += weight * inputRows[2][border + ndy[n]][px + ndx[n]];
            }

            float invW = 1.0f / w;
            outputRows[0][0][x] = sX * invW;
            outputRows[1][0][x] = sY * invW;
            outputRows[2][0][x] = sB * invW;
        }
        return true;
    }

    /// <summary>
    /// EPF Stage 1: 4 plus-shaped neighbors, composite SAD.
    /// Border = 2. Port of EPF1Stage::ProcessRow.
    /// </summary>
    private bool ProcessEpf1(float[][][] inputRows, float[][][] outputRows,
                             int xsize, int xpos, int ypos)
    {
        const int border = 2;
        ReadOnlySpan<int> ndy = [-1, 0, 0, 1];
        ReadOnlySpan<int> ndx = [0, -1, 1, 0];

        Span<float> sads = stackalloc float[4];
        for (int x = 0; x < xsize; x++)
        {
            int px = x + xpos;
            float invSigma = GetInvSigma(x, ypos);
            if (invSigma < kMinSigma)
            {
                for (int c = 0; c < 3; c++)
                    outputRows[c][0][x] = inputRows[c][border][px];
                continue;
            }

            sads.Clear();
            for (int c = 0; c < 3; c++)
            {
                float scale = _channelScale[c];
                for (int n = 0; n < 4; n++)
                {
                    float sad = 0;
                    // Plus-shaped SAD: center + 4 h/v neighbors
                    sad += MathF.Abs(inputRows[c][border][px] -
                                     inputRows[c][border + ndy[n]][px + ndx[n]]);
                    sad += MathF.Abs(inputRows[c][border - 1][px] -
                                     inputRows[c][border + ndy[n] - 1][px + ndx[n]]);
                    sad += MathF.Abs(inputRows[c][border + 1][px] -
                                     inputRows[c][border + ndy[n] + 1][px + ndx[n]]);
                    sad += MathF.Abs(inputRows[c][border][px - 1] -
                                     inputRows[c][border + ndy[n]][px + ndx[n] - 1]);
                    sad += MathF.Abs(inputRows[c][border][px + 1] -
                                     inputRows[c][border + ndy[n]][px + ndx[n] + 1]);
                    sads[n] += sad * scale;
                }
            }

            float w = 1.0f;
            float sX = inputRows[0][border][px];
            float sY = inputRows[1][border][px];
            float sB = inputRows[2][border][px];

            for (int n = 0; n < 4; n++)
            {
                float weight = Weight(sads[n], invSigma);
                if (weight <= 0) continue;
                w += weight;
                sX += weight * inputRows[0][border + ndy[n]][px + ndx[n]];
                sY += weight * inputRows[1][border + ndy[n]][px + ndx[n]];
                sB += weight * inputRows[2][border + ndy[n]][px + ndx[n]];
            }

            float invW = 1.0f / w;
            outputRows[0][0][x] = sX * invW;
            outputRows[1][0][x] = sY * invW;
            outputRows[2][0][x] = sB * invW;
        }
        return true;
    }

    /// <summary>
    /// EPF Stage 2: 4 plus-shaped neighbors, 1 SAD per neighbor.
    /// Border = 1. Port of EPF2Stage::ProcessRow.
    /// </summary>
    private bool ProcessEpf2(float[][][] inputRows, float[][][] outputRows,
                             int xsize, int xpos, int ypos)
    {
        const int border = 1;
        ReadOnlySpan<int> ndy = [-1, 0, 0, 1];
        ReadOnlySpan<int> ndx = [0, -1, 1, 0];

        for (int x = 0; x < xsize; x++)
        {
            int px = x + xpos;
            float invSigma = GetInvSigma(x, ypos);
            if (invSigma < kMinSigma)
            {
                for (int c = 0; c < 3; c++)
                    outputRows[c][0][x] = inputRows[c][border][px];
                continue;
            }

            float cxv = inputRows[0][border][px];
            float cyv = inputRows[1][border][px];
            float cbv = inputRows[2][border][px];

            float w = 1.0f;
            float sX = cxv, sY = cyv, sB = cbv;

            for (int n = 0; n < 4; n++)
            {
                float nx_ = inputRows[0][border + ndy[n]][px + ndx[n]];
                float ny_ = inputRows[1][border + ndy[n]][px + ndx[n]];
                float nb  = inputRows[2][border + ndy[n]][px + ndx[n]];

                float sad = MathF.Abs(nx_ - cxv) * _channelScale[0]
                          + MathF.Abs(ny_ - cyv) * _channelScale[1]
                          + MathF.Abs(nb  - cbv) * _channelScale[2];

                float weight = Weight(sad, invSigma);
                if (weight <= 0) continue;
                w += weight;
                sX += weight * nx_;
                sY += weight * ny_;
                sB += weight * nb;
            }

            float invW = 1.0f / w;
            outputRows[0][0][x] = sX * invW;
            outputRows[1][0][x] = sY * invW;
            outputRows[2][0][x] = sB * invW;
        }
        return true;
    }

    /// <summary>
    /// Weight function: max(0, 1 + sad * inv_sigma).
    /// Port of Weight() from stage_epf.cc.
    /// </summary>
    private static float Weight(float sad, float invSigma)
    {
        float v = 1.0f + sad * invSigma;
        return v > 0 ? v : 0;
    }

    /// <summary>Gets inverse sigma for a given pixel position.</summary>
    private float GetInvSigma(int x, int y)
    {
        const int kBlockDim = 8;
        if (SigmaImage != null)
        {
            int bx = Math.Clamp(x / kBlockDim, 0, SigmaImage.GetLength(1) - 1);
            int by = Math.Clamp(y / kBlockDim, 0, SigmaImage.GetLength(0) - 1);
            return SigmaImage[by, bx] * _sigmaScale * kInvSigmaNum;
        }
        return UniformSigma * _sigmaScale * kInvSigmaNum;
    }
}
