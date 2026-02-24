// JXL-specific exception for fatal errors
namespace LibJxl.Base;

public class JxlException : Exception
{
    public StatusCode StatusCode { get; }

    public JxlException(StatusCode code, string? message = null)
        : base(message ?? $"JXL error: {code}")
    {
        StatusCode = code;
    }

    public JxlException(string message) : base(message)
    {
        StatusCode = StatusCode.GenericError;
    }
}
