using LibJxl.Base;
using Xunit;

namespace LibJxl.Tests.Base;

public class StatusTests
{
    [Fact]
    public void OkStatus_IsOk()
    {
        JxlStatus s = JxlStatus.OkStatus();
        Assert.True(s.IsOk);
        Assert.True(s);
        Assert.False(s.IsFatalError);
        Assert.Equal(StatusCode.Ok, s.Code);
    }

    [Fact]
    public void BoolTrue_IsOk()
    {
        JxlStatus s = true;
        Assert.True(s.IsOk);
    }

    [Fact]
    public void BoolFalse_IsGenericError()
    {
        JxlStatus s = false;
        Assert.False(s.IsOk);
        Assert.True(s.IsFatalError);
        Assert.Equal(StatusCode.GenericError, s.Code);
    }

    [Fact]
    public void NotEnoughBytes_NonFatal()
    {
        JxlStatus s = StatusCode.NotEnoughBytes;
        Assert.False(s.IsOk);
        Assert.False(s.IsFatalError);
    }

    [Fact]
    public void GenericError_IsFatal()
    {
        JxlStatus s = StatusCode.GenericError;
        Assert.True(s.IsFatalError);
    }

    [Fact]
    public void Unsupported_IsFatal()
    {
        JxlStatus s = StatusCode.Unsupported;
        Assert.True(s.IsFatalError);
    }

    [Fact]
    public void ThrowIfError_ThrowsOnFatal()
    {
        JxlStatus s = StatusCode.GenericError;
        Assert.Throws<JxlException>(() => s.ThrowIfError());
    }

    [Fact]
    public void ThrowIfError_DoesNotThrowOnOk()
    {
        JxlStatus s = true;
        s.ThrowIfError(); // should not throw
    }
}

public class JxlResultTests
{
    [Fact]
    public void OkResult_HasValue()
    {
        JxlResult<int> r = 42;
        Assert.True(r.IsOk);
        Assert.Equal(42, r.Value);
    }

    [Fact]
    public void ErrorResult_IsNotOk()
    {
        JxlResult<int> r = StatusCode.GenericError;
        Assert.False(r.IsOk);
    }

    [Fact]
    public void ImplicitConversion_FromValue()
    {
        JxlResult<string> r = "hello";
        Assert.True(r.IsOk);
        Assert.Equal("hello", r.Value);
    }
}
