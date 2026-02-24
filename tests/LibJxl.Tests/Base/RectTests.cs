using LibJxl.Base;
using Xunit;

namespace LibJxl.Tests.Base;

public class RectTests
{
    [Fact]
    public void BasicRect()
    {
        var r = new Rect(10, 20, 100, 200);
        Assert.Equal(10, r.X0);
        Assert.Equal(20, r.Y0);
        Assert.Equal(100, r.XSize);
        Assert.Equal(200, r.YSize);
        Assert.Equal(110, r.X1);
        Assert.Equal(220, r.Y1);
    }

    [Fact]
    public void ClampedRect()
    {
        // Begin at 50, max size 200, but end at 100 → actual size = 50
        var r = new Rect(50, 50, 200, 200, 100, 100);
        Assert.Equal(50, r.XSize);
        Assert.Equal(50, r.YSize);
    }

    [Fact]
    public void Intersection()
    {
        var a = new Rect(0, 0, 100, 100);
        var b = new Rect(50, 50, 100, 100);
        var c = a.Intersection(b);
        Assert.Equal(50, c.X0);
        Assert.Equal(50, c.Y0);
        Assert.Equal(50, c.XSize);
        Assert.Equal(50, c.YSize);
    }

    [Fact]
    public void NoIntersection()
    {
        var a = new Rect(0, 0, 10, 10);
        var b = new Rect(20, 20, 10, 10);
        var c = a.Intersection(b);
        Assert.Equal(0, c.XSize);
        Assert.Equal(0, c.YSize);
    }

    [Fact]
    public void Translate()
    {
        var r = new Rect(10, 20, 30, 40);
        var t = r.Translate(5, -10);
        Assert.Equal(15, t.X0);
        Assert.Equal(10, t.Y0);
        Assert.Equal(30, t.XSize);
        Assert.Equal(40, t.YSize);
    }

    [Fact]
    public void IsInside()
    {
        var outer = new Rect(0, 0, 100, 100);
        var inner = new Rect(10, 10, 50, 50);
        Assert.True(inner.IsInside(outer));
        Assert.False(outer.IsInside(inner));
    }

    [Fact]
    public void ToString_Format()
    {
        var r = new Rect(10, 20, 30, 40);
        Assert.Equal("[10..40)x[20..60)", r.ToString());
    }
}
