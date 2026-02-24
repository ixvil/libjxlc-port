// Port of lib/jxl/image.h — 2D planar image
using System.Diagnostics;
using System.Runtime.CompilerServices;
using LibJxl.Base;

namespace LibJxl.Image;

/// <summary>
/// A 2D array of T values arranged in rows. SIMD-friendly row access.
/// Port of jxl::Plane&lt;T&gt; from image.h.
/// </summary>
public sealed class Plane<T> where T : unmanaged
{
    private readonly T[] _data;
    private readonly int _xsize;
    private readonly int _ysize;
    private readonly int _stride; // elements per row (may include padding)

    public Plane(int xsize, int ysize)
    {
        _xsize = xsize;
        _ysize = ysize;
        // Align stride to 64 bytes for SIMD
        int bytesPerElement = Unsafe.SizeOf<T>();
        int minBytesPerRow = xsize * bytesPerElement;
        int alignedBytesPerRow = (minBytesPerRow + 63) & ~63;
        _stride = alignedBytesPerRow / bytesPerElement;
        _data = new T[_stride * ysize];
    }

    private Plane(T[] data, int xsize, int ysize, int stride)
    {
        _data = data;
        _xsize = xsize;
        _ysize = ysize;
        _stride = stride;
    }

    public int XSize => _xsize;
    public int YSize => _ysize;
    public int Stride => _stride;
    public int BytesPerRow => _stride * Unsafe.SizeOf<T>();

    /// <summary>Returns a writable span of the given row.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> Row(int y)
    {
        Debug.Assert(y >= 0 && y < _ysize);
        return _data.AsSpan(y * _stride, _xsize);
    }

    /// <summary>Returns a readonly span of the given row.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<T> ConstRow(int y)
    {
        Debug.Assert(y >= 0 && y < _ysize);
        return _data.AsSpan(y * _stride, _xsize);
    }

    /// <summary>Returns a writable row for the Rect-offset position.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> Row(Rect rect, int y)
    {
        int absY = rect.Y0 + y;
        Debug.Assert(absY >= 0 && absY < _ysize);
        return _data.AsSpan(absY * _stride + rect.X0, rect.XSize);
    }

    /// <summary>Gets/sets a single pixel.</summary>
    public ref T this[int y, int x]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(x >= 0 && x < _xsize && y >= 0 && y < _ysize);
            return ref _data[y * _stride + x];
        }
    }

    /// <summary>Shrinks the visible size (data unchanged).</summary>
    public Plane<T> ShrinkTo(int xsize, int ysize)
    {
        Debug.Assert(xsize <= _xsize && ysize <= _ysize);
        return new Plane<T>(_data, xsize, ysize, _stride);
    }

    /// <summary>Fills all elements with the given value.</summary>
    public void Fill(T value)
    {
        _data.AsSpan().Fill(value);
    }

    /// <summary>Returns the raw backing array (for interop).</summary>
    public T[] RawData => _data;
}
