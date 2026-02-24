// Port of lib/jxl/base/rect.h — rectangular region
using System.Runtime.CompilerServices;

namespace LibJxl.Base;

/// <summary>
/// Rectangular region in image(s). Port of jxl::Rect from rect.h.
/// </summary>
public readonly struct Rect
{
    public readonly int X0;
    public readonly int Y0;
    public readonly int XSize;
    public readonly int YSize;

    public Rect(int x0, int y0, int xsize, int ysize)
    {
        X0 = x0;
        Y0 = y0;
        XSize = xsize;
        YSize = ysize;
    }

    /// <summary>Clamped constructor: size is min(size_max, end - begin).</summary>
    public Rect(int xbegin, int ybegin, int xsize_max, int ysize_max, int xend, int yend)
    {
        X0 = xbegin;
        Y0 = ybegin;
        XSize = ClampedSize(xbegin, xsize_max, xend);
        YSize = ClampedSize(ybegin, ysize_max, yend);
    }

    public int X1
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => X0 + XSize;
    }

    public int Y1
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Y0 + YSize;
    }

    public Rect Intersection(Rect other)
    {
        int newX0 = Math.Max(X0, other.X0);
        int newY0 = Math.Max(Y0, other.Y0);
        int newX1 = Math.Min(X1, other.X1);
        int newY1 = Math.Min(Y1, other.Y1);
        return new Rect(newX0, newY0,
            Math.Max(0, newX1 - newX0),
            Math.Max(0, newY1 - newY0));
    }

    public Rect Crop(int areaXSize, int areaYSize) =>
        Intersection(new Rect(0, 0, areaXSize, areaYSize));

    public Rect Translate(int xOffset, int yOffset) =>
        new(X0 + xOffset, Y0 + yOffset, XSize, YSize);

    public bool IsInside(Rect other) =>
        X0 >= other.X0 && X1 <= other.X1 && Y0 >= other.Y0 && Y1 <= other.Y1;

    public Rect ShiftLeft(int shiftx, int shifty) =>
        new(X0 * (1 << shiftx), Y0 * (1 << shifty), XSize << shiftx, YSize << shifty);

    public Rect ShiftLeft(int shift) => ShiftLeft(shift, shift);

    public Rect Extend(int border, Rect parent)
    {
        int newX0 = X0 > parent.X0 + border ? X0 - border : parent.X0;
        int newY0 = Y0 > parent.Y0 + border ? Y0 - border : parent.Y0;
        int newX1 = X1 + border > parent.X1 ? parent.X1 : X1 + border;
        int newY1 = Y1 + border > parent.Y1 ? parent.Y1 : Y1 + border;
        return new Rect(newX0, newY0, newX1 - newX0, newY1 - newY0);
    }

    public override string ToString() => $"[{X0}..{X1})x[{Y0}..{Y1})";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ClampedSize(int begin, int sizeMax, int end) =>
        (begin + sizeMax <= end) ? sizeMax : (end > begin ? end - begin : 0);
}
