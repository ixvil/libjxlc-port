// Port of lib/jxl/image.h — 3-channel planar image
using System.Runtime.CompilerServices;
using LibJxl.Base;

namespace LibJxl.Image;

/// <summary>
/// A 3-channel (e.g. RGB, XYB) planar image.
/// Port of jxl::Image3&lt;T&gt; from image.h.
/// </summary>
public sealed class Image3<T> where T : unmanaged
{
    private readonly Plane<T>[] _planes;

    public Image3(int xsize, int ysize)
    {
        _planes = new Plane<T>[3];
        for (int c = 0; c < 3; c++)
            _planes[c] = new Plane<T>(xsize, ysize);
    }

    private Image3(Plane<T> p0, Plane<T> p1, Plane<T> p2)
    {
        _planes = [p0, p1, p2];
    }

    public int XSize => _planes[0].XSize;
    public int YSize => _planes[0].YSize;

    /// <summary>Returns the plane at the given channel index.</summary>
    public Plane<T> this[int c]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _planes[c];
    }

    /// <summary>Returns a writable row of the given channel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> PlaneRow(int c, int y) => _planes[c].Row(y);

    /// <summary>Returns a readonly row of the given channel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<T> ConstPlaneRow(int c, int y) => _planes[c].ConstRow(y);

    /// <summary>Returns the plane row offset by a Rect.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> PlaneRow(int c, Rect rect, int y) => _planes[c].Row(rect, y);

    public static Image3<T> FromPlanes(Plane<T> p0, Plane<T> p1, Plane<T> p2) =>
        new(p0, p1, p2);
}
