// Port of lib/jxl/jpeg/dec_jpeg_data_writer.cc — JPEG reconstruction from JPEGData
// This is the core JPEG writer that serializes JPEGData back to a valid JPEG byte stream.

using System.Buffers.Binary;

namespace LibJxl.Jpeg;

/// <summary>
/// Huffman code table for JPEG encoding (encoding direction).
/// Port of HuffmanCodeTable from dec_jpeg_serialization_state.h.
/// </summary>
public class HuffmanCodeTable
{
    public sbyte[] Depth = new sbyte[256];
    public ushort[] Code = new ushort[256];
    public bool Initialized;

    public void InitDepths(sbyte value = 0)
    {
        Array.Fill(Depth, value);
    }
}

/// <summary>
/// Bit-level writer for JPEG entropy coding.
/// Port of JpegBitWriter from dec_jpeg_serialization_state.h.
/// </summary>
internal class JpegBitWriter
{
    public bool Healthy = true;
    public List<byte> Output;
    public ulong PutBuffer;
    public int PutBits;

    public JpegBitWriter(List<byte> output)
    {
        Output = output;
        PutBuffer = 0;
        PutBits = 64;
    }

    /// <summary>Write byte, with 0xFF byte stuffing.</summary>
    public void EmitByte(int b)
    {
        Output.Add((byte)b);
        if (b == 0xFF) Output.Add(0);
    }

    /// <summary>Discharge accumulated bits to output.</summary>
    public void DischargeBitBuffer(int nbits, ulong bits)
    {
        PutBuffer |= bits >> -PutBits;

        // Emit 8 bytes with 0xFF stuffing
        for (int i = 56; i >= 0; i -= 8)
            EmitByte((int)(PutBuffer >> i) & 0xFF);

        PutBits += 64;
        PutBuffer = bits << PutBits;
    }

    /// <summary>Write bits to the output stream.</summary>
    public void WriteBits(int nbits, ulong bits)
    {
        if (nbits <= 0) return;
        PutBits -= nbits;
        if (PutBits < 0)
        {
            if (nbits > 64)
            {
                PutBits += nbits;
                Healthy = false;
            }
            else
            {
                DischargeBitBuffer(nbits, bits);
            }
        }
        else
        {
            PutBuffer |= bits << PutBits;
        }
    }

    /// <summary>Emit a JPEG marker (0xFF + marker byte).</summary>
    public void EmitMarker(int marker)
    {
        Output.Add(0xFF);
        Output.Add((byte)marker);
    }

    /// <summary>Write a Huffman-coded symbol.</summary>
    public void WriteSymbol(int symbol, HuffmanCodeTable table)
    {
        WriteBits(table.Depth[symbol], table.Code[symbol]);
    }

    /// <summary>Write a Huffman-coded symbol followed by extra bits.</summary>
    public void WriteSymbolBits(int symbol, HuffmanCodeTable table, int nbits, ulong bits)
    {
        WriteBits(nbits + table.Depth[symbol],
                  bits | ((ulong)table.Code[symbol] << nbits));
    }

    /// <summary>
    /// Flush remaining bits to byte boundary.
    /// Port of JumpToByteBoundary.
    /// </summary>
    public bool JumpToByteBoundary(ref int padBitsPos, byte[]? padBits)
    {
        int nBits = PutBits & 7;
        byte padPattern;

        if (padBits == null || padBitsPos < 0)
        {
            padPattern = (byte)((1u << nBits) - 1);
        }
        else
        {
            padPattern = 0;
            byte danglingBits = 0;
            int src = padBitsPos;
            int remaining = nBits;
            while (remaining-- > 0)
            {
                padPattern <<= 1;
                if (src >= padBits.Length) return false;
                byte bit = padBits[src++];
                danglingBits |= bit;
                padPattern |= bit;
            }
            padBitsPos = src;
            if ((danglingBits & ~1) != 0) return false;
        }

        while (PutBits <= 56)
        {
            int c = (int)(PutBuffer >> 56) & 0xFF;
            EmitByte(c);
            PutBuffer <<= 8;
            PutBits += 8;
        }
        if (PutBits < 64)
        {
            int padMask = 0xFF >> (64 - PutBits);
            int c = ((int)(PutBuffer >> 56) & ~padMask) | padPattern;
            EmitByte(c);
        }
        PutBuffer = 0;
        PutBits = 64;
        return true;
    }
}

/// <summary>
/// Holds data buffered between blocks in progressive mode.
/// Port of DCTCodingState.
/// </summary>
internal class DCTCodingState
{
    public int EobRun;
    public HuffmanCodeTable? CurAcHuff;
    public List<ushort> RefinementBits = new(64);
    public int RefinementBitsCount;

    public void Init()
    {
        EobRun = 0;
        CurAcHuff = null;
        RefinementBits.Clear();
        RefinementBitsCount = 0;
    }

    /// <summary>Flush buffered EOB runs and refinement bits.</summary>
    public void Flush(JpegBitWriter bw)
    {
        if (EobRun > 0)
        {
            int nbits = FloorLog2(EobRun);
            int symbol = nbits << 4;
            bw.WriteSymbol(symbol, CurAcHuff!);
            if (nbits > 0)
                bw.WriteBits(nbits, (ulong)(EobRun & ((1 << nbits) - 1)));
            EobRun = 0;
        }

        int numWords = RefinementBitsCount >> 4;
        for (int i = 0; i < numWords; i++)
            bw.WriteBits(16, RefinementBits[i]);

        int tail = RefinementBitsCount & 0xF;
        if (tail > 0)
            bw.WriteBits(tail, RefinementBits[^1]);

        RefinementBits.Clear();
        RefinementBitsCount = 0;
    }

    /// <summary>Buffer end-of-band data.</summary>
    public void BufferEndOfBand(HuffmanCodeTable acHuff, int[]? newBitsArray,
                                int newBitsCount, JpegBitWriter bw)
    {
        if (EobRun == 0) CurAcHuff = acHuff;
        EobRun++;

        if (newBitsCount > 0 && newBitsArray != null)
        {
            ulong newBits = 0;
            for (int i = 0; i < newBitsCount; i++)
                newBits = (newBits << 1) | (uint)newBitsArray[i];

            int tail2 = RefinementBitsCount & 0xF;
            if (tail2 > 0)
            {
                int stuffCount = Math.Min(16 - tail2, newBitsCount);
                ushort stuffBits = (ushort)(newBits >> (newBitsCount - stuffCount));
                stuffBits &= (ushort)((1u << stuffCount) - 1);
                RefinementBits[^1] = (ushort)((RefinementBits[^1] << stuffCount) | stuffBits);
                newBitsCount -= stuffCount;
                RefinementBitsCount += stuffCount;
            }
            while (newBitsCount >= 16)
            {
                RefinementBits.Add((ushort)(newBits >> (newBitsCount - 16)));
                newBitsCount -= 16;
                RefinementBitsCount += 16;
            }
            if (newBitsCount > 0)
            {
                RefinementBits.Add((ushort)(newBits & ((1u << newBitsCount) - 1)));
                RefinementBitsCount += newBitsCount;
            }
        }

        if (EobRun == 0x7FFF) Flush(bw);
    }

    private static int FloorLog2(int val)
    {
        if (val <= 0) return 0;
        return 31 - int.LeadingZeroCount(val);
    }
}

/// <summary>
/// Writes a JPEGData structure to a JPEG byte stream.
/// Port of jxl::jpeg::WriteJpeg from dec_jpeg_data_writer.cc.
/// </summary>
public static class JpegWriter
{
    private const int kJpegPrecision = 8;

    /// <summary>
    /// Write complete JPEG byte stream from JPEGData.
    /// Returns the JPEG bytes or null on error.
    /// </summary>
    public static byte[]? WriteJpeg(JPEGData jpg)
    {
        var output = new List<byte>(65536);
        if (!WriteJpegInternal(jpg, output))
            return null;
        return output.ToArray();
    }

    /// <summary>
    /// Write complete JPEG byte stream to an Action callback.
    /// Port of jxl::jpeg::WriteJpeg with JPEGOutput.
    /// </summary>
    public static bool WriteJpeg(JPEGData jpg, Action<byte[], int, int> outputCallback)
    {
        var output = new List<byte>(65536);
        if (!WriteJpegInternal(jpg, output))
            return false;
        var arr = output.ToArray();
        outputCallback(arr, 0, arr.Length);
        return true;
    }

    private static bool WriteJpegInternal(JPEGData jpg, List<byte> output)
    {
        if (jpg.MarkerOrder.Count == 0) return false;

        var dcHuffTable = new HuffmanCodeTable[JpegConstants.kMaxHuffmanTables];
        var acHuffTable = new HuffmanCodeTable[JpegConstants.kMaxHuffmanTables];
        for (int i = 0; i < JpegConstants.kMaxHuffmanTables; i++)
        {
            dcHuffTable[i] = new HuffmanCodeTable();
            acHuffTable[i] = new HuffmanCodeTable();
        }

        int padBitsPos = -1;
        byte[]? padBits = null;
        if (jpg.HasZeroPaddingBit && jpg.PaddingBits.Count > 0)
        {
            padBits = jpg.PaddingBits.ToArray();
            padBitsPos = 0;
        }

        bool isProgressive = false;
        int dhtIndex = 0;
        int dqtIndex = 0;
        int appIndex = 0;
        int comIndex = 0;
        int dataIndex = 0;
        int scanIndex = 0;

        // SOI marker
        output.Add(0xFF);
        output.Add(0xD8);

        foreach (byte marker in jpg.MarkerOrder)
        {
            switch (marker)
            {
                case 0xC0:
                case 0xC1:
                case 0xC2:
                case 0xC9:
                case 0xCA:
                    if (!EncodeSOF(jpg, marker, output, ref isProgressive)) return false;
                    break;
                case 0xC4:
                    if (!EncodeDHT(jpg, ref dhtIndex, dcHuffTable, acHuffTable, output)) return false;
                    break;
                case 0xD0: case 0xD1: case 0xD2: case 0xD3:
                case 0xD4: case 0xD5: case 0xD6: case 0xD7:
                    output.Add(0xFF);
                    output.Add(marker);
                    break;
                case 0xD9:
                    output.Add(0xFF);
                    output.Add(0xD9);
                    if (jpg.TailData.Length > 0)
                        output.AddRange(jpg.TailData);
                    break;
                case 0xDA:
                    if (!EncodeScan(jpg, scanIndex, isProgressive,
                                    dcHuffTable, acHuffTable,
                                    ref padBitsPos, padBits, output))
                        return false;
                    scanIndex++;
                    break;
                case 0xDB:
                    if (!EncodeDQT(jpg, ref dqtIndex, output)) return false;
                    break;
                case 0xDD:
                    if (!EncodeDRI(jpg, output)) return false;
                    break;
                case >= 0xE0 and <= 0xEF:
                    if (appIndex >= jpg.AppData.Count) return false;
                    output.Add(0xFF);
                    output.AddRange(jpg.AppData[appIndex++]);
                    break;
                case 0xFE:
                    if (comIndex >= jpg.ComData.Count) return false;
                    output.Add(0xFF);
                    output.AddRange(jpg.ComData[comIndex++]);
                    break;
                case 0xFF: // inter-marker data
                    if (dataIndex >= jpg.InterMarkerData.Count) return false;
                    output.AddRange(jpg.InterMarkerData[dataIndex++]);
                    break;
                default:
                    return false;
            }
        }

        // Verify padding bits consumed
        if (padBits != null && padBitsPos >= 0 && padBitsPos != padBits.Length)
            return false;

        return true;
    }

    private static bool EncodeSOF(JPEGData jpg, byte marker, List<byte> output, ref bool isProgressive)
    {
        if (marker <= 0xC2) isProgressive = marker == 0xC2;

        int nComps = jpg.Components.Count;
        int markerLen = 8 + 3 * nComps;
        output.Add(0xFF);
        output.Add(marker);
        output.Add((byte)(markerLen >> 8));
        output.Add((byte)(markerLen & 0xFF));
        output.Add(kJpegPrecision);
        output.Add((byte)(jpg.Height >> 8));
        output.Add((byte)(jpg.Height & 0xFF));
        output.Add((byte)(jpg.Width >> 8));
        output.Add((byte)(jpg.Width & 0xFF));
        output.Add((byte)nComps);
        for (int i = 0; i < nComps; i++)
        {
            var c = jpg.Components[i];
            output.Add((byte)c.Id);
            output.Add((byte)((c.HSampFactor << 4) | c.VSampFactor));
            if (c.QuantIdx >= (uint)jpg.Quant.Count) return false;
            output.Add((byte)jpg.Quant[(int)c.QuantIdx].Index);
        }
        return true;
    }

    private static bool EncodeDHT(JPEGData jpg, ref int dhtIndex,
        HuffmanCodeTable[] dcHuffTable, HuffmanCodeTable[] acHuffTable,
        List<byte> output)
    {
        // Calculate marker length
        int markerLen = 2;
        for (int i = dhtIndex; i < jpg.HuffmanCode.Count; i++)
        {
            var huff = jpg.HuffmanCode[i];
            foreach (uint count in huff.Counts)
                markerLen += (int)count;
            if (markerLen == 2) break; // empty DHT
            markerLen += JpegConstants.kJpegHuffmanMaxBitLength;
            if (huff.IsLast) break;
        }

        output.Add(0xFF);
        output.Add(0xC4);
        output.Add((byte)(markerLen >> 8));
        output.Add((byte)(markerLen & 0xFF));

        while (true)
        {
            if (dhtIndex >= jpg.HuffmanCode.Count) return false;
            var huff = jpg.HuffmanCode[dhtIndex++];

            int index = huff.SlotId;
            uint totalCount = 0;
            int maxLength = 0;
            for (int i = 0; i < huff.Counts.Length; i++)
            {
                if (huff.Counts[i] != 0) maxLength = i;
                totalCount += huff.Counts[i];
            }
            if (totalCount == 0) break; // empty DHT

            HuffmanCodeTable huffTable;
            if ((index & 0x10) != 0)
            {
                index -= 0x10;
                huffTable = acHuffTable[index];
            }
            else
            {
                huffTable = dcHuffTable[index];
            }

            huffTable.InitDepths(127);
            if (!BuildHuffmanCodeTable(huff, huffTable)) return false;
            huffTable.Initialized = true;

            totalCount--; // exclude EOI symbol
            output.Add((byte)huff.SlotId);
            for (int i = 1; i <= JpegConstants.kJpegHuffmanMaxBitLength; i++)
                output.Add((byte)(i == maxLength ? huff.Counts[i] - 1 : huff.Counts[i]));
            for (uint i = 0; i < totalCount; i++)
                output.Add((byte)huff.Values[i]);

            if (huff.IsLast) break;
        }
        return true;
    }

    private static bool EncodeDQT(JPEGData jpg, ref int dqtIndex, List<byte> output)
    {
        int markerLen = 2;
        for (int i = dqtIndex; i < jpg.Quant.Count; i++)
        {
            var table = jpg.Quant[i];
            markerLen += 1 + (table.Precision != 0 ? 2 : 1) * JpegConstants.kDCTBlockSize;
            if (table.IsLast) break;
        }

        output.Add(0xFF);
        output.Add(0xDB);
        output.Add((byte)(markerLen >> 8));
        output.Add((byte)(markerLen & 0xFF));

        while (true)
        {
            if (dqtIndex >= jpg.Quant.Count) return false;
            var table = jpg.Quant[dqtIndex++];
            output.Add((byte)((table.Precision << 4) + table.Index));
            for (int i = 0; i < JpegConstants.kDCTBlockSize; i++)
            {
                int valIdx = (int)JpegConstants.kJPEGNaturalOrder[i];
                int val = table.Values[valIdx];
                if (table.Precision != 0)
                    output.Add((byte)(val >> 8));
                output.Add((byte)(val & 0xFF));
            }
            if (table.IsLast) break;
        }
        return true;
    }

    private static bool EncodeDRI(JPEGData jpg, List<byte> output)
    {
        output.Add(0xFF);
        output.Add(0xDD);
        output.Add(0);
        output.Add(4);
        output.Add((byte)(jpg.RestartInterval >> 8));
        output.Add((byte)(jpg.RestartInterval & 0xFF));
        return true;
    }

    private static bool EncodeSOS(JPEGData jpg, JPEGScanInfo scanInfo, List<byte> output)
    {
        int nScans = (int)scanInfo.NumComponents;
        int markerLen = 6 + 2 * nScans;
        output.Add(0xFF);
        output.Add(0xDA);
        output.Add((byte)(markerLen >> 8));
        output.Add((byte)(markerLen & 0xFF));
        output.Add((byte)nScans);
        for (int i = 0; i < nScans; i++)
        {
            var si = scanInfo.Components[i];
            if (si.CompIdx >= (uint)jpg.Components.Count) return false;
            output.Add((byte)jpg.Components[(int)si.CompIdx].Id);
            output.Add((byte)((si.DcTblIdx << 4) + si.AcTblIdx));
        }
        output.Add((byte)scanInfo.Ss);
        output.Add((byte)scanInfo.Se);
        output.Add((byte)((scanInfo.Ah << 4) | scanInfo.Al));
        return true;
    }

    private static bool EncodeScan(JPEGData jpg, int scanIndex, bool isProgressive,
        HuffmanCodeTable[] dcHuffTable, HuffmanCodeTable[] acHuffTable,
        ref int padBitsPos, byte[]? padBits, List<byte> output)
    {
        if (scanIndex >= jpg.ScanInfo.Count) return false;
        var scanInfo = jpg.ScanInfo[scanIndex];

        if (!EncodeSOS(jpg, scanInfo, output)) return false;

        var bw = new JpegBitWriter(output);
        var codingState = new DCTCodingState();
        codingState.Init();

        int restartInterval = (int)jpg.RestartInterval;
        int restartToGo = restartInterval;
        int nextRestartMarker = 0;
        short[] lastDcCoeff = new short[JpegConstants.kMaxComponents];

        jpg.CalculateMcuSize(scanInfo, out int mcusPerRow, out int mcuRows);

        int Al = isProgressive ? (int)scanInfo.Al : 0;
        int Ah = isProgressive ? (int)scanInfo.Ah : 0;
        int Ss = isProgressive ? (int)scanInfo.Ss : 0;
        int Se = isProgressive ? (int)scanInfo.Se : 63;
        bool isInterleaved = scanInfo.NumComponents > 1;

        int extraZeroRunsPos = 0;
        int resetPointPos = 0;
        int blockScanIndex = 0;

        int nextResetPoint = resetPointPos < scanInfo.ResetPoints.Count
            ? (int)scanInfo.ResetPoints[resetPointPos++] : -1;
        int nextExtraZeroRunIndex = extraZeroRunsPos < scanInfo.ExtraZeroRuns.Count
            ? (int)scanInfo.ExtraZeroRuns[extraZeroRunsPos].BlockIdx : -1;

        for (int mcuY = 0; mcuY < mcuRows; mcuY++)
        {
            for (int mcuX = 0; mcuX < mcusPerRow; mcuX++)
            {
                // Possibly emit restart marker
                if (restartInterval > 0 && restartToGo == 0)
                {
                    codingState.Flush(bw);
                    if (!bw.JumpToByteBoundary(ref padBitsPos, padBits))
                        return false;
                    bw.EmitMarker(0xD0 + nextRestartMarker);
                    nextRestartMarker = (nextRestartMarker + 1) & 7;
                    restartToGo = restartInterval;
                    Array.Clear(lastDcCoeff);
                }

                // Encode one MCU
                for (int i = 0; i < (int)scanInfo.NumComponents; i++)
                {
                    var si = scanInfo.Components[i];
                    var c = jpg.Components[(int)si.CompIdx];
                    var dcHuff = dcHuffTable[si.DcTblIdx];
                    var acHuff = acHuffTable[si.AcTblIdx];

                    int nBlocksY = isInterleaved ? c.VSampFactor : 1;
                    int nBlocksX = isInterleaved ? c.HSampFactor : 1;

                    for (int iy = 0; iy < nBlocksY; iy++)
                    {
                        for (int ix = 0; ix < nBlocksX; ix++)
                        {
                            int blockY = mcuY * nBlocksY + iy;
                            int blockX = mcuX * nBlocksX + ix;
                            int blockIdx = blockY * (int)c.WidthInBlocks + blockX;

                            if (blockScanIndex == nextResetPoint)
                            {
                                codingState.Flush(bw);
                                nextResetPoint = resetPointPos < scanInfo.ResetPoints.Count
                                    ? (int)scanInfo.ResetPoints[resetPointPos++] : -1;
                            }

                            int numZeroRuns = 0;
                            if (blockScanIndex == nextExtraZeroRunIndex)
                            {
                                numZeroRuns = (int)scanInfo.ExtraZeroRuns[extraZeroRunsPos].NumExtraZeroRuns;
                                extraZeroRunsPos++;
                                nextExtraZeroRunIndex = extraZeroRunsPos < scanInfo.ExtraZeroRuns.Count
                                    ? (int)scanInfo.ExtraZeroRuns[extraZeroRunsPos].BlockIdx : -1;
                            }

                            int coeffOffset = blockIdx << 6;
                            bool needSequential = !isProgressive || (Ah == 0 && Al == 0 && Ss == 0 && Se == 63);

                            bool ok;
                            if (needSequential)
                                ok = EncodeDCTBlockSequential(c.Coeffs, coeffOffset, dcHuff, acHuff, numZeroRuns, lastDcCoeff, (int)si.CompIdx, bw);
                            else if (Ah == 0)
                                ok = EncodeDCTBlockProgressive(c.Coeffs, coeffOffset, dcHuff, acHuff, Ss, Se, Al, numZeroRuns, codingState, lastDcCoeff, (int)si.CompIdx, bw);
                            else
                                ok = EncodeRefinementBits(c.Coeffs, coeffOffset, acHuff, Ss, Se, Al, codingState, bw);

                            if (!ok) return false;
                            blockScanIndex++;
                        }
                    }
                }
                restartToGo--;
            }
        }

        codingState.Flush(bw);
        if (!bw.JumpToByteBoundary(ref padBitsPos, padBits))
            return false;

        return bw.Healthy;
    }

    private static bool EncodeDCTBlockSequential(
        short[] coeffs, int offset,
        HuffmanCodeTable dcHuff, HuffmanCodeTable acHuff,
        int numZeroRuns, short[] lastDcCoeff, int compIdx,
        JpegBitWriter bw)
    {
        short temp2 = coeffs[offset];
        short temp = (short)(temp2 - lastDcCoeff[compIdx]);
        lastDcCoeff[compIdx] = temp2;

        // Compute absolute value + sign bit
        int absVal = temp < 0 ? -temp : temp;
        int signedVal = temp < 0 ? temp - 1 : temp;

        int dcNbits = absVal == 0 ? 0 : (FloorLog2((uint)absVal) + 1);
        bw.WriteSymbol(dcNbits, dcHuff);
        if (dcNbits > 0)
            bw.WriteBits(dcNbits, (ulong)(signedVal & ((1 << dcNbits) - 1)));

        int r = 0;
        for (int i = 1; i < 64; i++)
        {
            short coeff = coeffs[offset + (int)JpegConstants.kJPEGNaturalOrder[i]];
            if (coeff == 0)
            {
                r++;
            }
            else
            {
                int abs2 = coeff < 0 ? -coeff : coeff;
                int signed2 = coeff < 0 ? coeff - 1 : coeff;

                while (r > 15)
                {
                    bw.WriteSymbol(0xF0, acHuff);
                    r -= 16;
                }

                int acNbits = FloorLog2((uint)abs2) + 1;
                int symbol = (r << 4) + acNbits;
                bw.WriteSymbolBits(symbol, acHuff, acNbits,
                    (ulong)(signed2 & ((1 << acNbits) - 1)));
                r = 0;
            }
        }

        for (int i = 0; i < numZeroRuns; i++)
        {
            bw.WriteSymbol(0xF0, acHuff);
            r -= 16;
        }
        if (r > 0)
            bw.WriteSymbol(0, acHuff);

        return true;
    }

    private static bool EncodeDCTBlockProgressive(
        short[] coeffs, int offset,
        HuffmanCodeTable dcHuff, HuffmanCodeTable acHuff,
        int Ss, int Se, int Al, int numZeroRuns,
        DCTCodingState codingState, short[] lastDcCoeff, int compIdx,
        JpegBitWriter bw)
    {
        bool eobRunAllowed = Ss > 0;
        if (Ss == 0)
        {
            short temp2 = (short)(coeffs[offset] >> Al);
            short temp = (short)(temp2 - lastDcCoeff[compIdx]);
            lastDcCoeff[compIdx] = temp2;

            int absVal = temp < 0 ? -temp : temp;
            int signedVal = temp < 0 ? temp - 1 : temp;

            int nbits = absVal == 0 ? 0 : (FloorLog2((uint)absVal) + 1);
            bw.WriteSymbol(nbits, dcHuff);
            if (nbits > 0)
                bw.WriteBits(nbits, (ulong)(signedVal & ((1 << nbits) - 1)));
            Ss++;
        }
        if (Ss > Se) return true;

        int r = 0;
        for (int k = Ss; k <= Se; k++)
        {
            short coeff = coeffs[offset + (int)JpegConstants.kJPEGNaturalOrder[k]];
            if (coeff == 0) { r++; continue; }

            int absCoeff, signed2;
            if (coeff < 0)
            {
                absCoeff = (-coeff) >> Al;
                signed2 = ~((-coeff) >> Al);
            }
            else
            {
                absCoeff = coeff >> Al;
                signed2 = coeff >> Al;
            }

            if (absCoeff == 0) { r++; continue; }

            codingState.Flush(bw);
            while (r > 15)
            {
                bw.WriteSymbol(0xF0, acHuff);
                r -= 16;
            }

            int nbits = FloorLog2((uint)absCoeff) + 1;
            int symbol = (r << 4) + nbits;
            bw.WriteSymbol(symbol, acHuff);
            bw.WriteBits(nbits, (ulong)(signed2 & ((1 << nbits) - 1)));
            r = 0;
        }

        if (numZeroRuns > 0)
        {
            codingState.Flush(bw);
            for (int i = 0; i < numZeroRuns; i++)
                bw.WriteSymbol(0xF0, acHuff);
        }

        if (r > 0)
        {
            codingState.BufferEndOfBand(acHuff, null, 0, bw);
            if (!eobRunAllowed) codingState.Flush(bw);
        }
        return true;
    }

    private static bool EncodeRefinementBits(
        short[] coeffs, int offset,
        HuffmanCodeTable acHuff,
        int Ss, int Se, int Al,
        DCTCodingState codingState, JpegBitWriter bw)
    {
        bool eobRunAllowed = Ss > 0;
        if (Ss == 0)
        {
            bw.WriteBits(1, (ulong)((coeffs[offset] >> Al) & 1));
            Ss++;
        }
        if (Ss > Se) return true;

        int[] absValues = new int[JpegConstants.kDCTBlockSize];
        int eob = 0;
        for (int k = Ss; k <= Se; k++)
        {
            int absVal = Math.Abs(coeffs[offset + (int)JpegConstants.kJPEGNaturalOrder[k]]);
            absValues[k] = absVal >> Al;
            if (absValues[k] == 1) eob = k;
        }

        int r = 0;
        int[] refinementBits = new int[JpegConstants.kDCTBlockSize];
        int refinementBitsCount = 0;

        for (int k = Ss; k <= Se; k++)
        {
            if (absValues[k] == 0) { r++; continue; }

            while (r > 15 && k <= eob)
            {
                codingState.Flush(bw);
                bw.WriteSymbol(0xF0, acHuff);
                r -= 16;
                for (int i = 0; i < refinementBitsCount; i++)
                    bw.WriteBits(1, (ulong)refinementBits[i]);
                refinementBitsCount = 0;
            }

            if (absValues[k] > 1)
            {
                refinementBits[refinementBitsCount++] = absValues[k] & 1;
                continue;
            }

            codingState.Flush(bw);
            int symbol = (r << 4) + 1;
            int newNonZeroBit = coeffs[offset + (int)JpegConstants.kJPEGNaturalOrder[k]] < 0 ? 0 : 1;
            bw.WriteSymbol(symbol, acHuff);
            bw.WriteBits(1, (ulong)newNonZeroBit);
            for (int i = 0; i < refinementBitsCount; i++)
                bw.WriteBits(1, (ulong)refinementBits[i]);
            refinementBitsCount = 0;
            r = 0;
        }

        if (r > 0 || refinementBitsCount > 0)
        {
            codingState.BufferEndOfBand(acHuff, refinementBits, refinementBitsCount, bw);
            if (!eobRunAllowed) codingState.Flush(bw);
        }
        return true;
    }

    private static bool BuildHuffmanCodeTable(JPEGHuffmanCode huff, HuffmanCodeTable table)
    {
        int[] huffCode = new int[JpegConstants.kJpegHuffmanAlphabetSize];
        uint[] huffSize = new uint[JpegConstants.kJpegHuffmanAlphabetSize + 1];
        int p = 0;

        for (int l = 1; l <= JpegConstants.kJpegHuffmanMaxBitLength; l++)
        {
            int count = (int)huff.Counts[l];
            if (p + count > JpegConstants.kJpegHuffmanAlphabetSize + 1) return false;
            while (count-- > 0) huffSize[p++] = (uint)l;
        }

        if (p == 0) return true;

        int lastP = p - 1;
        huffSize[lastP] = 0;

        int code = 0;
        uint si = huffSize[0];
        p = 0;
        while (huffSize[p] != 0)
        {
            while (huffSize[p] == si)
            {
                huffCode[p++] = code;
                code++;
            }
            code <<= 1;
            si++;
        }

        for (p = 0; p < lastP; p++)
        {
            int i = (int)huff.Values[p];
            table.Depth[i] = (sbyte)huffSize[p];
            table.Code[i] = (ushort)huffCode[p];
        }
        return true;
    }

    private static int FloorLog2(uint val)
    {
        if (val == 0) return 0;
        return 31 - int.LeadingZeroCount((int)val);
    }
}
