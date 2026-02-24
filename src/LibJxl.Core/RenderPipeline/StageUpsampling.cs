// Port of lib/jxl/render_pipeline/stage_upsampling.cc — Upsampling stage

namespace LibJxl.RenderPipeline;

/// <summary>
/// Upsampling stage using 5×5 kernel convolution with min/max clamping.
/// Port of jxl::UpsamplingStage.
/// Supports 2×, 4×, or 8× upsampling.
/// </summary>
public class StageUpsampling : RenderPipelineStage
{
    private readonly int _channel;
    private readonly int _shift;    // 1=2×, 2=4×, 3=8×
    private readonly int _factor;   // 2, 4, or 8
    private readonly float[] _kernel; // 25 weights per sub-pixel (factor*factor sub-pixels)

    private StageUpsampling(int channel, int shift, float[]? customKernel = null)
    {
        _channel = channel;
        _shift = shift;
        _factor = 1 << shift;
        Settings = StageSettings.Symmetric(shift, 2);

        // Default kernel: nearest-neighbor (center weight = 1.0)
        // Full implementation uses CustomTransformData weights
        _kernel = customKernel ?? GetDefaultKernel(_factor);
    }

    /// <summary>Creates a 2× upsampling stage for a channel.</summary>
    public static StageUpsampling Create2x(int channel) => new(channel, 1);

    /// <summary>Creates a 4× upsampling stage for a channel.</summary>
    public static StageUpsampling Create4x(int channel) => new(channel, 2);

    /// <summary>Creates an 8× upsampling stage for a channel.</summary>
    public static StageUpsampling Create8x(int channel) => new(channel, 3);

    /// <summary>Creates with custom kernel weights.</summary>
    public static StageUpsampling CreateCustom(int channel, int shift, float[] kernel)
        => new(channel, shift, kernel);

    public override string Name => $"Upsample{_factor}x";

    public override ChannelMode GetChannelMode(int channel)
    {
        return channel == _channel ? ChannelMode.InOut : ChannelMode.Ignored;
    }

    /// <summary>Simple ProcessRow (no border): nearest-neighbor replication.</summary>
    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        var input = inputRows[_channel];
        var output = outputRows[_channel];

        for (int x = 0; x < xsize; x++)
        {
            float val = input[x];
            for (int sx = 0; sx < _factor; sx++)
            {
                int ox = x * _factor + sx;
                if (ox < output.Length)
                    output[ox] = val;
            }
        }

        return true;
    }

    /// <summary>
    /// Full 5×5 kernel-based upsampling with min/max clamping.
    /// Port of UpsamplingStage::ProcessRow.
    /// inputRows[_channel] has 5 rows (border=2): [0]=y-2, [1]=y-1, [2]=y, [3]=y+1, [4]=y+2.
    /// outputRows[_channel] has _factor rows for the output sub-rows.
    /// xpos = borderX offset into padded rows.
    /// </summary>
    public override bool ProcessRowWithBorder(
        float[][][] inputRows, float[][][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        int N = _factor;
        int c = _channel;

        // Gather 5 input rows centered on the current row
        float[][] rows = new float[5][];
        for (int iy = 0; iy < 5; iy++)
            rows[iy] = inputRows[c][iy]; // iy=0..4 maps to y-2..y+2

        for (int x = 0; x < xsize; x++)
        {
            int px = x + xpos;

            // Compute local min/max across 5×5 neighborhood for clamping
            float localMin = float.MaxValue;
            float localMax = float.MinValue;
            for (int iy = 0; iy < 5; iy++)
            {
                for (int ix = -2; ix <= 2; ix++)
                {
                    float v = rows[iy][px + ix];
                    if (v < localMin) localMin = v;
                    if (v > localMax) localMax = v;
                }
            }

            // For each output sub-pixel (oy, ox)
            for (int oy = 0; oy < N; oy++)
            {
                for (int ox = 0; ox < N; ox++)
                {
                    int k = oy * N + ox;
                    int kOff = k * 25;

                    // 5×5 convolution
                    float acc = 0;
                    for (int ky = 0; ky < 5; ky++)
                    {
                        for (int kx = 0; kx < 5; kx++)
                        {
                            acc += rows[ky][px + kx - 2] * _kernel[kOff + ky * 5 + kx];
                        }
                    }

                    // Clamp to local min/max
                    acc = Math.Clamp(acc, localMin, localMax);

                    int outX = x * N + ox;
                    if (outX < outputRows[c][oy].Length)
                        outputRows[c][oy][outX] = acc;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Default upsampling kernel (nearest-neighbor: center weight = 1.0).
    /// Full implementation loads from CustomTransformData.
    /// </summary>
    private static float[] GetDefaultKernel(int factor)
    {
        int n = factor * factor;
        var kernel = new float[25 * n];

        for (int sy = 0; sy < factor; sy++)
        for (int sx = 0; sx < factor; sx++)
        {
            int subPixel = sy * factor + sx;
            int offset = subPixel * 25;
            kernel[offset + 12] = 1.0f; // Center of 5×5
        }

        return kernel;
    }
}
