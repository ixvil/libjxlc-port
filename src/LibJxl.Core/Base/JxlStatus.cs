// Port of lib/jxl/base/status.h — Status class
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace LibJxl.Base;

/// <summary>
/// Drop-in replacement for bool that carries an error code.
/// Port of jxl::Status from status.h.
/// </summary>
public readonly struct JxlStatus
{
    private readonly StatusCode _code;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JxlStatus(bool ok) => _code = ok ? StatusCode.Ok : StatusCode.GenericError;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JxlStatus(StatusCode code) => _code = code;

    public StatusCode Code
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _code;
    }

    public bool IsOk
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _code == StatusCode.Ok;
    }

    public bool IsFatalError
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (int)_code > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator bool(JxlStatus s) => s.IsOk;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator JxlStatus(bool ok) => new(ok);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator JxlStatus(StatusCode code) => new(code);

    public static JxlStatus OkStatus() => new(StatusCode.Ok);

    /// <summary>Throws if this status represents a fatal error.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIfError(
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        if (IsFatalError)
            throw new JxlException(_code, $"{file}:{line}: JXL error: {_code}");
    }

    /// <summary>Returns this status, throwing if fatal (equivalent to JXL_RETURN_IF_ERROR).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JxlStatus Check(
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        if (!IsOk)
        {
            Debug.WriteLine($"{file}:{line}: JXL_RETURN_IF_ERROR code={_code}");
        }
        return this;
    }

    public override string ToString() => _code.ToString();
}
