// Port of lib/jxl/ans_common.h/cc — alias table for ANS
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LibJxl.Base;

namespace LibJxl.Entropy;

/// <summary>
/// An alias table implements a mapping from [0, ANS_TAB_SIZE) into
/// [0, ANS_MAX_ALPHABET_SIZE), used for fast ANS decoding.
/// Port of jxl::AliasTable from ans_common.h.
/// </summary>
public static class AliasTable
{
    public struct Symbol
    {
        public int Value;
        public int Offset;
        public int Freq;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Entry
    {
        public byte Cutoff;
        public byte RightValue;
        public ushort Freq0;
        public ushort Offsets1;
        public ushort Freq1XorFreq0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Symbol Lookup(Entry[] table, int value, int logEntrySize, int entrySizeMinus1)
    {
        return Lookup(table, value, logEntrySize, entrySizeMinus1, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Symbol Lookup(Entry[] table, int value, int logEntrySize, int entrySizeMinus1, int tableOffset)
    {
        int i = value >> logEntrySize;
        int pos = value & entrySizeMinus1;
        int idx = tableOffset + i;

        int cutoff = table[idx].Cutoff;
        int rightValue = table[idx].RightValue;
        int freq0 = table[idx].Freq0;

        bool greater = pos >= cutoff;

        int offsets1Or0 = greater ? table[idx].Offsets1 : 0;
        int freq1XorFreq0Or0 = greater ? table[idx].Freq1XorFreq0 : 0;

        Symbol s;
        s.Value = greater ? rightValue : i;
        s.Offset = offsets1Or0 + pos;
        s.Freq = freq0 ^ freq1XorFreq0Or0;
        return s;
    }

    /// <summary>Computes an alias table for a given distribution.</summary>
    public static JxlStatus InitAliasTable(int[] distribution, int logRange,
                                            int logAlphaSize, Entry[] a)
    {
        int range = 1 << logRange;
        int tableSize = 1 << logAlphaSize;

        if (tableSize > range) return new JxlStatus(StatusCode.GenericError);

        // Remove trailing zeros
        int distLen = distribution.Length;
        while (distLen > 0 && distribution[distLen - 1] == 0)
            distLen--;

        // Ensure valid table even for empty alphabet
        int[] dist;
        if (distLen == 0)
        {
            dist = [range];
            distLen = 1;
        }
        else
        {
            dist = distribution[..distLen];
        }

        if (distLen > tableSize)
            return new JxlStatus(StatusCode.GenericError);

        int entrySize = range >> logAlphaSize;
        int singleSymbol = -1;
        int sum = 0;

        for (int sym = 0; sym < distLen; sym++)
        {
            int v = dist[sym];
            sum += v;
            if (v == AnsParams.AnsTabSize)
            {
                if (singleSymbol != -1) return new JxlStatus(StatusCode.GenericError);
                singleSymbol = sym;
            }
        }

        if (sum != range) return new JxlStatus(StatusCode.GenericError);

        // Single-symbol special case
        if (singleSymbol != -1)
        {
            byte sym = (byte)singleSymbol;
            for (int i = 0; i < tableSize; i++)
            {
                a[i].RightValue = sym;
                a[i].Cutoff = 0;
                a[i].Offsets1 = (ushort)(entrySize * i);
                a[i].Freq0 = 0;
                a[i].Freq1XorFreq0 = AnsParams.AnsTabSize;
            }
            return true;
        }

        var underfullPosn = new List<int>();
        var overfullPosn = new List<int>();
        int[] cutoffs = new int[tableSize];

        for (int i = 0; i < distLen; i++)
        {
            cutoffs[i] = dist[i];
            if (cutoffs[i] > entrySize)
                overfullPosn.Add(i);
            else if (cutoffs[i] < entrySize)
                underfullPosn.Add(i);
        }
        for (int i = distLen; i < tableSize; i++)
        {
            cutoffs[i] = 0;
            underfullPosn.Add(i);
        }

        while (overfullPosn.Count > 0)
        {
            int overfullI = overfullPosn[^1];
            overfullPosn.RemoveAt(overfullPosn.Count - 1);
            if (underfullPosn.Count == 0) return new JxlStatus(StatusCode.GenericError);
            int underfullI = underfullPosn[^1];
            underfullPosn.RemoveAt(underfullPosn.Count - 1);

            int underfullBy = entrySize - cutoffs[underfullI];
            cutoffs[overfullI] -= underfullBy;
            a[underfullI].RightValue = (byte)overfullI;
            a[underfullI].Offsets1 = (ushort)cutoffs[overfullI];

            if (cutoffs[overfullI] < entrySize)
                underfullPosn.Add(overfullI);
            else if (cutoffs[overfullI] > entrySize)
                overfullPosn.Add(overfullI);
        }

        for (int i = 0; i < tableSize; i++)
        {
            if (cutoffs[i] == entrySize)
            {
                a[i].RightValue = (byte)i;
                a[i].Offsets1 = 0;
                a[i].Cutoff = 0;
            }
            else
            {
                a[i].Offsets1 = (ushort)(a[i].Offsets1 - cutoffs[i]);
                a[i].Cutoff = (byte)cutoffs[i];
            }

            int freq0Val = i < distLen ? dist[i] : 0;
            int i1 = a[i].RightValue;
            int freq1Val = i1 < distLen ? dist[i1] : 0;
            a[i].Freq0 = (ushort)freq0Val;
            a[i].Freq1XorFreq0 = (ushort)(freq1Val ^ freq0Val);
        }

        return true;
    }

    /// <summary>Returns population count precision bits for the given logcount.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetPopulationCountPrecision(int logcount, int shift)
    {
        int r = Math.Min(logcount,
            shift - ((AnsParams.AnsLogTabSize - logcount) >> 1));
        return r < 0 ? 0 : r;
    }

    /// <summary>Creates a flat histogram summing to totalCount.</summary>
    public static int[] CreateFlatHistogram(int length, int totalCount)
    {
        Debug.Assert(length > 0 && length <= totalCount);
        int count = totalCount / length;
        int[] result = new int[length];
        Array.Fill(result, count);
        int remainder = totalCount % length;
        for (int i = 0; i < remainder; i++)
            result[i]++;
        return result;
    }
}
