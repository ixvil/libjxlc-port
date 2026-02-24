// Port of lib/jxl/render_pipeline/stage_chroma_upsampling.cc
// Chroma upsampling stages (horizontal and vertical 2× using 3/4 + 1/4 interpolation)

namespace LibJxl.RenderPipeline;

/// <summary>
/// Horizontal chroma upsampling: doubles the width of a single channel
/// using linear interpolation (75% center + 25% neighbor).
/// Port of jxl::HorizontalChromaUpsamplingStage.
/// </summary>
public class StageChromaUpsamplingH : RenderPipelineStage
{
    private readonly int _channel;

    public StageChromaUpsamplingH(int channel)
    {
        _channel = channel;
        // shift=1 (2× horizontal), border=1 (need 1 neighbor pixel)
        Settings = new StageSettings { ShiftX = 1, ShiftY = 0, BorderX = 1, BorderY = 0 };
    }

    public override string Name => $"ChromaUpsampleH(c{_channel})";

    public override ChannelMode GetChannelMode(int channel)
        => channel == _channel ? ChannelMode.InOut : ChannelMode.Ignored;

    // This stage uses ProcessRowWithBorder; ProcessRow is not called directly.
    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId) => true;

    /// <summary>
    /// For each input pixel at x, produces two output pixels:
    ///   out[2x]   = 0.75 * in[x] + 0.25 * in[x-1]  (left)
    ///   out[2x+1] = 0.75 * in[x] + 0.25 * in[x+1]  (right)
    /// </summary>
    public override bool ProcessRowWithBorder(
        float[][][] inputRows, float[][][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        int c = _channel;
        float[] rowIn = inputRows[c][0]; // single row (borderY=0)
        float[] rowOut = outputRows[c][0]; // output row (2× width)

        for (int x = 0; x < xsize; x++)
        {
            int px = x + xpos; // position in padded row
            float current = rowIn[px];
            float prev = rowIn[px - 1];
            float next = rowIn[px + 1];

            int ox = x * 2;
            if (ox < rowOut.Length)
                rowOut[ox] = 0.75f * current + 0.25f * prev;
            if (ox + 1 < rowOut.Length)
                rowOut[ox + 1] = 0.75f * current + 0.25f * next;
        }

        return true;
    }
}

/// <summary>
/// Vertical chroma upsampling: doubles the height of a single channel
/// using linear interpolation (75% center + 25% neighbor).
/// Port of jxl::VerticalChromaUpsamplingStage.
/// </summary>
public class StageChromaUpsamplingV : RenderPipelineStage
{
    private readonly int _channel;

    public StageChromaUpsamplingV(int channel)
    {
        _channel = channel;
        // shift=1 (2× vertical), border=1 (need 1 neighbor row)
        Settings = new StageSettings { ShiftX = 0, ShiftY = 1, BorderX = 0, BorderY = 1 };
    }

    public override string Name => $"ChromaUpsampleV(c{_channel})";

    public override ChannelMode GetChannelMode(int channel)
        => channel == _channel ? ChannelMode.InOut : ChannelMode.Ignored;

    // This stage uses ProcessRowWithBorder; ProcessRow is not called directly.
    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId) => true;

    /// <summary>
    /// For each input row at y, produces two output rows:
    ///   out0[x] = 0.75 * mid[x] + 0.25 * top[x]  (upper)
    ///   out1[x] = 0.75 * mid[x] + 0.25 * bot[x]  (lower)
    /// inputRows[c] has 3 rows: [0]=top (y-1), [1]=mid (y), [2]=bot (y+1).
    /// outputRows[c] has 2 rows: [0]=upper, [1]=lower.
    /// </summary>
    public override bool ProcessRowWithBorder(
        float[][][] inputRows, float[][][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        int c = _channel;
        float[] rowTop = inputRows[c][0]; // y-1
        float[] rowMid = inputRows[c][1]; // y
        float[] rowBot = inputRows[c][2]; // y+1
        float[] rowOut0 = outputRows[c][0]; // upper output
        float[] rowOut1 = outputRows[c][1]; // lower output

        for (int x = 0; x < xsize; x++)
        {
            float mid = rowMid[x + xpos];
            float top = rowTop[x + xpos];
            float bot = rowBot[x + xpos];

            float midScaled = 0.75f * mid;

            if (x < rowOut0.Length)
                rowOut0[x] = midScaled + 0.25f * top;
            if (x < rowOut1.Length)
                rowOut1[x] = midScaled + 0.25f * bot;
        }

        return true;
    }
}
