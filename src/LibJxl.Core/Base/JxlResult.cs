// Port of lib/jxl/base/status.h — StatusOr<T>
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace LibJxl.Base;

/// <summary>
/// A result type that holds either a value or an error status code.
/// Port of jxl::StatusOr&lt;T&gt; from status.h.
/// </summary>
public readonly struct JxlResult<T>
{
    private readonly T? _value;
    private readonly StatusCode _code;

    public JxlResult(StatusCode code)
    {
        Debug.Assert(code != StatusCode.Ok);
        _code = code;
        _value = default;
    }

    public JxlResult(JxlStatus status) : this(status.Code) { }

    public JxlResult(T value)
    {
        _code = StatusCode.Ok;
        _value = value;
    }

    public bool IsOk
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _code == StatusCode.Ok;
    }

    public JxlStatus Status => new(_code);

    /// <summary>
    /// Gets the value. Only call when IsOk is true.
    /// Equivalent to StatusOr::value_().
    /// </summary>
    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(IsOk, "Accessing value of failed JxlResult");
            return _value!;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator JxlResult<T>(T value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator JxlResult<T>(StatusCode code) => new(code);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator JxlResult<T>(JxlStatus status) => new(status);
}
