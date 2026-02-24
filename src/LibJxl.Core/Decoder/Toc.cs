// Port of lib/jxl/toc.h/cc — Table of Contents reading
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;

namespace LibJxl.Decoder;

/// <summary>TOC entry — size and logical id of a section.</summary>
public struct TocEntry
{
    public int Size;
    public int Id;
}

/// <summary>
/// Reads the Table of Contents from the bitstream.
/// Port of jxl::ReadToc and jxl::ReadGroupOffsets.
/// </summary>
public static class TocReader
{
    // kTocDist: U32Enc(Bits(10), BitsOffset(14, 1024), BitsOffset(22, 17408), BitsOffset(30, 4211712))
    private static readonly U32Distr TocD0 = U32Distr.Bits(10);
    private static readonly U32Distr TocD1 = U32Distr.BitsOffset(14, 1024);
    private static readonly U32Distr TocD2 = U32Distr.BitsOffset(22, 17408);
    private static readonly U32Distr TocD3 = U32Distr.BitsOffset(30, 4211712);

    /// <summary>
    /// Computes the index of an AC group section in the TOC.
    /// </summary>
    public static int AcGroupIndex(int pass, int group, int numGroups, int numDcGroups)
    {
        return 2 + numDcGroups + pass * numGroups + group;
    }

    /// <summary>
    /// Computes the total number of TOC entries.
    /// </summary>
    public static int NumTocEntries(int numGroups, int numDcGroups, int numPasses)
    {
        if (numGroups == 1 && numPasses == 1) return 1;
        return AcGroupIndex(0, 0, numGroups, numDcGroups) + numGroups * numPasses;
    }

    /// <summary>
    /// Reads the TOC from the bitstream. Returns sizes and optional permutation.
    /// </summary>
    public static JxlStatus ReadToc(int tocEntries, BitReader reader,
                                     out uint[] sizes, out int[]? permutation)
    {
        sizes = Array.Empty<uint>();
        permutation = null;

        if (tocEntries > 65536)
            return false;

        if (tocEntries == 0)
            return false;

        sizes = new uint[tocEntries];

        // Check if there's a permutation
        if (reader.ReadFixedBits(1) == 1)
        {
            permutation = new int[tocEntries];
            var permStatus = PermutationDecoder.DecodePermutation(0, tocEntries, permutation, reader);
            if (!permStatus) return false;
        }

        reader.JumpToByteBoundary();

        // Read sizes using U32 kTocDist encoding
        for (int i = 0; i < tocEntries; i++)
        {
            sizes[i] = FieldReader.ReadU32(reader, TocD0, TocD1, TocD2, TocD3);
        }

        reader.JumpToByteBoundary();
        return true;
    }

    /// <summary>
    /// Reads group offsets from the TOC. Applies permutation if present.
    /// Returns offsets, sizes, and total size.
    /// </summary>
    public static JxlStatus ReadGroupOffsets(int tocEntries, BitReader reader,
                                              out long[] offsets, out uint[] sizes,
                                              out long totalSize)
    {
        offsets = Array.Empty<long>();
        totalSize = 0;

        var tocStatus = ReadToc(tocEntries, reader, out sizes, out int[]? permutation);
        if (!tocStatus) return false;

        offsets = new long[tocEntries];

        // Prefix sum
        long offset = 0;
        for (int i = 0; i < tocEntries; i++)
        {
            offsets[i] = offset;
            offset += sizes[i];
            if (offset < offsets[i]) // overflow check
                return false;
        }
        totalSize = offset;

        // Apply permutation if present
        if (permutation != null)
        {
            long[] permutedOffsets = new long[tocEntries];
            uint[] permutedSizes = new uint[tocEntries];
            for (int i = 0; i < tocEntries; i++)
            {
                permutedOffsets[i] = offsets[permutation[i]];
                permutedSizes[i] = sizes[permutation[i]];
            }
            offsets = permutedOffsets;
            sizes = permutedSizes;
        }

        return true;
    }
}
