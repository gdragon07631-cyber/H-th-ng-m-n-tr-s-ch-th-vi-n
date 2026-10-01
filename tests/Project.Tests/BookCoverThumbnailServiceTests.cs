using Project.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Project.Tests;

public sealed class BookCoverThumbnailServiceTests
{
    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    public async Task Creates_thumbnail_from_supported_cover_image(string extension)
    {
        var temporaryFile = Path.Combine(Path.GetTempPath(), $"cover-thumb-{Guid.NewGuid():N}{extension}");
        await using var original = new MemoryStream();
        using (var source = new Image<Rgba32>(240, 360))
        {
            if (extension == ".jpg")
                await source.SaveAsJpegAsync(original);
            else
                await source.SaveAsPngAsync(original);
        }
        original.Position = 0;

        try
        {
            await new BookCoverThumbnailService().CreateAsync(original, temporaryFile);
            using var thumbnail = await Image.LoadAsync(temporaryFile);
            Assert.Equal(120, thumbnail.Width);
            Assert.Equal(174, thumbnail.Height);
        }
        finally
        {
            if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
        }
    }
}
