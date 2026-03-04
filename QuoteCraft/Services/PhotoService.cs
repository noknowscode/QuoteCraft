namespace QuoteCraft.Services;

public interface IPhotoService
{
    string GetPhotoDirectory(string quoteId);
    List<string> GetPhotos(string quoteId);
    Task<string?> AddPhotoAsync(string quoteId, string sourceFilePath);
    void DeletePhoto(string photoPath);
    void DeletePhotosForQuote(string quoteId);
    int MaxPhotos => 5;
}

public class PhotoService : IPhotoService
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuoteCraft", "photos");

    public int MaxPhotos => 5;

    public string GetPhotoDirectory(string quoteId)
    {
        var dir = Path.Combine(BaseDir, quoteId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public List<string> GetPhotos(string quoteId)
    {
        var dir = Path.Combine(BaseDir, quoteId);
        if (!Directory.Exists(dir))
            return [];

        return Directory.GetFiles(dir, "*.*")
            .Where(f => IsImageFile(f))
            .OrderBy(f => File.GetCreationTimeUtc(f))
            .ToList();
    }

    public async Task<string?> AddPhotoAsync(string quoteId, string sourceFilePath)
    {
        var existing = GetPhotos(quoteId);
        if (existing.Count >= MaxPhotos)
            return null;

        var dir = GetPhotoDirectory(quoteId);
        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var destPath = Path.Combine(dir, fileName);

        await Task.Run(() => File.Copy(sourceFilePath, destPath, overwrite: true));

        return destPath;
    }

    public void DeletePhoto(string photoPath)
    {
        if (File.Exists(photoPath))
            File.Delete(photoPath);
    }

    public void DeletePhotosForQuote(string quoteId)
    {
        var dir = Path.Combine(BaseDir, quoteId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp";
    }
}
