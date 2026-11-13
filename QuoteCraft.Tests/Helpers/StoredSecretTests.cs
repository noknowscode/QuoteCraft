using QuoteCraft.Helpers;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

/// <summary>
/// The database key and the sign-in session: in the vault on Android, iOS and WinAppSDK, in a file on desktop, and an
/// earlier version's file moved into the vault (docs/DECISIONS-production-readiness.md, D13).
/// </summary>
public sealed class StoredSecretTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "quotecraft-tests", Guid.NewGuid().ToString("N"));
    private readonly MemoryVault _vault = new();

    private string KeyFile => Path.Combine(_folder, ".dbkey");

    private StoredSecret WithVault() => new("database-key", KeyFile, _vault);

    private StoredSecret WithoutVault() => new("database-key", KeyFile, null);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Without_a_vault_the_key_is_created_once_in_its_file()
    {
        var created = WithoutVault().GetOrCreate(() => "first");
        var again = WithoutVault().GetOrCreate(() => "second");

        Assert.Equal("first", created);
        Assert.Equal("first", again);
        Assert.Equal("first", File.ReadAllText(KeyFile));
    }

    [Fact]
    public void With_a_vault_a_new_key_goes_to_the_vault_and_no_file_is_written()
    {
        var created = WithVault().GetOrCreate(() => "first");
        var again = WithVault().GetOrCreate(() => "second");

        Assert.Equal("first", created);
        Assert.Equal("first", again);
        Assert.Equal("first", _vault.Read("database-key"));
        Assert.False(File.Exists(KeyFile));
    }

    [Fact]
    public void An_existing_key_file_moves_into_the_vault_and_is_deleted()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(KeyFile, "from-file\n");

        var key = WithVault().GetOrCreate(() => "new");

        Assert.Equal("from-file", key);
        Assert.Equal("from-file", _vault.Read("database-key"));
        Assert.False(File.Exists(KeyFile));
        Assert.Equal("from-file", WithVault().GetOrCreate(() => "new"));
    }

    [Fact]
    public void A_key_file_wins_over_a_different_value_already_in_the_vault()
    {
        // The file belongs to the database beside it, which a value left in the vault (say, by an earlier install whose
        // Keychain entry outlived it) cannot open.
        _vault.Write("database-key", "stale");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(KeyFile, "from-file");

        Assert.Equal("from-file", WithVault().GetOrCreate(() => "new"));
        Assert.Equal("from-file", _vault.Read("database-key"));
        Assert.False(File.Exists(KeyFile));
    }

    [Fact]
    public void The_file_stays_when_the_vault_does_not_give_the_value_back()
    {
        var forgetful = new MemoryVault { KeepsWrites = false };
        Directory.CreateDirectory(_folder);
        File.WriteAllText(KeyFile, "from-file");

        var key = new StoredSecret("database-key", KeyFile, forgetful).GetOrCreate(() => "new");

        Assert.Equal("from-file", key);
        Assert.True(File.Exists(KeyFile));
    }

    [Fact]
    public void Delete_removes_the_secret_from_the_vault_and_any_file()
    {
        WithVault().Write("session");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(KeyFile, "left-over");

        WithVault().Delete();

        Assert.Null(_vault.Read("database-key"));
        Assert.False(File.Exists(KeyFile));
        Assert.Null(WithVault().Read());
    }

    private sealed class MemoryVault : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public bool KeepsWrites { get; init; } = true;

        public string? Read(string name) => _values.GetValueOrDefault(name);

        public void Write(string name, string value)
        {
            if (KeepsWrites)
            {
                _values[name] = value;
            }
        }

        public void Delete(string name) => _values.Remove(name);
    }
}
