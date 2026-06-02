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
            """;

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
