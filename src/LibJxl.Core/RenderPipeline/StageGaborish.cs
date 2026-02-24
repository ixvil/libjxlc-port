// Port of lib/jxl/render_pipeline/stage_gaborish.cc — Gaborish sharpening stage

namespace LibJxl.RenderPipeline;

/// <summary>
/// 3×3 Gaborish sharpening filter applied to XYB channels.
/// Port of jxl::GaborishStage.
/// Applies weighted 3×3 convolution with per-channel configurable weights.
/// weight layout: [c*3 + {0=center, 1=hvNeighbor, 2=diagNeighbor}]
/// </summary>
public class StageGaborish : RenderPipelineStage
{
    // weights_[c*3 + {0=center, 1=hv, 2=diag}]
    private readonly float[] _weights = new float[9];

    public StageGaborish(float xW1, float xW2, float yW1, float yW2, float bW1, float bW2)
    {
        Settings = StageSettings.SymmetricBorderOnly(1);

        // Compute normalized weights per channel
        SetChannelWeights(0, xW1, xW2);
        SetChannelWeights(1, yW1, yW2);
        SetChannelWeights(2, bW1, bW2);
    }

    /// <summary>Creates with default weights (from LoopFilter).</summary>
    public static StageGaborish CreateDefault()
    {
        // Default weights: slightly sharpen, suppress noise
        return new StageGaborish(
            0.115169525f, 0.061248592f,  // X channel
            0.115169525f, 0.061248592f,  // Y channel
            0.115169525f, 0.061248592f); // B channel
    }

    public override string Name => "Gaborish";

    public override ChannelMode GetChannelMode(int channel)
    {
        return channel < 3 ? ChannelMode.InOut : ChannelMode.Ignored;
    }

    /// <summary>
    /// Simple single-row ProcessRow (backward compat for tests without border).
    /// Applies center weight only.
    /// </summary>
    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        for (int c = 0; c < 3; c++)
        {
            float w0 = _weights[c * 3 + 0]; // center
            var row = inputRows[c];
            var outRow = outputRows[c];
            for (int x = 0; x < xsize; x++)
                outRow[x] = row[x] * w0;
        }
        return true;
    }

    /// <summary>
    /// Full 3×3 Gaborish convolution with border access.
    /// Port of GaborishStage::ProcessRow from stage_gaborish.cc.
    /// inputRows[c] has 3 rows: [0]=top, [1]=center, [2]=bottom.
    /// xpos = borderX offset into the padded rows.
    /// </summary>
    public override bool ProcessRowWithBorder(
        float[][][] inputRows, float[][][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        for (int c = 0; c < 3; c++)
        {
            float w0 = _weights[c * 3 + 0]; // center
            float w1 = _weights[c * 3 + 1]; // h/v neighbors
            float w2 = _weights[c * 3 + 2]; // diagonal neighbors

            var rowT = inputRows[c][0]; // y - 1
            var rowM = inputRows[c][1]; // y (center)
            var rowB = inputRows[c][2]; // y + 1
            var rowOut = outputRows[c][0];

            for (int x = 0; x < xsize; x++)
            {
                int px = x + xpos; // offset into padded row

                float t  = rowT[px];
                float tl = rowT[px - 1];
                float tr = rowT[px + 1];
                float m  = rowM[px];
                float l  = rowM[px - 1];
                float r  = rowM[px + 1];
                float b  = rowB[px];
                float bl = rowB[px - 1];
                float br = rowB[px + 1];

                float sum0 = m;
                float sum1 = l + r + t + b;
                float sum2 = tl + tr + bl + br;

                rowOut[x] = sum0 * w0 + sum1 * w1 + sum2 * w2;
            }
        }
        return true;
    }

    private void SetChannelWeights(int c, float w1, float w2)
    {
        float w0 = 1.0f;
        float div = w0 + 4.0f * (w1 + w2);
        _weights[c * 3 + 0] = w0 / div;
        _weights[c * 3 + 1] = w1 / div;
        _weights[c * 3 + 2] = w2 / div;
    }
}
