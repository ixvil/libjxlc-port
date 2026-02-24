// Port of lib/jxl/modular/modular_image.h — Channel and Image types
using LibJxl.Image;

namespace LibJxl.Modular;

/// <summary>Pixel type used for modular channels (int32).</summary>
public static class ModularTypes
{
    public const int MaxBitDepth = 31;
}

/// <summary>
/// A single channel in a modular image.
/// Port of jxl::Channel.
/// </summary>
public class Channel
{
    public Plane<int> Pixels;
    public int W;
    public int H;
    public int HShift;
    public int VShift;
    public int Component = -1;

    private Channel(int w, int h, int hshift, int vshift)
    {
        W = w;
        H = h;
        HShift = hshift;
        VShift = vshift;
        Pixels = new Plane<int>(w, h);
    }

    public static Channel Create(int w, int h, int hshift = 0, int vshift = 0)
    {
        return new Channel(w, h, hshift, vshift);
    }

    /// <summary>Returns a pointer to the row data.</summary>
    public Span<int> Row(int y) => Pixels.Row(y);

    public void Shrink(int newW, int newH)
    {
        if (newW == W && newH == H) return;
        var newPlane = new Plane<int>(newW, newH);
        int copyW = Math.Min(W, newW);
        int copyH = Math.Min(H, newH);
        for (int y = 0; y < copyH; y++)
        {
            Pixels.Row(y).Slice(0, copyW).CopyTo(newPlane.Row(y));
        }
        Pixels = newPlane;
        W = newW;
        H = newH;
    }
}

/// <summary>
/// A modular image: a collection of channels with transforms.
/// Port of jxl::Image (modular).
/// </summary>
public class ModularImage
{
    public List<Channel> Channels = [];
    public List<Transform> Transforms = [];
    public int W;
    public int H;
    public int BitDepth;
    public int NbMetaChannels;
    public bool Error;

    private ModularImage() { }

    public static ModularImage Create(int w, int h, int bitdepth, int nbChans)
    {
        var img = new ModularImage
        {
            W = w,
            H = h,
            BitDepth = bitdepth,
        };
        for (int i = 0; i < nbChans; i++)
        {
            img.Channels.Add(Channel.Create(w, h));
        }
        return img;
    }

    /// <summary>Undo all transforms in reverse order.</summary>
    public void UndoTransforms(WeightedHeader wpHeader)
    {
        for (int i = Transforms.Count - 1; i >= 0; i--)
        {
            Transforms[i].Inverse(this, wpHeader);
        }
        Transforms.Clear();
    }
}
