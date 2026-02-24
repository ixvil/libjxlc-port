// Port of JXL enums used in headers and frame headers
namespace LibJxl.Fields;

public enum ColorSpace : uint
{
    RGB = 0,
    Grey = 1,
    XYB = 2,
    Unknown = 3,
}

public enum WhitePoint : uint
{
    D65 = 1,
    Custom = 2,
    E = 10,
    DCI = 11,
}

public enum Primaries : uint
{
    SRGB = 1,
    Custom = 2,
    BT2100 = 9,
    P3 = 11,
}

public enum TransferFunction : uint
{
    BT709 = 1,
    Unknown = 2,
    Linear = 8,
    SRGB = 13,
    PQ = 16,
    DCI = 17,
    HLG = 18,
}

public enum RenderingIntent : uint
{
    Perceptual = 0,
    Relative = 1,
    Saturation = 2,
    Absolute = 3,
}

public enum ExtraChannelType : uint
{
    Alpha = 0,
    Depth = 1,
    SpotColor = 2,
    SelectionMask = 3,
    Black = 4,
    CFA = 5,
    Thermal = 6,
    Unknown = 15,
    Optional = 16,
}

public enum FrameType : uint
{
    RegularFrame = 0,
    DCFrame = 1,
    ReferenceOnly = 2,
    SkipProgressive = 3,
}

public enum FrameEncoding : uint
{
    VarDCT = 0,
    Modular = 1,
}

public enum ColorTransform : uint
{
    XYB = 0,
    None = 1,
    YCbCr = 2,
}

public enum BlendMode : uint
{
    Replace = 0,
    Add = 1,
    Blend = 2,
    AlphaWeightedAdd = 3,
    Mul = 4,
}

/// <summary>Valid values lists for enum encoding.</summary>
public static class EnumValues
{
    public static readonly ColorSpace[] ColorSpaces =
        [ColorSpace.RGB, ColorSpace.Grey, ColorSpace.XYB, ColorSpace.Unknown];

    public static readonly WhitePoint[] WhitePoints =
        [WhitePoint.D65, WhitePoint.Custom, WhitePoint.E, WhitePoint.DCI];

    public static readonly Primaries[] PrimariesValues =
        [Primaries.SRGB, Primaries.Custom, Primaries.BT2100, Primaries.P3];

    public static readonly TransferFunction[] TransferFunctions =
        [TransferFunction.BT709, TransferFunction.Unknown, TransferFunction.Linear,
         TransferFunction.SRGB, TransferFunction.PQ, TransferFunction.DCI, TransferFunction.HLG];

    public static readonly RenderingIntent[] RenderingIntents =
        [RenderingIntent.Perceptual, RenderingIntent.Relative,
         RenderingIntent.Saturation, RenderingIntent.Absolute];

    public static readonly ExtraChannelType[] ExtraChannelTypes =
        [ExtraChannelType.Alpha, ExtraChannelType.Depth, ExtraChannelType.SpotColor,
         ExtraChannelType.SelectionMask, ExtraChannelType.Black, ExtraChannelType.CFA,
         ExtraChannelType.Thermal, ExtraChannelType.Unknown, ExtraChannelType.Optional];

    public static readonly FrameType[] FrameTypes =
        [FrameType.RegularFrame, FrameType.DCFrame,
         FrameType.ReferenceOnly, FrameType.SkipProgressive];

    public static readonly BlendMode[] BlendModes =
        [BlendMode.Replace, BlendMode.Add, BlendMode.Blend,
         BlendMode.AlphaWeightedAdd, BlendMode.Mul];
}
