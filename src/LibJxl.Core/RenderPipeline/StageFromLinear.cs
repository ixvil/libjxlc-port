// Port of lib/jxl/render_pipeline/stage_from_linear.{h,cc} — linear→sRGB stage

using LibJxl.ColorManagement;

namespace LibJxl.RenderPipeline;

/// <summary>
/// Pipeline stage that converts linear RGB to sRGB encoding.
/// Port of jxl::GetFromLinearStage.
/// </summary>
public class StageFromLinear : RenderPipelineStage
{
    public override string Name => "FromLinear";

    public override ChannelMode GetChannelMode(int channel)
    {
        return channel < 3 ? ChannelMode.InPlace : ChannelMode.Ignored;
    }

    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        if (inputRows.Length < 3) return false;

        for (int c = 0; c < 3; c++)
        {
            var row = inputRows[c];
            for (int x = 0; x < xsize; x++)
            {
                row[x] = SrgbTransferFunction.LinearToSrgb(row[x]);
            }
        }

        return true;
    }
}
