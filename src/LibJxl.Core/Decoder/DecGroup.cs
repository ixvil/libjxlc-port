// Port of lib/jxl/dec_group.cc — AC group decoding
using LibJxl.Bitstream;
using LibJxl.Entropy;
using LibJxl.Fields;

namespace LibJxl.Decoder;

/// <summary>
/// Decodes AC coefficients for a group of blocks.
/// Port of jxl::DecodeGroup from dec_group.cc.
/// </summary>
public static class DecGroup
{
    /// <summary>
    /// Decodes AC coefficients for a single group and pass.
    /// </summary>
    public static bool DecodeGroupPass(
        FrameHeader header,
        PassesDecoderState decState,
        int groupId,
        int passIdx,
        BitReader br)
    {
        var shared = decState.Shared;
        var frameDim = shared.FrameDim;

        int groupDim = frameDim.GroupDimValue;
        int gx = groupId % frameDim.XSizeGroups;
        int gy = groupId / frameDim.XSizeGroups;
        int x0 = gx * groupDim;
        int y0 = gy * groupDim;

        // Block coordinates
        int bx0 = x0 / 8;
        int by0 = y0 / 8;
        int bx1 = Math.Min(bx0 + groupDim / 8, frameDim.XSizeBlocks);
        int by1 = Math.Min(by0 + groupDim / 8, frameDim.YSizeBlocks);

        if (header.Encoding != FrameEncoding.VarDCT)
            return true; // Modular groups handled separately

        // Create ANS symbol reader
        if (decState.Codes == null || decState.ContextMaps == null)
            return false;

        var code = decState.Codes[passIdx];
        var ctxMap = decState.ContextMaps[passIdx];
        var reader = ANSSymbolReader.Create(code, br);

        // Decode AC coefficients block by block
        for (int by = by0; by < by1; )
        {
            for (int bx = bx0; bx < bx1; )
            {
                var strategy = shared.AcStrategy![by, bx];
                int idx = (int)strategy;
                int covX = AcStrategy.CoveredBlocksX[idx];
                int covY = AcStrategy.CoveredBlocksY[idx];

                // Skip if this block is part of a larger transform
                // (only process the top-left corner)
                if (by > by0 || bx > bx0)
                {
                    // Check if we're at a transform boundary
                    // Simplified: skip sub-blocks of larger transforms
                }

                int numCoeffs = AcStrategy.CoeffCount(strategy);
                int orderBucket = AcStrategy.StrategyOrder[idx];

                // Decode AC coefficients for each channel
                for (int c = 0; c < 3; c++)
                {
                    int blockCtx = shared.BlockCtxMap.Context(0, 0, orderBucket, c);

                    // Read non-zero count
                    int nzCtx = shared.BlockCtxMap.NonZeroContext(numCoeffs / 2, blockCtx);
                    uint nonZeros = reader.ReadHybridUint(nzCtx, br, ctxMap);

                    if (nonZeros > (uint)numCoeffs)
                        return false;

                    // Read AC coefficient values
                    // In full implementation: use zero-density contexts and
                    // coefficient order to read actual values
                    // For now: skip bits for this block
                    for (int k = 0; k < (int)nonZeros; k++)
                    {
                        int zdCtx = shared.BlockCtxMap.ZeroDensityContextsOffset(blockCtx);
                        reader.ReadHybridUint(zdCtx, br, ctxMap);
                    }
                }

                bx += covX;
            }
            by++;
        }

        if (!reader.CheckANSFinalState())
            return false;

        return true;
    }
}
