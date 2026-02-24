// Port of lib/jxl/base/byte_order.h — endianness utilities
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibJxl.Base;

/// <summary>Endianness detection and byte-order conversion utilities.</summary>
public static class ByteOrder
{
    public static bool IsLittleEndian
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitConverter.IsLittleEndian;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort LoadBE16(ReadOnlySpan<byte> p) =>
        BinaryPrimitives.ReadUInt16BigEndian(p);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort LoadLE16(ReadOnlySpan<byte> p) =>
        BinaryPrimitives.ReadUInt16LittleEndian(p);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint LoadBE32(ReadOnlySpan<byte> p) =>
        BinaryPrimitives.ReadUInt32BigEndian(p);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint LoadLE32(ReadOnlySpan<byte> p) =>
        BinaryPrimitives.ReadUInt32LittleEndian(p);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong LoadBE64(ReadOnlySpan<byte> p) =>
        BinaryPrimitives.ReadUInt64BigEndian(p);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong LoadLE64(ReadOnlySpan<byte> p) =>
        BinaryPrimitives.ReadUInt64LittleEndian(p);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LoadBEFloat(ReadOnlySpan<byte> p)
    {
        uint u = LoadBE32(p);
        return Unsafe.As<uint, float>(ref u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LoadLEFloat(ReadOnlySpan<byte> p)
    {
        uint u = LoadLE32(p);
        return Unsafe.As<uint, float>(ref u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreBE16(ushort value, Span<byte> p) =>
        BinaryPrimitives.WriteUInt16BigEndian(p, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreLE16(ushort value, Span<byte> p) =>
        BinaryPrimitives.WriteUInt16LittleEndian(p, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreBE32(uint value, Span<byte> p) =>
        BinaryPrimitives.WriteUInt32BigEndian(p, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreLE32(uint value, Span<byte> p) =>
        BinaryPrimitives.WriteUInt32LittleEndian(p, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreBE64(ulong value, Span<byte> p) =>
        BinaryPrimitives.WriteUInt64BigEndian(p, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreLE64(ulong value, Span<byte> p) =>
        BinaryPrimitives.WriteUInt64LittleEndian(p, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BSwapFloat(float x)
    {
        uint u = Unsafe.As<float, uint>(ref x);
        u = BinaryPrimitives.ReverseEndianness(u);
        return Unsafe.As<uint, float>(ref u);
    }

    /// <summary>Whether endianness requires swapping for the given JxlEndianness.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool SwapEndianness(Common.JxlEndianness endianness) =>
        (endianness == Common.JxlEndianness.BigEndian && IsLittleEndian) ||
        (endianness == Common.JxlEndianness.LittleEndian && !IsLittleEndian);
}
