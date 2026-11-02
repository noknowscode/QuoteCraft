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

    /// <summary>Runs SQL straight on the database (to put it back to an older shape).</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var connection = await Database.OpenConnectionAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A new <see cref="AppDatabase"/> on the same file, as the next app start would open it: migrations missing from
    /// schema_version run again.
    /// </summary>
    public QuoteRepository Reopen() => new(new AppDatabase(Folder), Substitute.For<IPhotoService>());

    public void Dispose()
    {
        // Close this database's pooled connections only: clearing every pool would make the tests running beside this
        // one derive their SQLCipher keys again.
        using (var connection = Database.OpenConnectionAsync().GetAwaiter().GetResult())
        {
            SqliteConnection.ClearPool(connection);
        }

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
