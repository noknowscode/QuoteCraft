using Microsoft.Data.Sqlite;
using QuoteCraft.Models;

namespace QuoteCraft.Data;

public interface ICatalogItemRepository
{
    Task<List<CatalogItemEntity>> GetAllAsync();
    Task<CatalogItemEntity?> GetByIdAsync(string id);
    Task SaveAsync(CatalogItemEntity item);
    Task DeleteAsync(string id);
    Task<List<string>> GetCategoriesAsync();
    Task<int> GetItemCountAsync();
}

public class CatalogItemRepository : ICatalogItemRepository
{
    private readonly AppDatabase _db;

    public CatalogItemRepository(AppDatabase db) => _db = db;

    public async Task<List<CatalogItemEntity>> GetAllAsync()
    {
        await _db.InitializeAsync();
        using var conn = await _db.CreateConnectionAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM catalog_items WHERE is_deleted = 0 ORDER BY category, sort_order, description";

        var items = new List<CatalogItemEntity>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(ReadItem(reader));
        }
        return items;
    }

    public async Task<CatalogItemEntity?> GetByIdAsync(string id)
    {
        await _db.InitializeAsync();
        using var conn = await _db.CreateConnectionAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM catalog_items WHERE id = @id AND is_deleted = 0";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadItem(reader);
        }
        return null;
    }

    public async Task SaveAsync(CatalogItemEntity item)
    {
        await _db.InitializeAsync();
        using var conn = await _db.CreateConnectionAsync();

        item.UpdatedAt = DateTimeOffset.UtcNow;

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO catalog_items
            (id, description, unit_price, category, sort_order, updated_at, is_deleted)
            VALUES
            (@id, @description, @unit_price, @category, @sort_order, @updated_at, @is_deleted)
            ON CONFLICT(id) DO UPDATE SET
                description=excluded.description, unit_price=excluded.unit_price,
                category=excluded.category, sort_order=excluded.sort_order,
                updated_at=excluded.updated_at, is_deleted=excluded.is_deleted
            """;
        cmd.Parameters.AddWithValue("@id", item.Id);
        cmd.Parameters.AddWithValue("@description", item.Description);
        cmd.Parameters.AddWithValue("@unit_price", (double)item.UnitPrice);
        cmd.Parameters.AddWithValue("@category", item.Category);
        cmd.Parameters.AddWithValue("@sort_order", item.SortOrder);
        cmd.Parameters.AddWithValue("@updated_at", item.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@is_deleted", item.IsDeleted ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string id)
    {
        await _db.InitializeAsync();
        using var conn = await _db.CreateConnectionAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE catalog_items SET is_deleted = 1, updated_at = @updated_at WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        await _db.InitializeAsync();
        using var conn = await _db.CreateConnectionAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT category FROM catalog_items WHERE is_deleted = 0 ORDER BY category";

        var categories = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            categories.Add(reader.GetString(0));
        }
        return categories;
    }

    public async Task<int> GetItemCountAsync()
    {
        await _db.InitializeAsync();
        using var conn = await _db.CreateConnectionAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM catalog_items WHERE is_deleted = 0";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static CatalogItemEntity ReadItem(SqliteDataReader reader)
    {
        return new CatalogItemEntity
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Description = reader.GetString(reader.GetOrdinal("description")),
            UnitPrice = (decimal)reader.GetDouble(reader.GetOrdinal("unit_price")),
            Category = reader.GetString(reader.GetOrdinal("category")),
            SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
            IsDeleted = reader.GetInt32(reader.GetOrdinal("is_deleted")) == 1
        };
    }
}
