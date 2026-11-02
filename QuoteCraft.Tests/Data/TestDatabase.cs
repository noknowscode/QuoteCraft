using Microsoft.Data.Sqlite;
using NSubstitute;
using QuoteCraft.Data;
using QuoteCraft.Services;

namespace QuoteCraft.Tests.Data;

/// <summary>
/// The app's real database (SQLCipher, every migration) in a temporary folder, with the repositories over it.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public TestDatabase()
    {
        Folder = Path.Combine(Path.GetTempPath(), "quotecraft-tests", Guid.NewGuid().ToString("N"));
        Database = new AppDatabase(Folder);
        Quotes = new QuoteRepository(Database, Substitute.For<IPhotoService>());
        Clients = new ClientRepository(Database);
        Profiles = new BusinessProfileRepository(Database);
        History = new StatusHistoryRepository(Database);
    }

    public string Folder { get; }
    public AppDatabase Database { get; }
    public QuoteRepository Quotes { get; }
    public ClientRepository Clients { get; }
    public BusinessProfileRepository Profiles { get; }
    public StatusHistoryRepository History { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is harmless.
        }
    }
}
