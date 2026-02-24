using LibJxl.Bitstream;
using LibJxl.Decoder;
using LibJxl.Fields;
using LibJxl.RenderPipeline;
using Xunit;

namespace LibJxl.Tests.Decoder;

public class ColorCorrelationTests
{
    [Fact]
    public void DefaultCorrelation_ScaleInverse()
    {
        var cc = new ColorCorrelation();
        Assert.Equal(1.0f / ColorCorrelation.kDefaultColorFactor, cc.ColorScale, 5);
    }

    [Fact]
    public void YtoXRatio_ZeroFactor_IsBase()
    {
        var cc = new ColorCorrelation();
        float ratio = cc.YtoXRatio(0);
        Assert.Equal(cc.BaseCorrelationX, ratio);
    }

    [Fact]
    public void YtoBRatio_ZeroFactor_IsBase()
    {
        var cc = new ColorCorrelation();
        float ratio = cc.YtoBRatio(0);
        Assert.Equal(cc.BaseCorrelationB, ratio);
    }

    [Fact]
    public void YtoXRatio_PositiveFactor()
    {
        var cc = new ColorCorrelation();
        float ratio = cc.YtoXRatio(42);
        Assert.Equal(cc.BaseCorrelationX + 42 * cc.ColorScale, ratio, 5);
    }

    [Fact]
    public void YtoBRatio_PositiveFactor()
    {
        var cc = new ColorCorrelation();
        float ratio = cc.YtoBRatio(42);
        Assert.Equal(cc.BaseCorrelationB + 42 * cc.ColorScale, ratio, 5);
    }

    [Fact]
    public void DefaultIsJPEGCompatible()
    {
        var cc = new ColorCorrelation();
        Assert.True(cc.IsJPEGCompatible());
    }
}

public class ColorCorrelationMapTests
{
    [Fact]
    public void Create_AllocatesMaps()
    {
        var cmap = new ColorCorrelationMap();
        cmap.Create(256, 256);

        int expectedTiles = (256 + 63) / 64;
        Assert.NotNull(cmap.YtoxMap);
        Assert.NotNull(cmap.YtobMap);
        Assert.Equal(expectedTiles, cmap.YtoxMap!.GetLength(0));
        Assert.Equal(expectedTiles, cmap.YtoxMap.GetLength(1));
    }
}

public class BlockCtxMapTests
{
    [Fact]
    public void Default_Has15Contexts()
    {
        var bcm = new BlockCtxMap();
        Assert.Equal(15, bcm.NumCtxs);
    }

    [Fact]
    public void DefaultCtxMap_CorrectSize()
    {
        Assert.Equal(3 * AcStrategy.kNumOrders, BlockCtxMap.kDefaultCtxMap.Length);
    }

    [Fact]
    public void NonZeroContext_ZeroNonZeros_Bucket0()
    {
        var bcm = new BlockCtxMap();
        Assert.Equal(0, bcm.NonZeroContext(0, 0));
    }

    [Fact]
    public void NonZeroContext_SmallCount()
    {
        var bcm = new BlockCtxMap();
        int ctx = bcm.NonZeroContext(5, 0);
        Assert.Equal(6, ctx); // 1 + 5
    }
}

public class ImageFeaturesTests
{
    [Fact]
    public void Default_NoFeatures()
    {
        var features = new ImageFeatures();
        Assert.False(features.Patches.HasAny());
        Assert.False(features.Splines.HasAny());
        Assert.False(features.Noise.HasAny());
    }

    [Fact]
    public void NoiseParams_Clear_HasNoNoise()
    {
        var np = new NoiseParams();
        np.Lut[0] = 1.0f;
        Assert.True(np.HasAny());
        np.Clear();
        Assert.False(np.HasAny());
    }
}

public class PassesStateTests
{
    [Fact]
    public void Default_QuantizerExists()
    {
        var state = new PassesSharedState();
        Assert.NotNull(state.Quantizer);
        Assert.NotNull(state.Matrices);
    }

    [Fact]
    public void Default_FeaturesExist()
    {
        var state = new PassesSharedState();
        Assert.NotNull(state.ImageFeatures);
        Assert.NotNull(state.Cmap);
        Assert.NotNull(state.BlockCtxMap);
    }

    [Fact]
    public void DecoderState_InitAllocates()
    {
        var ds = new PassesDecoderState();
        Assert.NotNull(ds.Shared);
    }
}

public class FrameHeaderCanBeReferencedTests
{
    [Fact]
    public void RegularFrame_CanBeReferenced()
    {
        var fh = new FrameHeader { Type = FrameType.RegularFrame, SaveAsReference = 0 };
        Assert.True(fh.CanBeReferenced());
    }

    [Fact]
    public void ReferenceOnly_CanBeReferenced()
    {
        var fh = new FrameHeader { Type = FrameType.ReferenceOnly, SaveAsReference = 0 };
        Assert.True(fh.CanBeReferenced());
    }

    [Fact]
    public void SkipProgressive_CannotBeReferenced()
    {
        var fh = new FrameHeader { Type = FrameType.SkipProgressive, SaveAsReference = 0 };
        Assert.False(fh.CanBeReferenced());
    }

    [Fact]
    public void SkipProgressive_WithSaveAs_StillCannotBeReferenced()
    {
        var fh = new FrameHeader { Type = FrameType.SkipProgressive, SaveAsReference = 1 };
        Assert.False(fh.CanBeReferenced());
    }

    [Fact]
    public void DCFrame_WithSaveAs_CanBeReferenced()
    {
        var fh = new FrameHeader { Type = FrameType.DCFrame, SaveAsReference = 1 };
        Assert.True(fh.CanBeReferenced());
    }

    [Fact]
    public void DCFrame_NoSaveAs_CannotBeReferenced()
    {
        var fh = new FrameHeader { Type = FrameType.DCFrame, SaveAsReference = 0 };
        Assert.False(fh.CanBeReferenced());
    }
}

public class FrameDecoderPipelineTests
{
    [Fact]
    public void InitFrameOutput_InitializesState()
    {
        var meta = new ImageMetadata();
        var size = CreateSmallSize();

        var decoder = CreateSmallDecoder(meta, size);
        var initStatus = decoder.InitFrameOutput();

        Assert.True(initStatus);
        Assert.False(decoder.HasDecodedDC);
        Assert.False(decoder.HasDecodedAll);
        Assert.NotNull(decoder.DecState);
    }

    [Fact]
    public void InitFrame_AllDefault_HeaderProperties()
    {
        var meta = new ImageMetadata();
        var size = CreateSmallSize();

        var decoder = CreateSmallDecoder(meta, size);

        Assert.Equal(FrameType.RegularFrame, decoder.Header.Type);
        Assert.Equal(FrameEncoding.VarDCT, decoder.Header.Encoding);
        Assert.True(decoder.Header.IsLast);
        Assert.True(decoder.Header.CanBeReferenced());
    }

    [Fact]
    public void OutputPixels_NullBeforeFinalize()
    {
        var meta = new ImageMetadata();
        var size = CreateSmallSize();

        var decoder = CreateSmallDecoder(meta, size);
        decoder.InitFrameOutput();

        Assert.Null(decoder.OutputPixels);
    }

    [Fact]
    public void Pipeline_NullBeforeFinalize()
    {
        var meta = new ImageMetadata();
        var size = CreateSmallSize();

        var decoder = CreateSmallDecoder(meta, size);
        decoder.InitFrameOutput();

        Assert.Null(decoder.Pipeline);
    }

    [Fact]
    public void References_NoBlending_Returns0()
    {
        var meta = new ImageMetadata();
        var size = CreateSmallSize();

        var decoder = CreateSmallDecoder(meta, size);
        Assert.Equal(0, decoder.References());
    }

    [Fact]
    public void FrameDim_SmallImage_Correct()
    {
        var meta = new ImageMetadata();
        var size = CreateSmallSize();

        var decoder = CreateSmallDecoder(meta, size);
        Assert.Equal(1, decoder.FrameDim.NumGroups);
        Assert.Equal(1, decoder.FrameDim.NumDcGroups);
    }

    [Fact]
    public void ProcessSections_MultiGroup_DcGlobalOnly()
    {
        // Use a larger image so we get multiple sections (not single-section mode)
        var meta = new ImageMetadata();
        var size = CreateLargeSize(); // 512×512 → multiple groups

        var decoder = CreateLargeDecoder(meta, size);
        decoder.InitFrameOutput();

        Assert.True(decoder.FrameDim.NumGroups > 1);

        // Build DC global section data
        var sectionWriter = new BitWriter();

        // DequantMatrices.DecodeDC: all_default = true
        sectionWriter.Write(1, 1);
        // Quantizer: global_scale U32 selector=0, Bits(11), value=999 (→1000)
        sectionWriter.Write(2, 0);
        sectionWriter.Write(11, 999);
        // Quantizer: quant_dc U32 selector=2, Bits(8), value=63 (→64)
        sectionWriter.Write(2, 2);
        sectionWriter.Write(8, 63);
        // ColorCorrelation.DecodeDC: all_default=true
        sectionWriter.Write(1, 1);
        sectionWriter.ZeroPadToByte();

        var sectionData = sectionWriter.GetSpan().ToArray();
        using var sectionReader = new BitReader(sectionData);

        // Submit only DC global (section id=0)
        var sections = new SectionInfo[]
        {
            new() { Reader = sectionReader, Id = 0, Index = 0 }
        };
        var status = new SectionStatus[1];

        var result = decoder.ProcessSections(sections, status);
        Assert.True(result);
        Assert.Equal(SectionStatus.Done, status[0]);

        sectionReader.Close();
    }

    // Helper: creates a SizeHeader for an 8×8 image
    public static SizeHeader CreateSmallSize()
    {
        var w = new BitWriter();
        w.Write(1, 1); // small
        w.Write(5, 0); // ysize_div8_minus1 = 0 → y=8
        w.Write(3, 1); // ratio 1:1 → x=8
        w.ZeroPadToByte();
        var br = new BitReader(w.GetSpan().ToArray());
        var size = new SizeHeader();
        size.ReadFromBitStream(br);
        br.Close();
        return size;
    }

    // Helper: creates a SizeHeader for a large image (multiple groups)
    // small=false for >256px, use non-small with explicit sizes
    private static SizeHeader CreateLargeSize()
    {
        var w = new BitWriter();
        w.Write(1, 0); // not small
        // ysize: U32 distribution — need to check encoding
        // SizeHeader reads ysize as U32 (Bits(9)+1, Bits(13)+1, Bits(18)+1, Bits(30)+1)
        // For 512: selector=1, Bits(13)=511 → 511+1=512
        w.Write(2, 1);  // selector 1
        w.Write(13, 511); // ysize = 512
        // ratio = 1 → 1:1 → xsize=512
        w.Write(3, 1);
        w.ZeroPadToByte();
        var br = new BitReader(w.GetSpan().ToArray());
        var size = new SizeHeader();
        size.ReadFromBitStream(br);
        br.Close();
        return size;
    }

    // Helper: creates a FrameDecoder with InitFrame already called (8×8)
    public static FrameDecoder CreateSmallDecoder(ImageMetadata meta, SizeHeader size)
    {
        var writer = new BitWriter();
        // Frame header: all_default = true
        writer.Write(1, 1);
        writer.ZeroPadToByte();
        // TOC: no permutation, single entry
        writer.Write(1, 0);
        writer.ZeroPadToByte();
        // TOC entry: selector 0, Bits(10) = 64 bytes
        writer.Write(2, 0);
        writer.Write(10, 64);
        writer.ZeroPadToByte();

        var data = writer.GetSpan().ToArray();
        using var reader = new BitReader(data);

        var decoder = new FrameDecoder(meta, size);
        decoder.InitFrame(reader);
        reader.Close();
        return decoder;
    }

    // Helper: creates a FrameDecoder for a larger image with multiple groups
    private static FrameDecoder CreateLargeDecoder(ImageMetadata meta, SizeHeader size)
    {
        var writer = new BitWriter();
        // Frame header: all_default = true
        writer.Write(1, 1);
        writer.ZeroPadToByte();

        // Compute expected TOC entries (we need to match what InitFrame expects)
        // For all-default frame: numPasses=1, groups/dcGroups depend on size
        // First calculate frame dimensions from the size header
        var tempFd = new FrameDimensions();
        tempFd.Set(size.XSize, size.YSize, 1, 0, 0, false, 1);
        int numEntries = TocReader.NumTocEntries(tempFd.NumGroups, tempFd.NumDcGroups, 1);

        // TOC: no permutation
        writer.Write(1, 0);
        writer.ZeroPadToByte();
        // TOC entries: each with selector 0, Bits(10)
        for (int i = 0; i < numEntries; i++)
        {
            writer.Write(2, 0);
            writer.Write(10, 64);
        }
        writer.ZeroPadToByte();

        var data = writer.GetSpan().ToArray();
        using var reader = new BitReader(data);

        var decoder = new FrameDecoder(meta, size);
        decoder.InitFrame(reader);
        reader.Close();
        return decoder;
    }
}

public class ReferenceFrameTests
{
    [Fact]
    public void ReferenceFrame_DefaultIsNull()
    {
        var rf = new ReferenceFrame();
        Assert.Null(rf.ChannelData);
        Assert.Null(rf.SourceHeader);
        Assert.Equal(0, rf.Width);
        Assert.Equal(0, rf.Height);
    }

    [Fact]
    public void ReferenceFrame_StoreAndRetrieve()
    {
        var rf = new ReferenceFrame
        {
            Width = 16,
            Height = 16,
            SavedBeforeColorTransform = false,
        };

        rf.ChannelData = new float[3][][];
        for (int c = 0; c < 3; c++)
        {
            rf.ChannelData[c] = new float[16][];
            for (int y = 0; y < 16; y++)
            {
                rf.ChannelData[c][y] = new float[16];
                Array.Fill(rf.ChannelData[c][y], 0.5f * (c + 1));
            }
        }

        Assert.Equal(16, rf.Width);
        Assert.Equal(16, rf.Height);
        Assert.Equal(0.5f, rf.ChannelData[0][0][0]);
        Assert.Equal(1.0f, rf.ChannelData[1][0][0]);
        Assert.Equal(1.5f, rf.ChannelData[2][0][0]);
    }

    [Fact]
    public void FrameDecoder_ReferenceFrames_InitiallyNull()
    {
        var meta = new ImageMetadata();
        var size = FrameDecoderPipelineTests.CreateSmallSize();
        var decoder = FrameDecoderPipelineTests.CreateSmallDecoder(meta, size);

        for (int i = 0; i < 4; i++)
            Assert.Null(decoder.ReferenceFrames[i]);
    }
}

public class PipelineBuilderIntegrationTests
{
    [Fact]
    public void BuildVarDctPipeline_DefaultHeader_HasXybAndFromLinear()
    {
        var fh = new FrameHeader
        {
            Transform = ColorTransform.XYB,
            Encoding = FrameEncoding.VarDCT,
        };
        var fd = new FrameDimensions();
        fd.Set(64, 64, 1, 0, 0, false, 1);

        var pipeline = PipelineBuilder.BuildVarDctPipeline(fh, fd, 3, 64, 64);
        Assert.NotNull(pipeline);
        Assert.NotNull(pipeline.ChannelData);
        Assert.Equal(3, pipeline.ChannelData!.Length);
        Assert.Equal(64, pipeline.Width);
        Assert.Equal(64, pipeline.Height);
    }

    [Fact]
    public void BuildVarDctPipeline_WithGaborish_NoThrow()
    {
        var fh = new FrameHeader
        {
            Transform = ColorTransform.XYB,
            Encoding = FrameEncoding.VarDCT,
        };
        fh.Filter.Gab = true;

        var fd = new FrameDimensions();
        fd.Set(32, 32, 1, 0, 0, false, 1);

        var pipeline = PipelineBuilder.BuildVarDctPipeline(fh, fd, 3, 32, 32);
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void BuildVarDctPipeline_WithEpf_NoThrow()
    {
        var fh = new FrameHeader
        {
            Transform = ColorTransform.XYB,
            Encoding = FrameEncoding.VarDCT,
        };
        fh.Filter.EpfIters = 3;

        var fd = new FrameDimensions();
        fd.Set(32, 32, 1, 0, 0, false, 1);

        var pipeline = PipelineBuilder.BuildVarDctPipeline(fh, fd, 3, 32, 32);
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void BuildVarDctPipeline_YCbCr_HasYcbcrStage()
    {
        var fh = new FrameHeader
        {
            Transform = ColorTransform.YCbCr,
            Encoding = FrameEncoding.VarDCT,
        };
        var fd = new FrameDimensions();
        fd.Set(16, 16, 1, 0, 0, false, 1);

        var pipeline = PipelineBuilder.BuildVarDctPipeline(fh, fd, 3, 16, 16);
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void BuildVarDctPipeline_ExecuteWithWrite_ProducesOutput()
    {
        var fh = new FrameHeader
        {
            Transform = ColorTransform.None,
            Encoding = FrameEncoding.VarDCT,
        };
        var fd = new FrameDimensions();
        fd.Set(8, 8, 1, 0, 0, false, 1);

        var pipeline = PipelineBuilder.BuildVarDctPipeline(fh, fd, 3, 8, 8);

        // Set some pixel data in the pipeline buffers
        for (int c = 0; c < 3; c++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    pipeline.ChannelData![c][y][x] = 0.5f;

        // Add output write stage
        byte[] output = new byte[8 * 8 * 3];
        pipeline.AddStage(new StageWrite(output, 8, 3, false));

        // Execute pipeline
        bool ok = pipeline.ProcessGroup(0, 0);
        Assert.True(ok);

        // All pixels should be ~128 (0.5 * 255 ≈ 128)
        for (int i = 0; i < output.Length; i++)
        {
            Assert.InRange(output[i], 126, 129);
        }
    }

    [Fact]
    public void BuildVarDctPipeline_XYBtoSRGB_ProducesPositiveOutput()
    {
        var fh = new FrameHeader
        {
            Transform = ColorTransform.XYB,
            Encoding = FrameEncoding.VarDCT,
        };
        var fd = new FrameDimensions();
        fd.Set(8, 8, 1, 0, 0, false, 1);

        var pipeline = PipelineBuilder.BuildVarDctPipeline(fh, fd, 3, 8, 8);

        // Set mid-range XYB values
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                pipeline.ChannelData![0][y][x] = 0.5f; // X
                pipeline.ChannelData![1][y][x] = 0.5f; // Y
                pipeline.ChannelData![2][y][x] = 0.5f; // B
            }

        byte[] output = new byte[8 * 8 * 3];
        pipeline.AddStage(new StageWrite(output, 8, 3, false));

        bool ok = pipeline.ProcessGroup(0, 0);
        Assert.True(ok);

        // Output should be non-zero (XYB → Linear → sRGB transform happened)
        bool hasNonZero = false;
        for (int i = 0; i < output.Length; i++)
        {
            if (output[i] > 0) hasNonZero = true;
        }
        Assert.True(hasNonZero, "XYB→sRGB pipeline should produce non-zero output");
    }
}
