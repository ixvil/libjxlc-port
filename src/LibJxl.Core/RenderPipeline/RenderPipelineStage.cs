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
    /// Process a single row of data.
    /// inputRows[channel][rowOffset] and outputRows[channel][rowOffset] are row spans.
    /// </summary>
    public abstract bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId);

    /// <summary>Inform the stage about input sizes.</summary>
    public virtual bool SetInputSizes(List<(int w, int h)> sizes) => true;

    /// <summary>Prepare per-thread storage.</summary>
    public virtual bool PrepareForThreads(int numThreads) => true;
}
