namespace SubtitleGuardian.Infrastructure.Storage;

public sealed class TempFileStore
{
    private readonly AppPaths _paths;

    public TempFileStore(AppPaths paths)
    {
        _paths = paths;
    }

    public string CreateTempFilePath(string extensionWithDot)
    {
        if (string.IsNullOrWhiteSpace(extensionWithDot) || !extensionWithDot.StartsWith('.'))
        {
            throw new ArgumentException("extension must start with '.'", nameof(extensionWithDot));
        }

        _paths.EnsureCreated();

        string fileName = $"{Guid.NewGuid():N}{extensionWithDot}";
        return Path.Combine(_paths.Temp, fileName);
    }
}

