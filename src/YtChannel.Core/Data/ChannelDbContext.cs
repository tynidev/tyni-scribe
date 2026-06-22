using Microsoft.Data.Sqlite;

namespace YtChannel.Core.Data;

/// <summary>
/// Opens and initializes the SQLite database used by yt-channel.
/// All schema creation happens here on first use.
/// </summary>
public sealed class ChannelDbContext : IAsyncDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public ChannelDbContext(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public async ValueTask<SqliteConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
            return _connection;

        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(cancellationToken);
        await EnableWalModeAsync(_connection, cancellationToken);
        return _connection;
    }

    private static async Task EnableWalModeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=30000;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
