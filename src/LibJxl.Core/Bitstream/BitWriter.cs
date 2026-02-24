// Port of lib/jxl/enc_bit_writer.h/cc — bit writer
using System.Diagnostics;
using System.Runtime.CompilerServices;
using LibJxl.Base;

namespace LibJxl.Bitstream;

/// <summary>
/// Writes bits to a growable byte buffer.
/// Port of jxl::BitWriter from enc_bit_writer.h.
/// </summary>
public sealed class BitWriter
{
    private byte[] _storage;
    private int _byteCount;
    private ulong _buffer;
    private int _bitsInBuffer;

    public BitWriter(int initialCapacity = 256)
    {
        _storage = new byte[initialCapacity];
        _byteCount = 0;
        _buffer = 0;
        _bitsInBuffer = 0;
    }

    /// <summary>Total bits written so far.</summary>
    public long BitsWritten => (long)_byteCount * JxlConstants.BitsPerByte + _bitsInBuffer;

    /// <summary>Writes nbits from the lower bits of 'bits'.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(int nbits, ulong bits)
    {
        Debug.Assert(nbits <= 56);
        Debug.Assert(nbits == 64 || (bits >> nbits) == 0, "upper bits must be zero");

        _buffer |= bits << _bitsInBuffer;
        _bitsInBuffer += nbits;

        // Flush complete bytes
        while (_bitsInBuffer >= 8)
        {
            EnsureCapacity(1);
            _storage[_byteCount++] = (byte)(_buffer & 0xFF);
            _buffer >>= 8;
            _bitsInBuffer -= 8;
        }
    }

    /// <summary>Pads to the next byte boundary with zero bits.</summary>
    public void ZeroPadToByte()
    {
        if (_bitsInBuffer > 0)
        {
            int pad = 8 - _bitsInBuffer;
            Write(pad, 0);
        }
    }

    /// <summary>Returns the written data as a byte array.</summary>
    public byte[] GetSpan()
    {
        // Flush remaining bits
        if (_bitsInBuffer > 0)
        {
            EnsureCapacity(1);
            _storage[_byteCount++] = (byte)(_buffer & 0xFF);
            _buffer = 0;
            _bitsInBuffer = 0;
        }
        return _storage.AsSpan(0, _byteCount).ToArray();
    }

    /// <summary>Appends raw bytes.</summary>
    public void AppendBytes(ReadOnlySpan<byte> bytes)
    {
        ZeroPadToByte();
        EnsureCapacity(bytes.Length);
        bytes.CopyTo(_storage.AsSpan(_byteCount));
        _byteCount += bytes.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int additionalBytes)
    {
        if (_byteCount + additionalBytes > _storage.Length)
        {
            int newSize = Math.Max(_storage.Length * 2, _byteCount + additionalBytes);
            Array.Resize(ref _storage, newSize);
        }
    }
}
