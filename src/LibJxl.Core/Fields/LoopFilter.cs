// Port of LoopFilter from lib/jxl/loop_filter.h/cc
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;

namespace LibJxl.Fields;

/// <summary>
/// Loop filter parameters (Gaborish convolution + Edge-Preserving Filter).
/// Port of jxl::LoopFilter.
/// </summary>
public class LoopFilter
{
    public bool AllDefault;

    // Gaborish convolution
    public bool Gab = true;
    public bool GabCustom;
    public float GabXWeight1 = 1.1f * 0.104699568f;
    public float GabXWeight2 = 1.1f * 0.055680538f;
    public float GabYWeight1 = 1.1f * 0.104699568f;
    public float GabYWeight2 = 1.1f * 0.055680538f;
    public float GabBWeight1 = 1.1f * 0.104699568f;
    public float GabBWeight2 = 1.1f * 0.055680538f;

    // Edge-preserving filter
    public uint EpfIters = 2;
    public bool EpfSharpCustom;
    public float[] EpfSharpLut = new float[8];
    public bool EpfWeightCustom;
    public float[] EpfChannelScale = [40.0f, 5.0f, 3.5f];
    public float EpfPass1Zeroflush = 0.45f;
    public float EpfPass2Zeroflush = 0.6f;
    public bool EpfSigmaCustom;
    public float EpfQuantMul = 0.46f;
    public float EpfPass0SigmaScale = 0.9f;
    public float EpfPass2SigmaScale = 6.5f;
    public float EpfBorderSadMul = 2.0f / 3.0f;
    public float EpfSigmaForModular = 1.0f;

    public ulong Extensions;

    // Set by caller before reading
    public bool NonserializedIsModular;

    public int Padding
    {
        get
        {
            int[] paddingPerIter = [0, 2, 3, 6];
            return paddingPerIter[EpfIters] + (Gab ? 1 : 0);
        }
    }

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        // AllDefault check
        AllDefault = FieldReader.ReadBool(br);
        if (AllDefault) return true;

        // Gaborish
        Gab = FieldReader.ReadBool(br);
        if (Gab)
        {
            GabCustom = FieldReader.ReadBool(br);
            if (GabCustom)
            {
                F16Coder.Read(br, out GabXWeight1);
                F16Coder.Read(br, out GabXWeight2);
                F16Coder.Read(br, out GabYWeight1);
                F16Coder.Read(br, out GabYWeight2);
                F16Coder.Read(br, out GabBWeight1);
                F16Coder.Read(br, out GabBWeight2);
            }
        }

        // EPF
        EpfIters = (uint)br.ReadBits(2);

        if (EpfIters > 0)
        {
            if (!NonserializedIsModular)
            {
                EpfSharpCustom = FieldReader.ReadBool(br);
                if (EpfSharpCustom)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        F16Coder.Read(br, out EpfSharpLut[i]);
                    }
                }
            }

            EpfWeightCustom = FieldReader.ReadBool(br);
            if (EpfWeightCustom)
            {
                F16Coder.Read(br, out EpfChannelScale[0]);
                F16Coder.Read(br, out EpfChannelScale[1]);
                F16Coder.Read(br, out EpfChannelScale[2]);
                F16Coder.Read(br, out EpfPass1Zeroflush);
                F16Coder.Read(br, out EpfPass2Zeroflush);
            }

            EpfSigmaCustom = FieldReader.ReadBool(br);
            if (EpfSigmaCustom)
            {
                if (!NonserializedIsModular)
                    F16Coder.Read(br, out EpfQuantMul);
                F16Coder.Read(br, out EpfPass0SigmaScale);
                F16Coder.Read(br, out EpfPass2SigmaScale);
                F16Coder.Read(br, out EpfBorderSadMul);
            }

            if (NonserializedIsModular)
                F16Coder.Read(br, out EpfSigmaForModular);
        }

        // Extensions
        Extensions = U64Coder.Read(br);
        // Skip unknown extensions if any
        return true;
    }
}
