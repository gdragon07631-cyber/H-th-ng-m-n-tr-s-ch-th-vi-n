using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;

namespace Project.Services;

public sealed class BookCoverThumbnailService : IBookCoverThumbnailService
{
    private static readonly Size ThumbnailSize = new(120, 174);
    private const long MaxDecodedBytes = 128L * 1024 * 1024;

    public async Task CreateAsync(Stream source, string destinationPath, CancellationToken cancellationToken = default)
    {
        var info = await Image.IdentifyAsync(source, cancellationToken);
        if (info.GetPixelMemorySize() > MaxDecodedBytes)
            throw new InvalidImageContentException("The image dimensions exceed the thumbnail processing limit.");

        if (source.CanSeek) source.Position = 0;
        using var image = await Image.LoadAsync(new DecoderOptions { MaxFrames = 1, SkipMetadata = true }, source, cancellationToken);
        image.Mutate(operation => operation.Resize(new ResizeOptions
        {
            Size = ThumbnailSize,
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));
        await image.SaveAsync(destinationPath, cancellationToken);
    }
}
