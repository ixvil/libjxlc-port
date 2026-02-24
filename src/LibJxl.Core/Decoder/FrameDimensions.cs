// Port of lib/jxl/frame_dimensions.h — frame dimensions and block/group constants
using LibJxl.Base;

namespace LibJxl.Decoder;

/// <summary>Block and group dimension constants for the decoder.</summary>
public static class FrameConstants
{
    public const int BlockDim = 8;
    public const int DCTBlockSize = BlockDim * BlockDim; // 64
    public const int GroupDim = 256;
    public const int GroupDimInBlocks = GroupDim / BlockDim; // 32
    public const int MaxNumPasses = 11;
}

/// <summary>
/// Dimensions of a frame, computed from FrameHeader.
/// Port of jxl::FrameDimensions.
/// </summary>
public class FrameDimensions
{
    /// <summary>Image size without upsampling (original / upsampling).</summary>
    public int XSize;
    public int YSize;

    /// <summary>Original image size.</summary>
    public int XSizeUpsampled;
    public int YSizeUpsampled;

    /// <summary>After upsampling the padded image.</summary>
    public int XSizeUpsampledPadded;
    public int YSizeUpsampledPadded;

    /// <summary>After padding to multiple of BlockDim (VarDCT mode).</summary>
    public int XSizePadded;
    public int YSizePadded;

    /// <summary>In BlockDim blocks.</summary>
    public int XSizeBlocks;
    public int YSizeBlocks;

    /// <summary>In number of groups.</summary>
    public int XSizeGroups;
    public int YSizeGroups;

    /// <summary>In number of DC groups.</summary>
    public int XSizeDcGroups;
    public int YSizeDcGroups;

    public int NumGroups;
    public int NumDcGroups;

    /// <summary>Size of a group in pixels.</summary>
    public int GroupDimValue;
    public int DcGroupDim;

    public void Set(int xsizePx, int ysizePx, int groupSizeShift,
                    int maxHShift, int maxVShift, bool modularMode, int upsampling)
    {
        GroupDimValue = (FrameConstants.GroupDim >> 1) << groupSizeShift;
        DcGroupDim = GroupDimValue * FrameConstants.BlockDim;
        XSizeUpsampled = xsizePx;
        YSizeUpsampled = ysizePx;
        XSize = JxlConstants.DivCeil(xsizePx, upsampling);
        YSize = JxlConstants.DivCeil(ysizePx, upsampling);
        XSizeBlocks = JxlConstants.DivCeil(XSize, FrameConstants.BlockDim << maxHShift) << maxHShift;
        YSizeBlocks = JxlConstants.DivCeil(YSize, FrameConstants.BlockDim << maxVShift) << maxVShift;
        XSizePadded = XSizeBlocks * FrameConstants.BlockDim;
        YSizePadded = YSizeBlocks * FrameConstants.BlockDim;
        if (modularMode)
        {
            XSizePadded = XSize;
            YSizePadded = YSize;
        }
        XSizeUpsampledPadded = XSizePadded * upsampling;
        YSizeUpsampledPadded = YSizePadded * upsampling;
        XSizeGroups = JxlConstants.DivCeil(XSize, GroupDimValue);
        YSizeGroups = JxlConstants.DivCeil(YSize, GroupDimValue);
        XSizeDcGroups = JxlConstants.DivCeil(XSizeBlocks, GroupDimValue);
        YSizeDcGroups = JxlConstants.DivCeil(YSizeBlocks, GroupDimValue);
        NumGroups = XSizeGroups * YSizeGroups;
        NumDcGroups = XSizeDcGroups * YSizeDcGroups;
    }

    public Rect GroupRect(int groupIndex)
    {
        int gx = groupIndex % XSizeGroups;
        int gy = groupIndex / XSizeGroups;
        return new Rect(gx * GroupDimValue, gy * GroupDimValue,
                        GroupDimValue, GroupDimValue, XSize, YSize);
    }

    public Rect BlockGroupRect(int groupIndex)
    {
        int gx = groupIndex % XSizeGroups;
        int gy = groupIndex / XSizeGroups;
        return new Rect(gx * (GroupDimValue >> 3), gy * (GroupDimValue >> 3),
                        GroupDimValue >> 3, GroupDimValue >> 3,
                        XSizeBlocks, YSizeBlocks);
    }

    public Rect DCGroupRect(int groupIndex)
    {
        int gx = groupIndex % XSizeDcGroups;
        int gy = groupIndex / XSizeDcGroups;
        return new Rect(gx * GroupDimValue, gy * GroupDimValue,
                        GroupDimValue, GroupDimValue,
                        XSizeBlocks, YSizeBlocks);
    }
}
