// Port of SizeHeader from lib/jxl/headers.h — image size header
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;

namespace LibJxl.Fields;

/// <summary>
/// Image size header. Encodes image width and height with support for
/// small images, aspect ratios, and large dimensions.
/// Port of jxl::SizeHeader.
/// </summary>
public class SizeHeader
{
    private bool _small;
    private uint _ysizeDiv8Minus1;
    private uint _ysize;
    private uint _ratio;
    private uint _xsizeDiv8Minus1;
    private uint _xsize;

    // Aspect ratio table: index -> (num, denom) where xsize = ysize * num / denom
    private static readonly (int num, int denom)[] AspectRatios = [
        (0, 0),    // 0: custom
        (1, 1),    // 1: 1:1
        (12, 10),  // 2: 12:10
        (4, 3),    // 3: 4:3
        (3, 2),    // 4: 3:2
        (16, 9),   // 5: 16:9
        (5, 4),    // 6: 5:4
        (2, 1),    // 7: 2:1
    ];

    public int XSize
    {
        get
        {
            if (_ratio != 0)
            {
                var (num, denom) = AspectRatios[_ratio];
                return (int)((YSize * (long)num + denom - 1) / denom);
            }
            return _small ? (int)((_xsizeDiv8Minus1 + 1) * 8) : (int)_xsize;
        }
    }

    public int YSize => _small ? (int)((_ysizeDiv8Minus1 + 1) * 8) : (int)_ysize;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        _small = FieldReader.ReadBool(br);

        if (_small)
        {
            _ysizeDiv8Minus1 = (uint)br.ReadBits(5);
        }
        else
        {
            _ysize = FieldReader.ReadU32(br,
                U32Distr.BitsOffset(9, 1),
                U32Distr.BitsOffset(13, 1),
                U32Distr.BitsOffset(18, 1),
                U32Distr.BitsOffset(30, 1));
        }

        _ratio = (uint)br.ReadBits(3);

        if (_ratio == 0)
        {
            if (_small)
            {
                _xsizeDiv8Minus1 = (uint)br.ReadBits(5);
            }
            else
            {
                _xsize = FieldReader.ReadU32(br,
                    U32Distr.BitsOffset(9, 1),
                    U32Distr.BitsOffset(13, 1),
                    U32Distr.BitsOffset(18, 1),
                    U32Distr.BitsOffset(30, 1));
            }
        }

        return true;
    }
}
