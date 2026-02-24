// Port of lib/jxl/render_pipeline/render_pipeline.h — pipeline orchestrator
using LibJxl.Decoder;

namespace LibJxl.RenderPipeline;

/// <summary>
/// Base class for the render pipeline.
/// Port of jxl::RenderPipeline.
/// </summary>
public abstract class RenderPipeline
{
    protected List<RenderPipelineStage> Stages = [];
    protected int NumChannels;
    protected FrameDimensions? FrameDim;

    /// <summary>
    /// Adds a stage to the pipeline.
    /// </summary>
    public void AddStage(RenderPipelineStage stage)
    {
        Stages.Add(stage);
    }

    /// <summary>
    /// Finalizes the pipeline configuration.
    /// </summary>
    public virtual bool Finalize(FrameDimensions frameDim, int numChannels)
    {
        FrameDim = frameDim;
        NumChannels = numChannels;

        // Inform stages of input sizes
        var sizes = new List<(int w, int h)>();
        for (int c = 0; c < numChannels; c++)
        {
            sizes.Add((frameDim.XSize, frameDim.YSize));
        }

        foreach (var stage in Stages)
        {
            if (!stage.SetInputSizes(sizes))
                return false;

            var newSizes = new List<(int w, int h)>();
            for (int c = 0; c < sizes.Count; c++)
            {
                var mode = stage.GetChannelMode(c);
                if (mode == ChannelMode.InOut)
                {
                    int w = sizes[c].w << stage.Settings.ShiftX;
                    int h = sizes[c].h << stage.Settings.ShiftY;
                    newSizes.Add((w, h));
                }
                else
                {
                    newSizes.Add(sizes[c]);
                }
            }
            sizes = newSizes;
        }

        return true;
    }

    /// <summary>
    /// Prepares for multi-threaded operation.
    /// </summary>
    public virtual bool PrepareForThreads(int numThreads)
    {
        foreach (var stage in Stages)
        {
            if (!stage.PrepareForThreads(numThreads))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Process all stages on the given image data.
    /// </summary>
    public abstract bool ProcessGroup(int groupId, int threadId);

    /// <summary>
    /// Creates a simple (full-frame buffer) pipeline.
    /// </summary>
    public static SimpleRenderPipeline CreateSimple()
    {
        return new SimpleRenderPipeline();
    }
}

/// <summary>
/// Simple render pipeline that stores full-frame buffers as float[][].
/// Port of jxl::SimpleRenderPipeline.
/// Each channel is a float[height][width] array.
/// </summary>
public class SimpleRenderPipeline : RenderPipeline
{
    private float[][][]? _channelData; // [channel][row][col]
    private int _width;
    private int _height;

    /// <summary>Channel data buffers [channel][row].</summary>
    public float[][][]? ChannelData => _channelData;

    public int Width => _width;
    public int Height => _height;

    /// <summary>
    /// Allocates channel buffers.
    /// </summary>
    public bool AllocateBuffers(int width, int height, int numChannels)
    {
        _width = width;
        _height = height;
        NumChannels = numChannels;
        _channelData = new float[numChannels][][];
        for (int c = 0; c < numChannels; c++)
        {
            _channelData[c] = new float[height][];
            for (int y = 0; y < height; y++)
            {
                _channelData[c][y] = new float[width];
            }
        }
        return true;
    }

    /// <summary>
    /// Process all stages row by row across the full image.
    /// </summary>
    public override bool ProcessGroup(int groupId, int threadId)
    {
        if (_channelData == null || Stages.Count == 0)
            return true;

        int nc = _channelData.Length;

        foreach (var stage in Stages)
        {
            // Allocate output buffers for InOut channels
            float[][][]? outputData = null;
            bool hasInOut = false;
            for (int c = 0; c < nc; c++)
            {
                if (stage.GetChannelMode(c) == ChannelMode.InOut)
                {
                    if (outputData == null) outputData = new float[nc][][];
                    int ow = _width << stage.Settings.ShiftX;
                    int oh = _height << stage.Settings.ShiftY;
                    outputData[c] = new float[oh][];
                    for (int y = 0; y < oh; y++)
                        outputData[c][y] = new float[ow];
                    hasInOut = true;
                }
            }

            for (int y = 0; y < _height; y++)
            {
                var inputRows = new float[nc][];
                var outputRows = new float[nc][];

                for (int c = 0; c < nc; c++)
                {
                    int cy = Math.Min(y, _channelData[c].Length - 1);
                    inputRows[c] = _channelData[c][cy];

                    if (outputData != null && outputData[c] != null && y < outputData[c].Length)
                        outputRows[c] = outputData[c][y];
                    else
                        outputRows[c] = inputRows[c]; // In-place
                }

                if (!stage.ProcessRow(inputRows, outputRows, _width, 0, y, threadId))
                    return false;
            }

            if (hasInOut && outputData != null)
            {
                for (int c = 0; c < nc; c++)
                {
                    if (outputData[c] != null)
                        _channelData[c] = outputData[c];
                }
            }
        }

        return true;
    }
}
