// Port of lib/jxl/render_pipeline/render_pipeline_stage.h — abstract pipeline stage

namespace LibJxl.RenderPipeline;

/// <summary>
/// How a stage processes a given channel.
/// Port of jxl::RenderPipelineChannelMode.
/// </summary>
public enum ChannelMode
{
    Ignored = 0, // Channel not modified
    InPlace = 1, // Modified in-place
    InOut = 2,   // Read from input, write to resized output
    Input = 3,   // Read-only (stage has side effects like writing output)
}

/// <summary>
/// Settings for a render pipeline stage (border, shift).
/// Port of RenderPipelineStage::Settings.
/// </summary>
public struct StageSettings
{
    public int BorderX;
    public int BorderY;
    public int ShiftX;
    public int ShiftY;

    public static StageSettings None => default;

    public static StageSettings SymmetricBorderOnly(int border) =>
        new() { BorderX = border, BorderY = border };

    public static StageSettings Symmetric(int shift, int border) =>
        new() { ShiftX = shift, ShiftY = shift, BorderX = border, BorderY = border };
}

/// <summary>
/// Abstract base for a render pipeline processing stage.
/// Port of jxl::RenderPipelineStage.
/// </summary>
public abstract class RenderPipelineStage
{
    public StageSettings Settings { get; protected set; }

    /// <summary>Stage name for debugging.</summary>
    public abstract string Name { get; }

    /// <summary>How this stage processes the given channel.</summary>
    public abstract ChannelMode GetChannelMode(int channel);

    /// <summary>
    /// Process a single row of data (simple interface without border).
    /// inputRows[channel] and outputRows[channel] are the center row.
    /// </summary>
    public abstract bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId);

    /// <summary>
    /// Process a row with multi-row border access.
    /// inputRows[channel][rowIndex] provides rows y-borderY..y+borderY.
    /// outputRows[channel][oy] provides output rows (1 &lt;&lt; shiftY for InOut).
    /// Default: extracts center row and delegates to ProcessRow.
    /// Override for stages needing neighbor rows (Gaborish, EPF, etc).
    /// </summary>
    public virtual bool ProcessRowWithBorder(
        float[][][] inputRows, float[][][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        int borderY = Settings.BorderY;
        int nc = inputRows.Length;
        var centerIn = new float[nc][];
        var centerOut = new float[nc][];
        for (int c = 0; c < nc; c++)
        {
            centerIn[c] = inputRows[c] != null && inputRows[c].Length > borderY
                ? inputRows[c][borderY]
                : (inputRows[c] != null && inputRows[c].Length > 0 ? inputRows[c][0] : null!);
            centerOut[c] = outputRows[c] != null && outputRows[c].Length > 0
                ? outputRows[c][0]
                : centerIn[c];
        }
        return ProcessRow(centerIn, centerOut, xsize, xpos, ypos, threadId);
    }

    /// <summary>Inform the stage about input sizes.</summary>
    public virtual bool SetInputSizes(List<(int w, int h)> sizes) => true;

    /// <summary>Prepare per-thread storage.</summary>
    public virtual bool PrepareForThreads(int numThreads) => true;

    /// <summary>Mirror index for border extension.</summary>
    protected static int Mirror(int idx, int size)
    {
        if (size <= 1) return 0;
        while (idx < 0) idx += 2 * size;
        idx %= (2 * size);
        return idx < size ? idx : 2 * size - 1 - idx;
    }
}
