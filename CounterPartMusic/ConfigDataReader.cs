using Microsoft.Extensions.Logging;
using Snowflake.Data.Client;

public class ConfigDataReader
{
    private readonly string _connectionString;
    private readonly ILogger _logger;

    public ConfigDataReader(string connectionString,
        ILogger logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> GetAppSettingsAsync()
    {
        var appSettings = new Dictionary<string, string>();
        using (var connection = new SnowflakeDbConnection(_connectionString))
        {
            try
            {

                await connection.OpenAsync();

                var commandTxt = $"SELECT KEY, VALUE FROM MASTER.APPSETTINGS;";

                using (var command = new SnowflakeDbCommand(connection, commandTxt))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            appSettings.TryAdd(Convert.ToString(reader["KEY"]), Convert.ToString(reader["VALUE"]));
                        }
                    }
                }

                await connection.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading appSettings in Snowflake {MethodName}", nameof(GetAppSettingsAsync));
            }

        }

        return appSettings;

    }

    public async Task ResetTableReloadSettingsAsync()
    {
        var appSettings = new Dictionary<string, string>();
        using (var connection = new SnowflakeDbConnection(_connectionString))
        {
            try
            {

                await connection.OpenAsync();

                var commandTxt = $"UPDATE MASTER.APPSETTINGS SET VALUE = NULL WHERE KEY = 'TablesToReloadImmediately';";

                using (var command = new SnowflakeDbCommand(connection, commandTxt))
                {
                    await command.ExecuteNonQueryAsync();
                }

                await connection.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading appSettings in Snowflake {MethodName}", nameof(GetAppSettingsAsync));
            }

        }

    }

    public async Task<(DateTime?, DateTime?)> ReadLastSyncTimeAsync(string snapshotNm)
    {
        DateTime? lastSyncTime = null, now = null;

        try
        {

            using (var connection = new SnowflakeDbConnection(_connectionString))
            {
                await connection.OpenAsync();

                var query = $"SELECT UPDATED_ON_DTTM, TO_TIMESTAMP_NTZ(CURRENT_TIMESTAMP) AS NOW FROM MASTER.APPLOG WHERE SNAPSHOT_NM = '{snapshotNm}' AND IS_RELOAD = FALSE ORDER BY ID DESC LIMIT 1";

                using (var command = new SnowflakeDbCommand(connection, query))
                {
                    using(var reader = await command.ExecuteReaderAsync())
                    {
                        await reader.ReadAsync();
                        lastSyncTime = (DateTime?)reader["UPDATED_ON_DTTM"];
                        now = (DateTime?)reader["NOW"];
                    }

                }

                await connection.CloseAsync();
            }

        }
        catch (Exception ex) {
            _logger.LogError(ex, "An error occurred in {MethodName}", nameof(ReadLastSyncTimeAsync));
        }

        return (lastSyncTime, now);
    }

}
