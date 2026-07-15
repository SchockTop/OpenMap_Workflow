using OpenMapUnifier.Raster;
using Xunit;

namespace OpenMapUnifier.Tests;

public class GeoTiffReaderTests
{
    private static float[] MakeRamp(int width, int height)
    {
        var data = new float[width * height];
        for (var i = 0; i < data.Length; i++)
            data[i] = 300f + i * 0.25f;
        return data;
    }

    [Theory]
    [InlineData(TiffTestWriter.Compression.None)]
    [InlineData(TiffTestWriter.Compression.Lzw)]
    [InlineData(TiffTestWriter.Compression.Deflate)]
    public void Read_RoundTripsAllCompressions(TiffTestWriter.Compression compression)
    {
        var data = MakeRamp(20, 15);
        var tiff = TiffTestWriter.WriteFloat32(data, 20, 15, 729_000, 5_434_000, 1.0, compression);

        var grid = GeoTiffReader.Read(tiff);

        Assert.Equal(20, grid.Width);
        Assert.Equal(15, grid.Height);
        Assert.Equal(data, grid.Data);
        Assert.Equal(729_000, grid.OriginEasting);
        Assert.Equal(5_434_000, grid.OriginNorthing);
        Assert.Equal(1.0, grid.PixelSize);
        Assert.Equal(-9999f, grid.NoDataValue);
    }

    [Fact]
    public void Read_LzwSurvivesRepetitiveData()
    {
        // Long runs exercise the LZW dictionary (KwKwK cases and code growth).
        var data = new float[64 * 64];
        for (var i = 0; i < data.Length; i++)
            data[i] = i % 7 == 0 ? 5f : 1f;
        var tiff = TiffTestWriter.WriteFloat32(data, 64, 64, 0, 64, 1.0,
            TiffTestWriter.Compression.Lzw, rowsPerStrip: 64);

        var grid = GeoTiffReader.Read(tiff);
        Assert.Equal(data, grid.Data);
    }

    [Fact]
    public void Read_LzwSurvivesCodeWidthTransitions()
    {
        // Noisy data grows the dictionary ~1 code per byte, forcing the
        // 9->10->11->12-bit width switches that short real-world strips
        // (500 rows x 2) never reach.
        var data = new float[100 * 100];
        uint state = 12345;
        for (var i = 0; i < data.Length; i++)
        {
            state = state * 1664525 + 1013904223;
            data[i] = 200f + (state >> 8) % 5000 * 0.01f;
        }
        var tiff = TiffTestWriter.WriteFloat32(data, 100, 100, 0, 100, 1.0,
            TiffTestWriter.Compression.Lzw, rowsPerStrip: 100);

        var grid = GeoTiffReader.Read(tiff);
        Assert.Equal(data, grid.Data);
    }

    [Fact]
    public void Read_ParsesCustomNoData()
    {
        var tiff = TiffTestWriter.WriteFloat32(MakeRamp(4, 4), 4, 4, 0, 4, 1.0,
            noData: "-32768");
        Assert.Equal(-32768f, GeoTiffReader.Read(tiff).NoDataValue);
    }

    [Fact]
    public void Read_RejectsNonTiff()
    {
        Assert.Throws<InvalidDataException>(() => GeoTiffReader.Read("NOTATIFF"u8.ToArray()));
    }
}
