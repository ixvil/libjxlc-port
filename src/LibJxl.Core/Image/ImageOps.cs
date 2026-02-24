// Port of lib/jxl/image_ops.h — image utility operations
using LibJxl.Base;

namespace LibJxl.Image;

/// <summary>Common image operations: copy, fill, compare.</summary>
public static class ImageOps
{
    /// <summary>Copies src plane into dst plane.</summary>
    public static void CopyImageTo<T>(Plane<T> src, Plane<T> dst) where T : unmanaged
    {
        int rows = Math.Min(src.YSize, dst.YSize);
        int cols = Math.Min(src.XSize, dst.XSize);
        for (int y = 0; y < rows; y++)
        {
            src.ConstRow(y)[..cols].CopyTo(dst.Row(y));
        }
    }

    /// <summary>Copies a rectangular region from src to dst at (0,0).</summary>
    public static void CopyImageTo<T>(Plane<T> src, Rect srcRect, Plane<T> dst) where T : unmanaged
    {
        for (int y = 0; y < srcRect.YSize; y++)
        {
            src.Row(srcRect, y).CopyTo(dst.Row(y));
        }
    }

    /// <summary>Copies src Image3 into dst Image3.</summary>
    public static void CopyImageTo<T>(Image3<T> src, Image3<T> dst) where T : unmanaged
    {
        for (int c = 0; c < 3; c++)
            CopyImageTo(src[c], dst[c]);
    }

    /// <summary>Fills a plane with a given value.</summary>
    public static void FillImage<T>(Plane<T> plane, T value) where T : unmanaged
    {
        plane.Fill(value);
    }

    /// <summary>Checks if two planes have the same dimensions.</summary>
    public static bool SameSize<T>(Plane<T> a, Plane<T> b) where T : unmanaged =>
        a.XSize == b.XSize && a.YSize == b.YSize;
}
