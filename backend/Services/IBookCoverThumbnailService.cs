namespace Project.Services;

public interface IBookCoverThumbnailService
{
    Task CreateAsync(Stream source, string destinationPath, CancellationToken cancellationToken = default);
}
