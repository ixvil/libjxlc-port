// Port of lib/jxl/render_pipeline/stage_ycbcr.{h,cc} — YCbCr→RGB stage

namespace LibJxl.RenderPipeline;

/// <summary>
/// Pipeline stage that converts YCbCr to RGB.
/// Port of jxl::GetYCbCrStage.
/// Channels: 0=Cb, 1=Y, 2=Cr → 0=R, 1=G, 2=B (in-place).
/// </summary>
public class StageYcbcr : RenderPipelineStage
{
    public override string Name => "YCbCr";

    public override ChannelMode GetChannelMode(int channel)
    {
        return channel < 3 ? ChannelMode.InPlace : ChannelMode.Ignored;
    }

    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        if (inputRows.Length < 3) return false;

        var cb = inputRows[0];
        var y = inputRows[1];
        var cr = inputRows[2];

        for (int x = 0; x < xsize; x++)
        {
            float yVal = y[x];
            float cbVal = cb[x];
            float crVal = cr[x];

            // YCbCr→RGB (JPEG convention)
            float r = yVal + 1.402f * crVal;
            float g = yVal - 0.344136f * cbVal - 0.714136f * crVal;
            float b = yVal + 1.772f * cbVal;

            cb[x] = r;
            y[x] = g;
            cr[x] = b;
        }

        return true;
    }
}
