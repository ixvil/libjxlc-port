// Port of lib/jxl/render_pipeline/stage_blending.cc — Frame blending stage
// Blends the current frame with a reference (background) frame.

using LibJxl.Decoder;
using LibJxl.Fields;

namespace LibJxl.RenderPipeline;

/// <summary>
/// Blending mode for individual patch/pixel operations.
/// Port of jxl::PatchBlendMode.
/// </summary>
public enum PatchBlendMode : byte
{
    None = 0,                // Keep old values
    Replace = 1,             // sample = new
    Add = 2,                 // sample = old + new
    Mul = 3,                 // sample = old * new (optionally clamped)
    BlendAbove = 4,          // Alpha blend: new over old
    BlendBelow = 5,          // Alpha blend: old over new
    AlphaWeightedAddAbove = 6, // alpha * new + old
    AlphaWeightedAddBelow = 7, // alpha * old + new
}

/// <summary>
/// Blending parameters for a single channel or channel group.
/// </summary>
public struct PatchBlending
{
    public PatchBlendMode Mode;
    public int AlphaChannel; // index of alpha EC (for alpha-based modes)
    public bool Clamp;       // clamp alpha/values to [0,1]
}

/// <summary>
/// Pipeline stage that blends the current frame with a reference frame.
/// Port of jxl::BlendingStage.
/// All channels are processed InPlace (modified directly).
/// </summary>
public class StageBlend : RenderPipelineStage
{
    private readonly FrameHeader _frameHeader;
    private readonly ReferenceFrame? _bgFrame;
    private readonly PatchBlending _colorBlending;
    private readonly PatchBlending[] _ecBlending;
    private readonly int _numChannels;
    private readonly int _imageWidth;
    private readonly int _imageHeight;

    private const float kSmallAlpha = 1.0f / (1u << 26);

    public StageBlend(
        FrameHeader frameHeader,
        ReferenceFrame? backgroundFrame,
        int numChannels,
        int imageWidth, int imageHeight)
    {
        _frameHeader = frameHeader;
        _bgFrame = backgroundFrame;
        _numChannels = numChannels;
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
        Settings = StageSettings.None;

        // Map frame-level BlendMode to PatchBlendMode for color channels
        _colorBlending = new PatchBlending
        {
            Mode = MapBlendMode(frameHeader.Blending.Mode),
            AlphaChannel = (int)frameHeader.Blending.AlphaChannel,
            Clamp = frameHeader.Blending.Clamp,
        };

        // Map extra channel blending
        _ecBlending = new PatchBlending[frameHeader.ExtraChannelBlending.Length];
        for (int i = 0; i < _ecBlending.Length; i++)
        {
            _ecBlending[i] = new PatchBlending
            {
                Mode = MapBlendMode(frameHeader.ExtraChannelBlending[i].Mode),
                AlphaChannel = (int)frameHeader.ExtraChannelBlending[i].AlphaChannel,
                Clamp = frameHeader.ExtraChannelBlending[i].Clamp,
            };
        }
    }

    public override string Name => "Blend";

    public override ChannelMode GetChannelMode(int channel)
        => channel < _numChannels ? ChannelMode.InPlace : ChannelMode.Ignored;

    public override bool ProcessRow(
        float[][] inputRows, float[][] outputRows,
        int xsize, int xpos, int ypos, int threadId)
    {
        // If no background frame or Replace mode, nothing to do
        if (_bgFrame?.ChannelData == null || _colorBlending.Mode == PatchBlendMode.Replace)
            return true;

        // Compute background position (applying frame origin offset)
        int bgX = _frameHeader.FrameOriginX0 + xpos;
        int bgY = _frameHeader.FrameOriginY0 + ypos;

        // Check bounds
        if (bgY < 0 || bgY >= _bgFrame.Height || bgX >= _bgFrame.Width)
            return true;

        int startX = 0;
        int effectiveXSize = xsize;
        if (bgX < 0)
        {
            startX = -bgX;
            effectiveXSize += bgX;
            bgX = 0;
        }
        if (bgX + effectiveXSize > _bgFrame.Width)
            effectiveXSize = _bgFrame.Width - bgX;

        if (effectiveXSize <= 0) return true;

        // Blend extra channels first (so alpha is preserved for color blending)
        for (int ec = 0; ec < _ecBlending.Length && (ec + 3) < _numChannels; ec++)
        {
            int c = ec + 3;
            if (_ecBlending[ec].Mode == PatchBlendMode.None ||
                _ecBlending[ec].Mode == PatchBlendMode.Replace)
                continue;

            float[] fg = inputRows[c];
            float[] bg = _bgFrame.ChannelData[c][bgY];

            BlendRow(fg, bg, startX, bgX, effectiveXSize, _ecBlending[ec], inputRows);
        }

        // Blend color channels
        if (_colorBlending.Mode != PatchBlendMode.None)
        {
            for (int c = 0; c < Math.Min(3, _numChannels); c++)
            {
                float[] fg = inputRows[c];
                float[] bg = _bgFrame.ChannelData[c][bgY];

                BlendRow(fg, bg, startX, bgX, effectiveXSize, _colorBlending, inputRows);
            }
        }

        return true;
    }

    private void BlendRow(float[] fg, float[] bg, int fgOffset, int bgOffset,
        int count, PatchBlending blending, float[][] allRows)
    {
        switch (blending.Mode)
        {
            case PatchBlendMode.Add:
                for (int i = 0; i < count; i++)
                    fg[fgOffset + i] += bg[bgOffset + i];
                break;

            case PatchBlendMode.Mul:
                if (blending.Clamp)
                {
                    for (int i = 0; i < count; i++)
                        fg[fgOffset + i] = bg[bgOffset + i] * Math.Clamp(fg[fgOffset + i], 0f, 1f);
                }
                else
                {
                    for (int i = 0; i < count; i++)
                        fg[fgOffset + i] = bg[bgOffset + i] * fg[fgOffset + i];
                }
                break;

            case PatchBlendMode.BlendAbove:
                BlendAlphaAbove(fg, bg, fgOffset, bgOffset, count, blending, allRows);
                break;

            case PatchBlendMode.BlendBelow:
                BlendAlphaBelow(fg, bg, fgOffset, bgOffset, count, blending, allRows);
                break;

            case PatchBlendMode.AlphaWeightedAddAbove:
                AlphaWeightedAdd(fg, bg, fgOffset, bgOffset, count, blending, allRows, true);
                break;

            case PatchBlendMode.AlphaWeightedAddBelow:
                AlphaWeightedAdd(fg, bg, fgOffset, bgOffset, count, blending, allRows, false);
                break;

            case PatchBlendMode.Replace:
                Array.Copy(bg, bgOffset, fg, fgOffset, count);
                break;

            case PatchBlendMode.None:
                break;
        }
    }

    /// <summary>Alpha blend: foreground over background.</summary>
    private void BlendAlphaAbove(float[] fg, float[] bg, int fgOff, int bgOff,
        int count, PatchBlending blending, float[][] allRows)
    {
        int ac = blending.AlphaChannel + 3;
        if (ac >= allRows.Length) return;

        float[]? bgAlpha = _bgFrame?.ChannelData != null && ac < _bgFrame.ChannelData.Length
            ? _bgFrame.ChannelData[ac][0] : null;

        for (int i = 0; i < count; i++)
        {
            float fga = allRows[ac][fgOff + i];
            if (blending.Clamp) fga = Math.Clamp(fga, 0f, 1f);

            float bga = bgAlpha != null ? bgAlpha[bgOff + i] : 1.0f;
            float newA = 1.0f - (1.0f - fga) * (1.0f - bga);
            float rNewA = newA > kSmallAlpha ? 1.0f / newA : 0.0f;

            fg[fgOff + i] = (fg[fgOff + i] * fga + bg[bgOff + i] * bga * (1.0f - fga)) * rNewA;
        }
    }

    /// <summary>Alpha blend: background over foreground.</summary>
    private void BlendAlphaBelow(float[] fg, float[] bg, int fgOff, int bgOff,
        int count, PatchBlending blending, float[][] allRows)
    {
        int ac = blending.AlphaChannel + 3;
        if (ac >= allRows.Length) return;

        float[]? bgAlpha = _bgFrame?.ChannelData != null && ac < _bgFrame.ChannelData.Length
            ? _bgFrame.ChannelData[ac][0] : null;

        for (int i = 0; i < count; i++)
        {
            float fga = allRows[ac][fgOff + i];
            if (blending.Clamp) fga = Math.Clamp(fga, 0f, 1f);

            float bga = bgAlpha != null ? bgAlpha[bgOff + i] : 1.0f;
            if (blending.Clamp) bga = Math.Clamp(bga, 0f, 1f);

            float newA = 1.0f - (1.0f - bga) * (1.0f - fga);
            float rNewA = newA > kSmallAlpha ? 1.0f / newA : 0.0f;

            fg[fgOff + i] = (bg[bgOff + i] * bga + fg[fgOff + i] * fga * (1.0f - bga)) * rNewA;
        }
    }

    /// <summary>Alpha weighted addition.</summary>
    private void AlphaWeightedAdd(float[] fg, float[] bg, int fgOff, int bgOff,
        int count, PatchBlending blending, float[][] allRows, bool fgWeighted)
    {
        int ac = blending.AlphaChannel + 3;
        if (ac >= allRows.Length) return;

        for (int i = 0; i < count; i++)
        {
            float alpha = allRows[ac][fgOff + i];
            if (blending.Clamp) alpha = Math.Clamp(alpha, 0f, 1f);

            if (fgWeighted)
                fg[fgOff + i] = bg[bgOff + i] + fg[fgOff + i] * alpha;
            else
                fg[fgOff + i] = fg[fgOff + i] + bg[bgOff + i] * alpha;
        }
    }

    private static PatchBlendMode MapBlendMode(BlendMode mode) => mode switch
    {
        BlendMode.Replace => PatchBlendMode.Replace,
        BlendMode.Add => PatchBlendMode.Add,
        BlendMode.Blend => PatchBlendMode.BlendAbove,
        BlendMode.AlphaWeightedAdd => PatchBlendMode.AlphaWeightedAddAbove,
        BlendMode.Mul => PatchBlendMode.Mul,
        _ => PatchBlendMode.Replace,
    };
}
