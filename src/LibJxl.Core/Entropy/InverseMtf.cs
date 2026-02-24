// Port of lib/jxl/inverse_mtf-inl.h — inverse move-to-front transform (scalar)
namespace LibJxl.Entropy;

/// <summary>
/// Inverse Move-to-Front transform for context map decoding.
/// Scalar port of jxl::InverseMoveToFrontTransform.
/// </summary>
public static class InverseMtf
{
    public static void InverseMoveToFrontTransform(byte[] v, int vLen)
    {
        byte[] mtf = new byte[256];
        for (int i = 0; i < 256; i++)
            mtf[i] = (byte)i;

        for (int i = 0; i < vLen; i++)
        {
            byte index = v[i];
            v[i] = mtf[index];
            if (index != 0)
                MoveToFront(mtf, index);
        }
    }

    private static void MoveToFront(byte[] v, byte index)
    {
        byte value = v[index];
        for (int i = index; i > 0; i--)
            v[i] = v[i - 1];
        v[0] = value;
    }
}
