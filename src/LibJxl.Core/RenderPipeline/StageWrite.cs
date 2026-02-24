// Port of lib/jxl/render_pipeline/stage_write.{h,cc} — output writing stage

namespace LibJxl.RenderPipeline;

/// <summary>
/// Pipeline stage that writes final pixel data to a byte buffer.
/// Port of jxl::GetWriteToOutputStage (simplified).
/// Reads float channels [0,1] and writes to byte[] in RGB/RGBA order.
/// </summary>
public class StageWrite : RenderPipelineStage
{
    private readonly byte[] _output;
    private readonly int _width;
    private readonly int _numChannels;
    private readonly bool _hasAlpha;
    private readonly int _outputStride;

    public StageWrite(byte[] output, int width, int numChannels, bool hasAlpha)
    {
        _output = output;
        _width = width;
        _numChannels = numChannels;
        _hasAlpha = hasAlpha;
        _outputStride = hasAlpha ? 4 : 3;
        Settings = StageSettings.None;
    }

    public override string Name => "Write";

    public override ChannelMode GetChannelMode(int channel)
    {
        if (channel < 3) return ChannelMode.Input;
        if (channel == 3 && _hasAlpha) return ChannelMode.Input;
        return ChannelMode.Ignored;
    }

    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        int rowOffset = ypos * _width * _outputStride;
        int count = Math.Min(xsize, _width - xpos);

        for (int x = 0; x < count; x++)
        {
            int idx = rowOffset + (xpos + x) * _outputStride;

            _output[idx + 0] = FloatToByte(inputRows[0][x]);
            _output[idx + 1] = FloatToByte(inputRows[1][x]);
            _output[idx + 2] = FloatToByte(inputRows[2][x]);

            if (_hasAlpha && inputRows.Length > 3 && inputRows[3] != null)
            {
                _output[idx + 3] = FloatToByte(inputRows[3][x]);
            }
            else if (_hasAlpha)
            {
                _output[idx + 3] = 255;
            }
        }

        return true;
    }

    private static byte FloatToByte(float value)
    {
        int v = (int)(value * 255.0f + 0.5f);
        return (byte)Math.Clamp(v, 0, 255);
    }
}
