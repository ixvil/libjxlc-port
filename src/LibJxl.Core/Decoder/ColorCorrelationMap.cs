// Port of lib/jxl/chroma_from_luma.h — Chroma from Luma correlation
using LibJxl.Bitstream;

namespace LibJxl.Decoder;

/// <summary>
/// Color correlation parameters for chroma-from-luma (CfL) prediction.
/// Port of jxl::ColorCorrelation from chroma_from_luma.h.
/// </summary>
public class ColorCorrelation
{
    public const int kColorTileDim = 64;
    public const int kColorTileDimInBlocks = kColorTileDim / 8;
    public const byte kDefaultColorFactor = 84;
    public const int kCFLFixedPointPrecision = 11;

    private float _colorScale;
    private float _baseCorrelationX;
    private float _baseCorrelationB;
    private int _ytoxDc;
    private int _ytobDc;
    private uint _colorFactor = kDefaultColorFactor;

    public ColorCorrelation()
    {
        SetColorFactor(kDefaultColorFactor);
    }

    public float ColorScale => _colorScale;
    public float BaseCorrelationX => _baseCorrelationX;
    public float BaseCorrelationB => _baseCorrelationB;
    public int YtoXDc => _ytoxDc;
    public int YtoBDc => _ytobDc;
    public uint ColorFactor => _colorFactor;

    /// <summary>Compute Y→X color ratio from tile factor.</summary>
    public float YtoXRatio(int xFactor)
    {
        return _baseCorrelationX + xFactor * _colorScale;
    }

    /// <summary>Compute Y→B color ratio from tile factor.</summary>
    public float YtoBRatio(int bFactor)
    {
        return _baseCorrelationB + bFactor * _colorScale;
    }

    /// <summary>
    /// Reads DC-level color correlation from bitstream.
    /// Port of ColorCorrelation::DecodeDC.
    /// </summary>
    public bool DecodeDC(BitReader br)
    {
        if (br.ReadBits(1) != 0) // use defaults
            return true;

        _colorFactor = (uint)br.ReadBits(2);
        switch (_colorFactor)
        {
            case 0: _colorFactor = (uint)br.ReadBits(8) + 1; break;
            case 1: _colorFactor = 256; break;
            case 2: _colorFactor = kDefaultColorFactor; break;
            case 3: _colorFactor = 256; break; // full range
        }
        SetColorFactor(_colorFactor);

        // base_correlation_x: signed, 8 bits → range [-1.5, +1.5]
        int bx = (int)br.ReadBits(8) - 128;
        _baseCorrelationX = bx / 128.0f;

        // base_correlation_b: signed, 8 bits
        int bb = (int)br.ReadBits(8) - 128;
        _baseCorrelationB = bb / 128.0f + 1.0f;

        // ytox_dc, ytob_dc
        _ytoxDc = (int)br.ReadBits(8) - 128;
        _ytobDc = (int)br.ReadBits(8) - 128;

        return true;
    }

    public bool IsJPEGCompatible()
    {
        return _ytoxDc == 0 && _ytobDc == 0;
    }

    private void SetColorFactor(uint colorFactor)
    {
        _colorFactor = colorFactor;
        _colorScale = 1.0f / colorFactor;
        _baseCorrelationX = 0.0f;
        _baseCorrelationB = 1.0f;
        _ytoxDc = 0;
        _ytobDc = 0;
    }
}

/// <summary>
/// Color correlation map storing per-tile X/B factors.
/// Port of jxl::ColorCorrelationMap from chroma_from_luma.h.
/// </summary>
public class ColorCorrelationMap
{
    private readonly ColorCorrelation _base = new();
    private sbyte[,]? _ytoxMap; // [row][col] tile factors
    private sbyte[,]? _ytobMap;

    public ColorCorrelation Base => _base;
    public sbyte[,]? YtoxMap => _ytoxMap;
    public sbyte[,]? YtobMap => _ytobMap;

    /// <summary>Creates a map for the given image size.</summary>
    public void Create(int xsize, int ysize)
    {
        int tilesX = (xsize + ColorCorrelation.kColorTileDim - 1) / ColorCorrelation.kColorTileDim;
        int tilesY = (ysize + ColorCorrelation.kColorTileDim - 1) / ColorCorrelation.kColorTileDim;
        _ytoxMap = new sbyte[tilesY, tilesX];
        _ytobMap = new sbyte[tilesY, tilesX];
    }

    /// <summary>Reads DC color correlation from bitstream.</summary>
    public bool DecodeDC(BitReader br)
    {
        return _base.DecodeDC(br);
    }
}
