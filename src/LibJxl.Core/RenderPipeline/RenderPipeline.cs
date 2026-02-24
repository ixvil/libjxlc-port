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
    /// Port of jxl::SimpleRenderPipeline::ProcessBuffers.
    /// Handles border mirroring for stages that need neighbor row access.
    /// </summary>
    public override bool ProcessGroup(int groupId, int threadId)
    {
        if (_channelData == null || Stages.Count == 0)
            return true;

        int nc = _channelData.Length;

        foreach (var stage in Stages)
        {
            int borderY = stage.Settings.BorderY;
            int borderX = stage.Settings.BorderX;
            int shiftX = stage.Settings.ShiftX;
            int shiftY = stage.Settings.ShiftY;

            // Track input sizes per channel
            int[] chHeight = new int[nc];
            int[] chWidth = new int[nc];
            for (int c = 0; c < nc; c++)
            {
                chHeight[c] = _channelData[c].Length;
                chWidth[c] = chHeight[c] > 0 ? _channelData[c][0].Length : 0;
            }

            // Allocate output buffers for InOut channels
            float[][][]? outputData = null;
            bool hasInOut = false;
            for (int c = 0; c < nc; c++)
            {
                if (stage.GetChannelMode(c) == ChannelMode.InOut)
                {
                    if (outputData == null) outputData = new float[nc][][];
                    int ow = chWidth[c] << shiftX;
                    int oh = chHeight[c] << shiftY;
                    outputData[c] = new float[oh][];
                    for (int y = 0; y < oh; y++)
                        outputData[c][y] = new float[ow];
                    hasInOut = true;
                }
            }

            // If stage has horizontal border, extend rows with mirrored pixels
            if (borderX > 0)
            {
                for (int c = 0; c < nc; c++)
                {
                    var mode = stage.GetChannelMode(c);
                    if (mode == ChannelMode.Ignored) continue;
                    int w = chWidth[c];
                    int h = chHeight[c];
                    int newW = w + 2 * borderX;
                    for (int y = 0; y < h; y++)
                    {
                        var oldRow = _channelData[c][y];
                        var newRow = new float[newW];
                        Array.Copy(oldRow, 0, newRow, borderX, w);
                        for (int ix = 0; ix < borderX; ix++)
                            newRow[borderX - 1 - ix] = oldRow[MirrorIdx(-(ix + 1), w)];
                        for (int ix = 0; ix < borderX; ix++)
                            newRow[borderX + w + ix] = oldRow[MirrorIdx(w + ix, w)];
                        _channelData[c][y] = newRow;
                    }
                    chWidth[c] = newW;
                }
            }

            // Determine processing dimensions
            int ysize = 0;
            int xsize = 0;
            for (int c = 0; c < nc; c++)
            {
                if (stage.GetChannelMode(c) == ChannelMode.Ignored) continue;
                ysize = Math.Max(chHeight[c], ysize);
                xsize = Math.Max(borderX > 0 ? chWidth[c] - 2 * borderX : chWidth[c], xsize);
            }
            if (ysize == 0) continue;

            // Process each row with multi-row border access
            for (int y = 0; y < ysize; y++)
            {
                var inputRows = new float[nc][][];
                var outputRowsM = new float[nc][][];

                for (int c = 0; c < nc; c++)
                {
                    var mode = stage.GetChannelMode(c);
                    if (mode == ChannelMode.Ignored)
                    {
                        inputRows[c] = [new float[0]];
                        outputRowsM[c] = [new float[0]];
                        continue;
                    }

                    int numRows = 2 * borderY + 1;
                    inputRows[c] = new float[numRows][];
                    for (int iy = -borderY; iy <= borderY; iy++)
                    {
                        int srcY = MirrorIdx(y + iy, chHeight[c]);
                        inputRows[c][iy + borderY] = _channelData[c][srcY];
                    }

                    if (mode == ChannelMode.InOut && outputData?[c] != null)
                    {
                        int outRows = 1 << shiftY;
                        outputRowsM[c] = new float[outRows][];
                        for (int oy = 0; oy < outRows; oy++)
                        {
                            int outY = (y << shiftY) + oy;
                            outputRowsM[c][oy] = outY < outputData[c]!.Length
                                ? outputData[c]![outY]
                                : new float[outputData[c]![0].Length];
                        }
                    }
                    else
                    {
                        outputRowsM[c] = [inputRows[c][borderY]];
                    }
                }

                // xpos = borderX so the stage knows where the padded data starts
                if (!stage.ProcessRowWithBorder(inputRows, outputRowsM, xsize, borderX, y, threadId))
                    return false;
            }

            // Strip horizontal padding from InPlace/Input channels
            if (borderX > 0)
            {
                for (int c = 0; c < nc; c++)
                {
                    var mode = stage.GetChannelMode(c);
                    if (mode == ChannelMode.InPlace || mode == ChannelMode.Input)
                    {
                        int origW = chWidth[c] - 2 * borderX;
                        for (int y = 0; y < chHeight[c]; y++)
                        {
                            var padded = _channelData[c][y];
                            var trimmed = new float[origW];
                            Array.Copy(padded, borderX, trimmed, 0, origW);
                            _channelData[c][y] = trimmed;
                        }
                    }
                }
            }

            // Replace channel data for InOut channels
            if (hasInOut && outputData != null)
            {
                for (int c = 0; c < nc; c++)
                {
                    if (outputData[c] != null)
                        _channelData[c] = outputData[c];
                }
                // Update dimensions from channel 0
                _height = _channelData[0].Length;
                _width = _height > 0 ? _channelData[0][0].Length : 0;
            }
        }

        return true;
    }

    /// <summary>Mirror index to stay within [0, size).</summary>
    private static int MirrorIdx(int idx, int size)
    {
        if (size <= 1) return 0;
        while (idx < 0) idx += 2 * size;
        idx %= (2 * size);
        return idx < size ? idx : 2 * size - 1 - idx;
    }
}
