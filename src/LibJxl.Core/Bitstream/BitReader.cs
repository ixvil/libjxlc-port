// Port of lib/jxl/dec_bit_reader.h — bounds-checked bit reader
using System.Diagnostics;
using System.Runtime.CompilerServices;
using LibJxl.Base;

namespace LibJxl.Bitstream;

/// <summary>
/// Reads bits from a byte buffer using a 64-bit internal buffer with deferred refills.
/// Port of jxl::BitReader from dec_bit_reader.h.
/// </summary>
public sealed class BitReader : IDisposable
{
    public const int MaxBitsPerCall = 56;

    private ulong _buf;
    private int _bitsInBuf;
    private readonly byte[] _data;
    private int _nextByte;
    private readonly int _endMinus8;
    private readonly int _firstByte;
    private ulong _overreadBytes;
    private bool _closeCalled;
    private ulong _checkedOutOfBoundsBits;

    /// <summary>Constructs an invalid BitReader (for later assignment).</summary>
    public BitReader()
    {
        _data = Array.Empty<byte>();
        _buf = 0;
        _bitsInBuf = 0;
        _nextByte = 0;
        _endMinus8 = 0;
        _firstByte = -1; // marks as invalid
    }

    /// <summary>Constructs a BitReader over the given data.</summary>
    public BitReader(byte[] data) : this(data, 0, data.Length) { }

    /// <summary>Constructs a BitReader over a slice of data.</summary>
    public BitReader(byte[] data, int offset, int length)
    {
        _data = data;
        _buf = 0;
        _bitsInBuf = 0;
        _nextByte = offset;
        _endMinus8 = offset + length - 8;
        _firstByte = offset;
        Refill();
    }

    public BitReader(ReadOnlySpan<byte> data) : this(data.ToArray()) { }

    /// <summary>Refills the internal 64-bit buffer from the byte stream.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Refill()
    {
        if (_nextByte > _endMinus8)
        {
            BoundsCheckedRefill();
        }
        else
        {
            // Safe to load 64 bits
            _buf |= LoadLE64(_nextByte) << _bitsInBuf;
            _nextByte += (63 - _bitsInBuf) >> 3;
            _bitsInBuf |= 56;
            Debug.Assert(_bitsInBuf >= 56 && _bitsInBuf < 64);
        }
    }

    /// <summary>Peeks at nbits without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong PeekBits(int nbits)
    {
        Debug.Assert(nbits <= MaxBitsPerCall);
        Debug.Assert(!_closeCalled);
        ulong mask = (1UL << nbits) - 1;
        return _buf & mask;
    }

    /// <summary>Peeks at N bits (compile-time constant).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong PeekFixedBits(int n)
    {
        Debug.Assert(n <= MaxBitsPerCall);
        Debug.Assert(!_closeCalled);
        return _buf & ((1UL << n) - 1);
    }

    /// <summary>Consumes num_bits from the buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Consume(int numBits)
    {
        Debug.Assert(!_closeCalled);
        Debug.Assert(_bitsInBuf >= numBits);
        _bitsInBuf -= numBits;
        _buf >>= numBits;
    }

    /// <summary>Reads nbits: refill, peek, consume.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadBits(int nbits)
    {
        Debug.Assert(!_closeCalled);
        Refill();
        ulong bits = PeekBits(nbits);
        Consume(nbits);
        return bits;
    }

    /// <summary>Read a fixed number of bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadFixedBits(int n)
    {
        Debug.Assert(!_closeCalled);
        Refill();
        ulong bits = PeekFixedBits(n);
        Consume(n);
        return bits;
    }

    /// <summary>Skips the given number of bits.</summary>
    public void SkipBits(int skip)
    {
        Debug.Assert(!_closeCalled);

        if (skip <= _bitsInBuf)
        {
            Consume(skip);
            return;
        }

        skip -= _bitsInBuf;
        _bitsInBuf = 0;
        _buf = 0;

        int wholeBytes = skip / JxlConstants.BitsPerByte;
        skip %= JxlConstants.BitsPerByte;

        int remaining = _endMinus8 + 8 - _nextByte;
        if (wholeBytes > remaining)
        {
            _nextByte = _endMinus8 + 8;
            skip += JxlConstants.BitsPerByte;
        }
        else
        {
            _nextByte += wholeBytes;
        }

        Refill();
        Consume(skip);
    }

    /// <summary>Total bits consumed so far.</summary>
    public long TotalBitsConsumed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            long bytesRead = _nextByte - _firstByte;
            return (bytesRead + (long)_overreadBytes) * JxlConstants.BitsPerByte - _bitsInBuf;
        }
    }

    /// <summary>Total bytes in the input.</summary>
    public int TotalBytes => _endMinus8 + 8 - _firstByte;

    /// <summary>Jumps to the next byte boundary, checking padding bits are zero.</summary>
    public JxlStatus JumpToByteBoundary()
    {
        int remainder = (int)(TotalBitsConsumed % JxlConstants.BitsPerByte);
        if (remainder == 0) return true;
        if (ReadBits(JxlConstants.BitsPerByte - remainder) != 0)
        {
            return new JxlStatus(StatusCode.GenericError);
        }
        return true;
    }

    /// <summary>Returns whether all reads so far were within bounds.</summary>
    public JxlStatus AllReadsWithinBounds()
    {
        _checkedOutOfBoundsBits = (ulong)TotalBitsConsumed;
        if (TotalBitsConsumed > (long)TotalBytes * JxlConstants.BitsPerByte)
            return false;
        return true;
    }

    /// <summary>Closes the bit reader and checks for overread.</summary>
    public JxlStatus Close()
    {
        Debug.Assert(!_closeCalled);
        _closeCalled = true;
        if (_firstByte < 0) return true; // was never initialized
        if (TotalBitsConsumed > (long)_checkedOutOfBoundsBits &&
            TotalBitsConsumed > (long)TotalBytes * JxlConstants.BitsPerByte)
        {
            return new JxlStatus(StatusCode.GenericError);
        }
        return true;
    }

    public void Dispose()
    {
        if (!_closeCalled && _firstByte >= 0)
            Close();
    }

    // --- Private helpers ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong LoadLE64(int offset)
    {
        if (offset + 8 <= _data.Length)
        {
            return BitConverter.ToUInt64(_data, offset);
        }
        // Partial read
        ulong result = 0;
        int available = _data.Length - offset;
        for (int i = 0; i < Math.Min(8, available); i++)
        {
            result |= (ulong)_data[offset + i] << (i * 8);
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BoundsCheckedRefill()
    {
        int end = _endMinus8 + 8;
        while (_bitsInBuf < 56)
        {
            if (_nextByte < end)
            {
                _buf |= (ulong)_data[_nextByte] << _bitsInBuf;
                _nextByte++;
                _bitsInBuf += 8;
            }
            else
            {
                // Past the end — read zeros
                _overreadBytes++;
                _bitsInBuf += 8;
            }
        }
    }
}
