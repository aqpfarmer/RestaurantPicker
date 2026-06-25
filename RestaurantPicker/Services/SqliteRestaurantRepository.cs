using Microsoft.Data.Sqlite;
using RestaurantPicker.Models;

namespace RestaurantPicker.Services;

public sealed class SqliteRestaurantRepository : IRestaurantRepository
{
    private readonly string _connectionString;

    public SqliteRestaurantRepository(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Restaurants (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                RestaurantType TEXT NOT NULL,
                Address TEXT NULL,
                WebsiteUrl TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS RestaurantSets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS RestaurantSetMembers (
                SetId INTEGER NOT NULL,
                RestaurantId INTEGER NOT NULL,
                PRIMARY KEY (SetId, RestaurantId)
            );
            INSERT OR IGNORE INTO RestaurantSets (Id, Name) VALUES (1, 'Default');
            """;

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<RestaurantSet>> GetAllSetsAsync()
    {
        var results = new List<RestaurantSet>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM RestaurantSets ORDER BY Id;";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new RestaurantSet
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            });
        }

        return results;
    }

    public async Task<int> AddSetAsync(string name, IEnumerable<int> restaurantIds)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        var insertSetCmd = connection.CreateCommand();
        insertSetCmd.Transaction = (SqliteTransaction)transaction;
        insertSetCmd.CommandText =
            """
            INSERT INTO RestaurantSets (Name) VALUES ($name);
            SELECT last_insert_rowid();
            """;
        insertSetCmd.Parameters.AddWithValue("$name", name.Trim());

        var setId = (int)(long)(await insertSetCmd.ExecuteScalarAsync() ?? 0L);

        foreach (var restaurantId in restaurantIds)
        {
            var insertMemberCmd = connection.CreateCommand();
            insertMemberCmd.Transaction = (SqliteTransaction)transaction;
            insertMemberCmd.CommandText =
                "INSERT OR IGNORE INTO RestaurantSetMembers (SetId, RestaurantId) VALUES ($setId, $restaurantId);";
            insertMemberCmd.Parameters.AddWithValue("$setId", setId);
            insertMemberCmd.Parameters.AddWithValue("$restaurantId", restaurantId);
            await insertMemberCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return setId;
    }

    public async Task DeleteSetAsync(int setId)
    {
        if (setId == 1)
        {
            return; // Cannot delete the Default set
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        var deleteMembersCmd = connection.CreateCommand();
        deleteMembersCmd.Transaction = (SqliteTransaction)transaction;
        deleteMembersCmd.CommandText = "DELETE FROM RestaurantSetMembers WHERE SetId = $setId;";
        deleteMembersCmd.Parameters.AddWithValue("$setId", setId);
        await deleteMembersCmd.ExecuteNonQueryAsync();

        var deleteSetCmd = connection.CreateCommand();
        deleteSetCmd.Transaction = (SqliteTransaction)transaction;
        deleteSetCmd.CommandText = "DELETE FROM RestaurantSets WHERE Id = $setId;";
        deleteSetCmd.Parameters.AddWithValue("$setId", setId);
        await deleteSetCmd.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<Restaurant>> GetRestaurantsBySetAsync(int setId)
    {
        if (setId == 1)
        {
            return await GetAllAsync();
        }

        var results = new List<Restaurant>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.Id, r.Name, r.RestaurantType, r.Address, r.WebsiteUrl
            FROM Restaurants r
            INNER JOIN RestaurantSetMembers m ON r.Id = m.RestaurantId
            WHERE m.SetId = $setId
            ORDER BY r.Name;
            """;
        command.Parameters.AddWithValue("$setId", setId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new Restaurant
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                RestaurantType = reader.GetString(2),
                Address = reader.IsDBNull(3) ? null : reader.GetString(3),
                WebsiteUrl = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }

        return results;
    }

    public async Task AddRestaurantToSetAsync(int setId, int restaurantId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR IGNORE INTO RestaurantSetMembers (SetId, RestaurantId) VALUES ($setId, $restaurantId);";
        command.Parameters.AddWithValue("$setId", setId);
        command.Parameters.AddWithValue("$restaurantId", restaurantId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<Restaurant>> GetAllAsync()
    {
        var results = new List<Restaurant>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, RestaurantType, Address, WebsiteUrl
            FROM Restaurants
            ORDER BY Name;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new Restaurant
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                RestaurantType = reader.GetString(2),
                Address = reader.IsDBNull(3) ? null : reader.GetString(3),
                WebsiteUrl = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }

        return results;
    }

    public async Task<int> AddAsync(Restaurant restaurant)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Restaurants (Name, RestaurantType, Address, WebsiteUrl)
            VALUES ($name, $type, $address, $website);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", restaurant.Name.Trim());
        command.Parameters.AddWithValue("$type", restaurant.RestaurantType.Trim());
        command.Parameters.AddWithValue("$address", (object?)restaurant.Address?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$website", (object?)restaurant.WebsiteUrl?.Trim() ?? DBNull.Value);

        var insertedId = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return (int)insertedId;
    }

    public async Task UpdateAsync(Restaurant restaurant)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Restaurants
            SET Name = $name,
                RestaurantType = $type,
                Address = $address,
                WebsiteUrl = $website
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", restaurant.Id);
        command.Parameters.AddWithValue("$name", restaurant.Name.Trim());
        command.Parameters.AddWithValue("$type", restaurant.RestaurantType.Trim());
        command.Parameters.AddWithValue("$address", (object?)restaurant.Address?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$website", (object?)restaurant.WebsiteUrl?.Trim() ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Restaurants WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await command.ExecuteNonQueryAsync();
    }
}
