using LibJxl.Base;
using LibJxl.Image;
using Xunit;

namespace LibJxl.Tests.Image;

public class PlaneTests
{
    [Fact]
    public void BasicCreation()
    {
        var plane = new Plane<float>(100, 50);
        Assert.Equal(100, plane.XSize);
        Assert.Equal(50, plane.YSize);
    }

    [Fact]
    public void RowAccess()
    {
        var plane = new Plane<int>(10, 5);
        var row = plane.Row(0);
        Assert.Equal(10, row.Length);
        row[3] = 42;
        Assert.Equal(42, plane[0, 3]);
    }

    [Fact]
    public void IndexerAccess()
    {
        var plane = new Plane<byte>(8, 8);
        plane[3, 5] = 0xFF;
        Assert.Equal(0xFF, plane[3, 5]);
    }

    [Fact]
    public void Fill()
    {
        var plane = new Plane<float>(10, 10);
        plane.Fill(3.14f);
        for (int y = 0; y < plane.YSize; y++)
        {
            var row = plane.ConstRow(y);
            for (int x = 0; x < plane.XSize; x++)
                Assert.Equal(3.14f, row[x]);
        }
    }

    [Fact]
    public void RectAccess()
    {
        var plane = new Plane<int>(20, 20);
        plane[5, 10] = 99;
        var rect = new Rect(10, 5, 5, 5);
        var row = plane.Row(rect, 0);
        Assert.Equal(99, row[0]);
    }

    [Fact]
    public void ShrinkTo()
    {
        var plane = new Plane<float>(100, 100);
        var smaller = plane.ShrinkTo(50, 50);
        Assert.Equal(50, smaller.XSize);
        Assert.Equal(50, smaller.YSize);
    }
}

public class Image3Tests
{
    [Fact]
    public void BasicCreation()
    {
        var img = new Image3<float>(100, 50);
        Assert.Equal(100, img.XSize);
        Assert.Equal(50, img.YSize);
    }

    [Fact]
    public void PlaneAccess()
    {
        var img = new Image3<float>(10, 10);
        img[0][2, 3] = 1.0f;
        img[1][2, 3] = 2.0f;
        img[2][2, 3] = 3.0f;

        Assert.Equal(1.0f, img[0][2, 3]);
        Assert.Equal(2.0f, img[1][2, 3]);
        Assert.Equal(3.0f, img[2][2, 3]);
    }

    [Fact]
    public void PlaneRow()
    {
        var img = new Image3<int>(5, 5);
        var row = img.PlaneRow(1, 2);
        Assert.Equal(5, row.Length);
        row[3] = 42;
        Assert.Equal(42, img[1][2, 3]);
    }
}

public class ImageOpsTests
{
    [Fact]
    public void CopyImageTo_Plane()
    {
        var src = new Plane<int>(10, 10);
        src[3, 5] = 42;
        var dst = new Plane<int>(10, 10);
        ImageOps.CopyImageTo(src, dst);
        Assert.Equal(42, dst[3, 5]);
    }

    [Fact]
    public void CopyImageTo_Image3()
    {
        var src = new Image3<float>(10, 10);
        src[0][2, 3] = 1.5f;
        src[1][4, 5] = 2.5f;
        src[2][6, 7] = 3.5f;

        var dst = new Image3<float>(10, 10);
        ImageOps.CopyImageTo(src, dst);

        Assert.Equal(1.5f, dst[0][2, 3]);
        Assert.Equal(2.5f, dst[1][4, 5]);
        Assert.Equal(3.5f, dst[2][6, 7]);
    }

    [Fact]
    public void SameSize()
    {
        var a = new Plane<int>(10, 20);
        var b = new Plane<int>(10, 20);
        var c = new Plane<int>(10, 30);
        Assert.True(ImageOps.SameSize(a, b));
        Assert.False(ImageOps.SameSize(a, c));
    }
}
