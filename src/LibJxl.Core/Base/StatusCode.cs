// Port of lib/jxl/base/status.h — StatusCode enum
namespace LibJxl.Base;

/// <summary>Status codes for JXL operations.</summary>
public enum StatusCode : int
{
    /// <summary>Non-fatal: not enough input bytes yet.</summary>
    NotEnoughBytes = -1,

    /// <summary>Success.</summary>
    Ok = 0,

    /// <summary>Fatal generic error.</summary>
    GenericError = 1,

    /// <summary>Feature not supported.</summary>
    Unsupported = 2,
}
