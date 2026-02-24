// Port of lib/jxl/jpeg/dec_jpeg_data.cc — Decode JPEG data from JXL codestream
// Uses Brotli decompression for APP markers, COM markers, inter-marker data, and tail data.

using System.IO.Compression;
using LibJxl.Bitstream;

namespace LibJxl.Jpeg;

/// <summary>
/// Decodes JPEG reconstruction data from a JXL codestream.
/// Port of jxl::jpeg::DecodeJPEGData.
/// </summary>
public static class JpegDecoder
{
    /// <summary>
    /// Decode JPEG data from encoded bytes.
    /// The encoded data contains:
    /// 1. Bit-field serialized JPEGData structure
    /// 2. Brotli-compressed marker and tail data
    /// </summary>
    public static bool DecodeJPEGData(ReadOnlySpan<byte> encoded, JPEGData jpegData)
    {
        // Phase 1: Read field-encoded metadata using BitReader
        var br = new BitReader(encoded.ToArray(), 0, encoded.Length);

        if (!ReadJPEGDataFields(br, jpegData))
            return false;

        br.JumpToByteBoundary();
        int bytesConsumed = (int)(br.TotalBitsConsumed / 8);

        // Phase 2: Brotli decompress remaining marker data
        if (bytesConsumed < encoded.Length)
        {
            var brotliData = encoded.Slice(bytesConsumed);
            if (!DecompressBrotliData(brotliData.ToArray(), jpegData))
                return false;
        }
        else if (NeedsBrotliData(jpegData))
        {
            return false;
        }

        return true;
    }

    // Helpers for reading typed values from BitReader (which returns ulong)
    private static bool RBool(BitReader br) => br.ReadBits(1) != 0;
    private static uint RU(BitReader br, int bits) => (uint)br.ReadBits(bits);

    private static bool NeedsBrotliData(JPEGData jpegData)
    {
        for (int i = 0; i < jpegData.AppData.Count; i++)
            if (jpegData.AppMarkerType[i] == AppMarkerType.Unknown)
                return true;
        if (jpegData.ComData.Count > 0) return true;
        if (jpegData.InterMarkerData.Count > 0) return true;
        if (jpegData.TailData.Length > 0) return true;
        return false;
    }

    private static bool DecompressBrotliData(byte[] compressed, JPEGData jpegData)
    {
        byte[] decompressed;
        try
        {
            using var input = new MemoryStream(compressed);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            decompressed = output.ToArray();
        }
        catch
        {
            return false;
        }

        int pos = 0;

        int numIcc = 0;
        for (int i = 0; i < jpegData.AppData.Count; i++)
        {
            var marker = jpegData.AppData[i];
            if (jpegData.AppMarkerType[i] != AppMarkerType.Unknown)
            {
                int sizeMinus1 = marker.Length - 1;
                marker[1] = (byte)(sizeMinus1 >> 8);
                marker[2] = (byte)(sizeMinus1 & 0xFF);

                if (jpegData.AppMarkerType[i] == AppMarkerType.ICC)
                {
                    if (marker.Length < 17) return false;
                    marker[0] = 0xE2;
                    Array.Copy(JpegConstants.kIccProfileTag, 0, marker, 3,
                        Math.Min(JpegConstants.kIccProfileTag.Length, marker.Length - 3));
                    marker[15] = (byte)(++numIcc);
                }
            }
            else
            {
                if (pos + marker.Length > decompressed.Length) return false;
                Array.Copy(decompressed, pos, marker, 0, marker.Length);
                pos += marker.Length;
                if (marker[1] * 256 + marker[2] + 1 != marker.Length) return false;
            }
        }

        for (int i = 0; i < jpegData.AppData.Count; i++)
        {
            var marker = jpegData.AppData[i];
            if (jpegData.AppMarkerType[i] == AppMarkerType.ICC)
                marker[16] = (byte)numIcc;
            if (jpegData.AppMarkerType[i] == AppMarkerType.Exif)
            {
                marker[0] = 0xE1;
                if (marker.Length < 3 + JpegConstants.kExifTag.Length) return false;
                Array.Copy(JpegConstants.kExifTag, 0, marker, 3,
                    Math.Min(JpegConstants.kExifTag.Length, marker.Length - 3));
            }
            if (jpegData.AppMarkerType[i] == AppMarkerType.XMP)
            {
                marker[0] = 0xE1;
                if (marker.Length < 3 + JpegConstants.kXMPTag.Length) return false;
                Array.Copy(JpegConstants.kXMPTag, 0, marker, 3,
                    Math.Min(JpegConstants.kXMPTag.Length, marker.Length - 3));
            }
        }

        foreach (var marker in jpegData.ComData)
        {
            if (pos + marker.Length > decompressed.Length) return false;
            Array.Copy(decompressed, pos, marker, 0, marker.Length);
            pos += marker.Length;
            if (marker[1] * 256 + marker[2] + 1 != marker.Length) return false;
        }

        foreach (var data in jpegData.InterMarkerData)
        {
            if (pos + data.Length > decompressed.Length) return false;
            Array.Copy(decompressed, pos, data, 0, data.Length);
            pos += data.Length;
        }

        if (jpegData.TailData.Length > 0)
        {
            if (pos + jpegData.TailData.Length > decompressed.Length) return false;
            Array.Copy(decompressed, pos, jpegData.TailData, 0, jpegData.TailData.Length);
            pos += jpegData.TailData.Length;
        }

        return true;
    }

    /// <summary>
    /// Read JPEGData fields from a bitstream.
    /// Port of JPEGData::VisitFields.
    /// </summary>
    private static bool ReadJPEGDataFields(BitReader br, JPEGData jpegData)
    {
        bool isGray = RBool(br);
        jpegData.Components.Clear();
        for (int i = 0; i < (isGray ? 1 : 3); i++)
            jpegData.Components.Add(new JPEGComponent());

        // Marker order
        int numAppMarkers = 0, numComMarkers = 0, numScans = 0, numIntermarker = 0;
        bool hasDri = false;

        jpegData.MarkerOrder.Clear();
        byte marker;
        do
        {
            uint marker32 = RU(br, 6);
            marker = (byte)(marker32 + 0xC0);

            if ((marker & 0xF0) == 0xE0) numAppMarkers++;
            if (marker == 0xFE) numComMarkers++;
            if (marker == 0xDA) numScans++;
            if (marker == 0xFF) numIntermarker++;
            if (marker == 0xDD) hasDri = true;

            jpegData.MarkerOrder.Add(marker);
            if (jpegData.MarkerOrder.Count > 16384) return false;
        } while (marker != 0xD9);

        jpegData.AppData.Clear();
        jpegData.AppMarkerType.Clear();
        jpegData.ComData.Clear();
        jpegData.ScanInfo.Clear();

        for (int i = 0; i < numAppMarkers; i++)
        {
            jpegData.AppData.Add(Array.Empty<byte>());
            jpegData.AppMarkerType.Add(AppMarkerType.Unknown);
        }
        for (int i = 0; i < numComMarkers; i++)
            jpegData.ComData.Add(Array.Empty<byte>());
        for (int i = 0; i < numScans; i++)
            jpegData.ScanInfo.Add(new JPEGScanInfo());

        // APP marker sizes and types
        // U32(Val(0), Val(1), BitsOffset(1, 2), BitsOffset(2, 4))
        for (int i = 0; i < numAppMarkers; i++)
        {
            uint typeVal = ReadU32_V_V_BO_BO(br, 0, 1, 2, 4, 1, 2);
            jpegData.AppMarkerType[i] = (AppMarkerType)typeVal;
            uint len = RU(br, 16);
            jpegData.AppData[i] = new byte[len + 1];
        }

        for (int i = 0; i < numComMarkers; i++)
        {
            uint len = RU(br, 16);
            jpegData.ComData[i] = new byte[len + 1];
        }

        // Quant tables: U32(Val(1), Val(2), Val(3), Val(4))
        uint numQuantTables = ReadU32_4Val(br, 1, 2, 3, 4);
        if (numQuantTables == 4) return false;

        jpegData.Quant.Clear();
        for (uint i = 0; i < numQuantTables; i++)
        {
            var qt = new JPEGQuantTable();
            qt.Precision = RU(br, 1);
            qt.Index = RU(br, 2);
            qt.IsLast = RBool(br);
            jpegData.Quant.Add(qt);
        }

        // Component type
        uint componentType = RU(br, 2);
        uint nc;
        if (componentType == 0) nc = 1;
        else if (componentType != 3) nc = 3;
        else
        {
            nc = ReadU32_4Val(br, 1, 2, 3, 4);
            if (nc != 1 && nc != 3) return false;
        }

        jpegData.Components.Clear();
        for (uint i = 0; i < nc; i++)
            jpegData.Components.Add(new JPEGComponent());

        if (componentType == 3)
        {
            foreach (var comp in jpegData.Components) comp.Id = RU(br, 8);
        }
        else if (componentType == 0) { jpegData.Components[0].Id = 1; }
        else if (componentType == 2)
        {
            jpegData.Components[0].Id = (byte)'R';
            jpegData.Components[1].Id = (byte)'G';
            jpegData.Components[2].Id = (byte)'B';
        }
        else { jpegData.Components[0].Id = 1; jpegData.Components[1].Id = 2; jpegData.Components[2].Id = 3; }

        foreach (var comp in jpegData.Components)
        {
            comp.QuantIdx = RU(br, 2);
            if (comp.QuantIdx >= (uint)jpegData.Quant.Count) return false;
        }

        // Huffman codes: U32(Val(4), BitsOffset(3,2), BitsOffset(4,10), BitsOffset(6,26))
        uint numHuff = ReadU32_V_BO_BO_BO(br, 4, 2, 10, 26, 3, 4, 6);

        jpegData.HuffmanCode.Clear();
        for (uint i = 0; i < numHuff; i++)
            jpegData.HuffmanCode.Add(new JPEGHuffmanCode());

        foreach (var hc in jpegData.HuffmanCode)
        {
            bool isAc = RBool(br);
            uint id = RU(br, 2);
            hc.SlotId = (int)(((isAc ? 1u : 0u) << 4) | id);
            hc.IsLast = RBool(br);

            uint numSymbols = 0;
            for (int i = 0; i <= 16; i++)
            {
                // U32(Val(0), Val(1), BitsOffset(3,2), Bits(8))
                hc.Counts[i] = ReadU32_V_V_BO_B(br, 0, 1, 2, 3, 8);
                numSymbols += hc.Counts[i];
            }

            if (numSymbols == 0) continue;
            if (numSymbols > (uint)hc.Values.Length) return false;

            for (uint i = 0; i < numSymbols; i++)
            {
                // U32(Bits(2), BitsOffset(2,4), BitsOffset(4,8), BitsOffset(8,1))
                hc.Values[i] = ReadU32_B_BO_BO_BO(br, 0, 4, 8, 1, 2, 2, 4, 8);
            }
        }

        // Scan info
        foreach (var scan in jpegData.ScanInfo)
        {
            scan.NumComponents = ReadU32_4Val(br, 1, 2, 3, 4);
            if (scan.NumComponents >= 4) return false;
            scan.Ss = RU(br, 6);
            scan.Se = RU(br, 6);
            scan.Al = RU(br, 4);
            scan.Ah = RU(br, 4);

            for (uint i = 0; i < scan.NumComponents; i++)
            {
                scan.Components[i].CompIdx = RU(br, 2);
                if (scan.Components[i].CompIdx >= (uint)jpegData.Components.Count) return false;
                scan.Components[i].AcTblIdx = RU(br, 2);
                scan.Components[i].DcTblIdx = RU(br, 2);
            }

            // U32(Val(0), Val(1), Val(2), BitsOffset(3,3))
            scan.LastNeededPass = ReadU32_V_V_V_BO(br, 0, 1, 2, 3, 3);
        }

        if (hasDri) jpegData.RestartInterval = RU(br, 16);

        // Reset points and extra zero runs
        foreach (var scan in jpegData.ScanInfo)
        {
            // U32(Val(0), BitsOffset(2,1), BitsOffset(4,4), BitsOffset(16,20))
            uint numResetPoints = ReadU32_V_BO_BO_BO(br, 0, 1, 4, 20, 2, 4, 16);
            scan.ResetPoints.Clear();
            int lastBlockIdx = -1;
            for (uint i = 0; i < numResetPoints; i++)
            {
                uint blockIdx = ReadU32_V_BO_BO_BO(br, 0, 1, 9, 41, 3, 5, 28);
                blockIdx += (uint)(lastBlockIdx + 1);
                if (blockIdx >= (3u << 26)) return false;
                scan.ResetPoints.Add(blockIdx);
                lastBlockIdx = (int)blockIdx;
            }

            uint numExtraZeroRuns = ReadU32_V_BO_BO_BO(br, 0, 1, 4, 20, 2, 4, 16);
            scan.ExtraZeroRuns.Clear();
            lastBlockIdx = -1;
            for (uint i = 0; i < numExtraZeroRuns; i++)
            {
                uint numRuns = ReadU32_V_BO_BO_BO(br, 1, 2, 5, 20, 2, 4, 8);
                uint blockIdx = ReadU32_V_BO_BO_BO(br, 0, 1, 9, 41, 3, 5, 28);
                blockIdx += (uint)(lastBlockIdx + 1);
                if (blockIdx > (3u << 26)) return false;
                scan.ExtraZeroRuns.Add(new ExtraZeroRunInfo { BlockIdx = blockIdx, NumExtraZeroRuns = numRuns });
                lastBlockIdx = (int)blockIdx;
            }
        }

        // Inter-marker data
        jpegData.InterMarkerData.Clear();
        for (int i = 0; i < numIntermarker; i++)
        {
            uint len = RU(br, 16);
            jpegData.InterMarkerData.Add(new byte[len]);
        }

        // Tail data: U32(Val(0), BitsOffset(8,1), BitsOffset(16,257), BitsOffset(22,65793))
        uint tailDataLen = ReadU32_V_BO_BO_BO(br, 0, 1, 257, 65793, 8, 16, 22);
        jpegData.TailData = new byte[tailDataLen];

        // Zero padding bits
        jpegData.HasZeroPaddingBit = RBool(br);
        if (jpegData.HasZeroPaddingBit)
        {
            uint nbit = RU(br, 24);
            jpegData.PaddingBits.Clear();
            for (uint i = 0; i < nbit; i++)
                jpegData.PaddingBits.Add(RBool(br) ? (byte)1 : (byte)0);
        }

        return true;
    }

    // U32 reader variants matching different C++ distribution patterns

    /// <summary>U32(Val(a), Val(b), BitsOffset(bits1,c), BitsOffset(bits2,d))</summary>
    private static uint ReadU32_V_V_BO_BO(BitReader br, uint a, uint b, uint c, uint d, int bits1, int bits2)
    {
        uint sel = RU(br, 2);
        return sel switch
        {
            0 => a,
            1 => b,
            2 => RU(br, bits1) + c,
            3 => RU(br, bits2) + d,
            _ => 0
        };
    }

    /// <summary>U32(Val(a), Val(b), Val(c), Val(d))</summary>
    private static uint ReadU32_4Val(BitReader br, uint a, uint b, uint c, uint d)
    {
        uint sel = RU(br, 2);
        return sel switch { 0 => a, 1 => b, 2 => c, 3 => d, _ => 0 };
    }

    /// <summary>U32(Val(a), BitsOffset(bits1,b), BitsOffset(bits2,c), BitsOffset(bits3,d))</summary>
    private static uint ReadU32_V_BO_BO_BO(BitReader br, uint a, uint b, uint c, uint d,
        int bits1, int bits2, int bits3)
    {
        uint sel = RU(br, 2);
        return sel switch
        {
            0 => a,
            1 => RU(br, bits1) + b,
            2 => RU(br, bits2) + c,
            3 => RU(br, bits3) + d,
            _ => 0
        };
    }

    /// <summary>U32(Val(a), Val(b), Val(c), BitsOffset(bits1,d))</summary>
    private static uint ReadU32_V_V_V_BO(BitReader br, uint a, uint b, uint c, uint d, int bits1)
    {
        uint sel = RU(br, 2);
        return sel switch
        {
            0 => a,
            1 => b,
            2 => c,
            3 => RU(br, bits1) + d,
            _ => 0
        };
    }

    /// <summary>U32(Val(a), Val(b), BitsOffset(bits1,c), Bits(bits2))</summary>
    private static uint ReadU32_V_V_BO_B(BitReader br, uint a, uint b, uint c, int bits1, int bits2)
    {
        uint sel = RU(br, 2);
        return sel switch
        {
            0 => a,
            1 => b,
            2 => RU(br, bits1) + c,
            3 => RU(br, bits2),
            _ => 0
        };
    }

    /// <summary>U32(Bits(bits0)+a, BitsOffset(bits1,b), BitsOffset(bits2,c), BitsOffset(bits3,d))</summary>
    private static uint ReadU32_B_BO_BO_BO(BitReader br, uint a, uint b, uint c, uint d,
        int bits0, int bits1, int bits2, int bits3)
    {
        uint sel = RU(br, 2);
        return sel switch
        {
            0 => RU(br, bits0) + a,
            1 => RU(br, bits1) + b,
            2 => RU(br, bits2) + c,
            3 => RU(br, bits3) + d,
            _ => 0
        };
    }
}
