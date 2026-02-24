// Port of lib/jxl/render_pipeline/stage_xyb.{h,cc} — XYB→Linear RGB stage

using LibJxl.ColorManagement;

namespace LibJxl.RenderPipeline;

/// <summary>
/// Pipeline stage that converts XYB to linear RGB.
/// Port of jxl::GetXybStage.
/// Expects 3 input channels: X, Y, B.
/// Outputs 3 channels: linear R, G, B (in-place).
/// </summary>
public class StageXyb : RenderPipelineStage
{
    private readonly OpsinParams _opsinParams;

    public StageXyb(OpsinParams opsinParams)
    {
        _opsinParams = opsinParams;
        Settings = StageSettings.None;
    }

    public override string Name => "XYB";

    public override ChannelMode GetChannelMode(int channel)
    {
        return channel < 3 ? ChannelMode.InPlace : ChannelMode.Ignored;
    }

    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        if (inputRows.Length < 3) return false;

        XybConverter.OpsinToLinearRow(
            inputRows[0], inputRows[1], inputRows[2],
            inputRows[0], inputRows[1], inputRows[2],
            xsize, _opsinParams);

        return true;
    }

    public static StageXyb Create(OpsinParams opsinParams)
    {
        return new StageXyb(opsinParams);
    }
}
