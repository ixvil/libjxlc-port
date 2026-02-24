// Port of ANSSymbolReader from lib/jxl/dec_ans.h/cc — ANS symbol reader state machine
using System.Runtime.CompilerServices;
using LibJxl.Bitstream;

namespace LibJxl.Entropy;

/// <summary>
/// Reads symbols from an entropy-coded stream using ANS or Huffman coding
/// with optional LZ77 support. Port of jxl::ANSSymbolReader.
/// </summary>
public class ANSSymbolReader
{
    public const int WindowSize = 1 << 20;
    public const int WindowMask = WindowSize - 1;
    public const int NumSpecialDistances = 120;
    public const int MaxCheckpointInterval = 512;

    // Special distance table from WebP lossless
    private static readonly sbyte[,] SpecialDistanceTable = {
        {0, 1},  {1, 0},  {1, 1},  {-1, 1}, {0, 2},  {2, 0},  {1, 2},  {-1, 2},
        {2, 1},  {-2, 1}, {2, 2},  {-2, 2}, {0, 3},  {3, 0},  {1, 3},  {-1, 3},
        {3, 1},  {-3, 1}, {2, 3},  {-2, 3}, {3, 2},  {-3, 2}, {0, 4},  {4, 0},
        {1, 4},  {-1, 4}, {4, 1},  {-4, 1}, {3, 3},  {-3, 3}, {2, 4},  {-2, 4},
        {4, 2},  {-4, 2}, {0, 5},  {3, 4},  {-3, 4}, {4, 3},  {-4, 3}, {5, 0},
        {1, 5},  {-1, 5}, {5, 1},  {-5, 1}, {2, 5},  {-2, 5}, {5, 2},  {-5, 2},
        {4, 4},  {-4, 4}, {3, 5},  {-3, 5}, {5, 3},  {-5, 3}, {0, 6},  {6, 0},
        {1, 6},  {-1, 6}, {6, 1},  {-6, 1}, {2, 6},  {-2, 6}, {6, 2},  {-6, 2},
        {4, 5},  {-4, 5}, {5, 4},  {-5, 4}, {3, 6},  {-3, 6}, {6, 3},  {-6, 3},
        {0, 7},  {7, 0},  {1, 7},  {-1, 7}, {5, 5},  {-5, 5}, {7, 1},  {-7, 1},
        {4, 6},  {-4, 6}, {6, 4},  {-6, 4}, {2, 7},  {-2, 7}, {7, 2},  {-7, 2},
        {3, 7},  {-3, 7}, {7, 3},  {-7, 3}, {5, 6},  {-5, 6}, {6, 5},  {-6, 5},
        {8, 0},  {4, 7},  {-4, 7}, {7, 4},  {-7, 4}, {8, 1},  {8, 2},  {6, 6},
        {-6, 6}, {8, 3},  {5, 7},  {-5, 7}, {7, 5},  {-7, 5}, {8, 4},  {6, 7},
        {-6, 7}, {7, 6},  {-7, 6}, {8, 5},  {7, 7},  {-7, 7}, {8, 6},  {8, 7}
    };

    // ANS state
    private readonly AliasTable.Entry[] _aliasTables;
    private readonly HuffmanDecoder[] _huffmanData;
    private readonly bool _usePrefixCode;
    private uint _state;
    private readonly HybridUintConfig[] _configs;
    private readonly int _logAlphaSize;
    private readonly int _logEntrySize;
    private readonly int _entrySizeMinus1;

    // LZ77 state
    private uint[]? _lz77Window;
    private uint _numDecoded;
    private uint _numToCopy;
    private uint _copyPos;
    private int _lz77Ctx;
    private uint _lz77MinLength;
    private uint _lz77Threshold = 1 << 20; // bigger than any symbol
    private HybridUintConfig _lz77LengthUint;
    private readonly int[] _specialDistances = new int[NumSpecialDistances];
    private int _numSpecialDistances;

    private ANSSymbolReader(ANSCode code, BitReader br, int distanceMultiplier)
    {
        _aliasTables = code.AliasTables;
        _huffmanData = code.HuffmanData;
        _usePrefixCode = code.UsePrefixCode;
        _configs = code.UintConfig;

        if (!_usePrefixCode)
        {
            _state = (uint)br.ReadFixedBits(32);
            _logAlphaSize = code.LogAlphaSize;
            _logEntrySize = AnsParams.AnsLogTabSize - code.LogAlphaSize;
            _entrySizeMinus1 = (1 << _logEntrySize) - 1;
        }
        else
        {
            _state = (uint)AnsParams.AnsSignature << 16;
        }

        if (!code.Lz77.Enabled) return;

        _lz77Window = new uint[WindowSize];
        _lz77Ctx = code.Lz77.NonserializedDistanceContext;
        _lz77LengthUint = code.Lz77.LengthUintConfig;
        _lz77Threshold = code.Lz77.MinSymbol;
        _lz77MinLength = code.Lz77.MinLength;
        _numSpecialDistances = distanceMultiplier == 0 ? 0 : NumSpecialDistances;

        for (int i = 0; i < _numSpecialDistances; i++)
        {
            int dist = SpecialDistanceTable[i, 0] +
                       distanceMultiplier * SpecialDistanceTable[i, 1];
            _specialDistances[i] = dist > 1 ? dist : 1;
        }
    }

    /// <summary>Creates an ANSSymbolReader from the given ANSCode.</summary>
    public static ANSSymbolReader Create(ANSCode code, BitReader br, int distanceMultiplier = 0)
    {
        return new ANSSymbolReader(code, br, distanceMultiplier);
    }

    /// <summary>Whether this reader uses LZ77.</summary>
    public bool UsesLZ77 => _lz77Window != null;

    /// <summary>Reads a raw ANS symbol without refill (caller must refill).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadSymbolANSWithoutRefill(int histoIdx, BitReader br)
    {
        uint res = _state & (AnsParams.AnsTabSize - 1u);

        int tableOffset = histoIdx << _logAlphaSize;
        var symbol = AliasTable.Lookup(_aliasTables, (int)res, _logEntrySize, _entrySizeMinus1,
            tableOffset);

        _state = (uint)symbol.Freq * (_state >> AnsParams.AnsLogTabSize) + (uint)symbol.Offset;

        // Branchless normalization
        uint newState = (_state << 16) | (uint)br.PeekFixedBits(16);
        bool normalize = _state < (1u << 16);
        _state = normalize ? newState : _state;
        br.Consume(normalize ? 16 : 0);

        return symbol.Value;
    }

    /// <summary>Reads a Huffman symbol without refill (caller must refill).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadSymbolHuffWithoutRefill(int histoIdx, BitReader br)
    {
        return _huffmanData[histoIdx].ReadSymbolWithoutRefill(br);
    }

    /// <summary>Reads a symbol without refill, using the appropriate method.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadSymbolWithoutRefill(int histoIdx, BitReader br)
    {
        if (_usePrefixCode)
            return ReadSymbolHuffWithoutRefill(histoIdx, br);
        return ReadSymbolANSWithoutRefill(histoIdx, br);
    }

    /// <summary>Reads a symbol with refill.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadSymbol(int histoIdx, BitReader br)
    {
        br.Refill();
        return ReadSymbolWithoutRefill(histoIdx, br);
    }

    /// <summary>Checks ANS final state (should be ANS_SIGNATURE &lt;&lt; 16).</summary>
    public bool CheckANSFinalState()
    {
        return _state == ((uint)AnsParams.AnsSignature << 16);
    }

    /// <summary>
    /// Reads a hybrid uint from a clustered context, with optional LZ77.
    /// This is the main entry point for reading values.
    /// </summary>
    public uint ReadHybridUintClustered(int ctx, BitReader br, bool usesLz77)
    {
        if (usesLz77)
        {
            if (_numToCopy > 0)
            {
                uint ret = _lz77Window![(_copyPos++) & WindowMask];
                _numToCopy--;
                _lz77Window[(_numDecoded++) & WindowMask] = ret;
                return ret;
            }
        }

        br.Refill();
        uint token = (uint)ReadSymbolWithoutRefill(ctx, br);

        if (usesLz77)
        {
            if (token >= _lz77Threshold)
            {
                _numToCopy = HybridUintConfig.DecodeHybridUint(
                    in _lz77LengthUint, token - _lz77Threshold, br) + _lz77MinLength;

                br.Refill();
                // Distance code
                uint dToken = (uint)ReadSymbolWithoutRefill(_lz77Ctx, br);
                uint distance = HybridUintConfig.DecodeHybridUint(
                    in _configs[_lz77Ctx], dToken, br);

                if (distance < (uint)_numSpecialDistances)
                    distance = (uint)_specialDistances[distance];
                else
                    distance = distance + 1 - (uint)_numSpecialDistances;

                if (distance > _numDecoded)
                    distance = _numDecoded;
                if (distance > WindowSize)
                    distance = WindowSize;

                _copyPos = _numDecoded - distance;

                if (distance == 0)
                {
                    // distance 0 -> fill with zeros
                    uint toFill = Math.Min(_numToCopy, WindowSize);
                    Array.Clear(_lz77Window!, 0, (int)toFill);
                }

                if (_numToCopy < _lz77MinLength) return 0;

                uint lzRet = _lz77Window![(_copyPos++) & WindowMask];
                _numToCopy--;
                _lz77Window[(_numDecoded++) & WindowMask] = lzRet;
                return lzRet;
            }
        }

        uint result = HybridUintConfig.DecodeHybridUint(in _configs[ctx], token, br);
        if (usesLz77 && _lz77Window != null)
            _lz77Window[(_numDecoded++) & WindowMask] = result;
        return result;
    }

    /// <summary>Reads a hybrid uint using a context map to map context to cluster.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadHybridUint(int ctx, BitReader br, byte[] contextMap)
    {
        return ReadHybridUintClustered(contextMap[ctx], br, usesLz77: true);
    }

    /// <summary>
    /// Reads a hybrid uint using a context map (inlined version for hot paths).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadHybridUintInlined(int ctx, BitReader br, byte[] contextMap, bool usesLz77)
    {
        return ReadHybridUintClustered(contextMap[ctx], br, usesLz77);
    }

    /// <summary>
    /// Checks if a clustered context always produces the same value, and advances
    /// the reader state as if count symbols were decoded. Returns true if so.
    /// </summary>
    public bool IsSingleValueAndAdvance(int ctx, out uint value, int count)
    {
        value = 0;

        if (_usePrefixCode) return false;
        if (_numToCopy != 0) return false;

        uint res = _state & (AnsParams.AnsTabSize - 1u);
        int tableOffset = ctx << _logAlphaSize;
        var symbol = AliasTable.Lookup(_aliasTables, (int)res, _logEntrySize, _entrySizeMinus1,
            tableOffset);

        if (symbol.Freq != AnsParams.AnsTabSize) return false;
        if (_configs[ctx].SplitToken <= (uint)symbol.Value) return false;
        if ((uint)symbol.Value >= _lz77Threshold) return false;

        value = (uint)symbol.Value;

        if (_lz77Window != null)
        {
            for (int i = 0; i < count; i++)
                _lz77Window[(_numDecoded++) & WindowMask] = value;
        }

        return true;
    }

    /// <summary>Checkpoint for save/restore of reader state.</summary>
    public struct Checkpoint
    {
        public uint State;
        public uint NumToCopy;
        public uint CopyPos;
        public uint NumDecoded;
        public uint[] Lz77Window;
    }

    /// <summary>Saves the current state to a checkpoint.</summary>
    public Checkpoint Save()
    {
        var cp = new Checkpoint
        {
            State = _state,
            NumDecoded = _numDecoded,
            NumToCopy = _numToCopy,
            CopyPos = _copyPos
        };

        if (_lz77Window != null)
        {
            cp.Lz77Window = new uint[MaxCheckpointInterval];
            uint winStart = _numDecoded & WindowMask;
            uint winEnd = (_numDecoded + MaxCheckpointInterval) & (uint)WindowMask;
            if (winEnd > winStart)
            {
                Array.Copy(_lz77Window, winStart, cp.Lz77Window, 0, winEnd - winStart);
            }
            else
            {
                Array.Copy(_lz77Window, winStart, cp.Lz77Window, 0, WindowSize - winStart);
                Array.Copy(_lz77Window, 0, cp.Lz77Window, WindowSize - winStart, winEnd);
            }
        }

        return cp;
    }

    /// <summary>Restores state from a checkpoint.</summary>
    public void Restore(in Checkpoint cp)
    {
        _state = cp.State;
        _numDecoded = cp.NumDecoded;
        _numToCopy = cp.NumToCopy;
        _copyPos = cp.CopyPos;

        if (_lz77Window != null && cp.Lz77Window != null)
        {
            uint winStart = _numDecoded & WindowMask;
            uint winEnd = (_numDecoded + MaxCheckpointInterval) & (uint)WindowMask;
            if (winEnd > winStart)
            {
                Array.Copy(cp.Lz77Window, 0, _lz77Window, winStart, winEnd - winStart);
            }
            else
            {
                Array.Copy(cp.Lz77Window, 0, _lz77Window, winStart, WindowSize - winStart);
                Array.Copy(cp.Lz77Window, (int)(WindowSize - winStart), _lz77Window, 0, (int)winEnd);
            }
        }
    }
}
