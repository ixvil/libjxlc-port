// Port of render pipeline stage construction from lib/jxl/dec_frame.cc
// Builds the render pipeline stages from FrameHeader settings.

using LibJxl.ColorManagement;
using LibJxl.Decoder;
using LibJxl.Fields;

namespace LibJxl.RenderPipeline;

/// <summary>
/// Builds the render pipeline from FrameHeader settings.
/// Port of jxl::RenderPipeline construction logic from dec_frame.cc.
/// </summary>
public static class PipelineBuilder
{
    /// <summary>
    /// Constructs the full render pipeline for a VarDCT frame.
    /// Returns a configured SimpleRenderPipeline.
    /// </summary>
    public static SimpleRenderPipeline BuildVarDctPipeline(
        FrameHeader fh, FrameDimensions frameDim,
        int numChannels, int width, int height,
        OpsinParams? opsinParams = null)
    {
        var pipeline = new SimpleRenderPipeline();
        pipeline.AllocateBuffers(width, height, numChannels);

        // 1. Gaborish (if enabled)
        if (fh.Filter.Gab)
        {
            var gab = fh.Filter.GabCustom
                ? new StageGaborish(
                    fh.Filter.GabXWeight1, fh.Filter.GabXWeight2,
                    fh.Filter.GabYWeight1, fh.Filter.GabYWeight2,
                    fh.Filter.GabBWeight1, fh.Filter.GabBWeight2)
                : StageGaborish.CreateDefault();
            pipeline.AddStage(gab);
        }

        // 2. Edge-Preserving Filter (if enabled)
        if (fh.Filter.EpfIters > 0)
        {
            AddEpfStages(pipeline, fh.Filter);
        }

        // 3. Upsampling (if any)
        if (fh.Upsampling > 1)
        {
            int shift = fh.Upsampling switch { 2 => 1, 4 => 2, 8 => 3, _ => 0 };
            if (shift > 0)
            {
                // Add upsampling for each color channel
                for (int c = 0; c < Math.Min(numChannels, 3); c++)
                {
                    var ups = shift switch
                    {
                        1 => StageUpsampling.Create2x(c),
                        2 => StageUpsampling.Create4x(c),
                        3 => StageUpsampling.Create8x(c),
                        _ => StageUpsampling.Create2x(c),
                    };
                    pipeline.AddStage(ups);
                }
            }
        }

        // 4. XYB to Linear RGB (if XYB color transform)
        if (fh.Transform == ColorTransform.XYB && numChannels >= 3)
        {
            var op = opsinParams;
            if (op == null)
            {
                op = new OpsinParams();
                op.InitDefault();
            }
            pipeline.AddStage(new StageXyb(op));
        }
        else if (fh.Transform == ColorTransform.YCbCr && numChannels >= 3)
        {
            pipeline.AddStage(new StageYcbcr());
        }

        // 5. Linear to sRGB (for standard sRGB output)
        if (fh.Transform == ColorTransform.XYB)
        {
            pipeline.AddStage(new StageFromLinear());
        }

        return pipeline;
    }

    /// <summary>
    /// Adds EPF stages according to epf_iters.
    /// epf_iters=1: stage0 only
    /// epf_iters=2: stage1 + stage2
    /// epf_iters=3: stage0 + stage1 + stage2
    /// </summary>
    private static void AddEpfStages(SimpleRenderPipeline pipeline, LoopFilter lf)
    {
        if (lf.EpfIters >= 3)
        {
            pipeline.AddStage(CreateEpfStage0(lf));
        }
        if (lf.EpfIters >= 2)
        {
            pipeline.AddStage(CreateEpfStage1(lf));
        }
        if (lf.EpfIters >= 1)
        {
            pipeline.AddStage(CreateEpfStage2(lf));
        }
    }

    private static StageEpf CreateEpfStage0(LoopFilter lf)
    {
        var stage = StageEpf.CreateStage0(lf.EpfPass0SigmaScale);
        return stage;
    }

    private static StageEpf CreateEpfStage1(LoopFilter lf)
    {
        var stage = StageEpf.CreateStage1();
        return stage;
    }

    private static StageEpf CreateEpfStage2(LoopFilter lf)
    {
        var stage = StageEpf.CreateStage2(lf.EpfPass2SigmaScale);
        return stage;
    }
}

/// <summary>
/// Computes the sigma image for edge-preserving filter.
/// Port of jxl::ComputeSigma from epf.cc.
/// </summary>
public static class EpfSigma
{
    private const float kInvSigmaNum = -1.1715728752538099024f;
    private const int kSigmaPadding = 2;
    private const int kBlockDim = 8;

    /// <summary>
    /// Computes sigma values for each 8×8 block.
    /// sigma_quant = epf_quant_mul / (quant_scale * row_quant * kInvSigmaNum)
    /// sigma = sigma_quant * epf_sharp_lut[sharpness]
    /// Returns 1/sigma stored in a 2D array padded by kSigmaPadding.
    /// </summary>
    public static float[,] ComputeSigma(
        LoopFilter lf,
        float quantScale,
        int[,] rawQuantField,
        byte[,] epfSharpness,
        int xsizeBlocks, int ysizeBlocks)
    {
        // Allocate sigma image with padding
        int sigmaW = xsizeBlocks + 2 * kSigmaPadding;
        int sigmaH = ysizeBlocks + 2 * kSigmaPadding;
        var sigma = new float[sigmaH, sigmaW];

        for (int by = 0; by < ysizeBlocks; by++)
        {
            for (int bx = 0; bx < xsizeBlocks; bx++)
            {
                int qf = rawQuantField[by, bx];
                if (qf == 0) qf = 1; // avoid division by zero

                float sigmaQuant = lf.EpfQuantMul / (quantScale * qf * kInvSigmaNum);
                int sharpIdx = Math.Clamp((int)epfSharpness[by, bx], 0, 7);
                float s = sigmaQuant * lf.EpfSharpLut[sharpIdx];
                // Avoid infinities
                s = MathF.Min(-1e-4f, s);
                sigma[by + kSigmaPadding, bx + kSigmaPadding] = 1.0f / s;
            }
        }

        // Mirror padding
        // Left
        for (int by = 0; by < ysizeBlocks; by++)
        {
            for (int p = 0; p < kSigmaPadding; p++)
            {
                int srcBx = MirrorIdx(-(p + 1), xsizeBlocks);
                sigma[by + kSigmaPadding, kSigmaPadding - 1 - p] =
                    sigma[by + kSigmaPadding, srcBx + kSigmaPadding];
            }
        }
        // Right
        for (int by = 0; by < ysizeBlocks; by++)
        {
            for (int p = 0; p < kSigmaPadding; p++)
            {
                int srcBx = MirrorIdx(xsizeBlocks + p, xsizeBlocks);
                sigma[by + kSigmaPadding, xsizeBlocks + kSigmaPadding + p] =
                    sigma[by + kSigmaPadding, srcBx + kSigmaPadding];
            }
        }
        // Top
        for (int p = 0; p < kSigmaPadding; p++)
        {
            int srcBy = MirrorIdx(-(p + 1), ysizeBlocks);
            for (int bx = 0; bx < sigmaW; bx++)
                sigma[kSigmaPadding - 1 - p, bx] = sigma[srcBy + kSigmaPadding, bx];
        }
        // Bottom
        for (int p = 0; p < kSigmaPadding; p++)
        {
            int srcBy = MirrorIdx(ysizeBlocks + p, ysizeBlocks);
            for (int bx = 0; bx < sigmaW; bx++)
                sigma[ysizeBlocks + kSigmaPadding + p, bx] = sigma[srcBy + kSigmaPadding, bx];
        }

        return sigma;
    }

    private static int MirrorIdx(int idx, int size)
    {
        if (size <= 1) return 0;
        while (idx < 0) idx += 2 * size;
        idx %= (2 * size);
        return idx < size ? idx : 2 * size - 1 - idx;
    }
}
