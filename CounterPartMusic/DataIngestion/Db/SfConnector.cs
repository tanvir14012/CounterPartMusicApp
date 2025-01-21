using Microsoft.Extensions.Logging;
using Snowflake.Data.Client;
using System.Collections.Concurrent;

namespace DataIngestion.Db
{
    public class SfConnector
    {
        private const int MaxConnections = 5; // Limit to 5 concurrent uploads
        private readonly string connectionString;
        private readonly ILogger logger;
        private static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(MaxConnections);

        public SfConnector(string connString, ILogger logger)
        {
            connectionString = connString;
            this.logger = logger;
        }

        public async Task<bool> CreateSchemaAsync(string name)
        {
            using (var connection = new SnowflakeDbConnection(connectionString))
            {
                try
                {

                    await connection.OpenAsync();

                    var commandTxt = $"CREATE SCHEMA IF NOT EXISTS {name};";

                    using (var command = new SnowflakeDbCommand(connection, commandTxt))
                    {
                        await command.ExecuteNonQueryAsync();
                    }

                    await connection.CloseAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error creating a schema in Snowflake {MethodName}", nameof(CreateStageAsync));
                    return false;
                }

            }
            return true;
        }

        public async Task<bool> CreateTablesAsync(string schemaNm)
        {
            using (var connection = new SnowflakeDbConnection(connectionString))
            {
                try
                {

                    await connection.OpenAsync();

                    var commandTxt = @$"
                            BEGIN 
                               CREATE TABLE IF NOT EXISTS {schemaNm}.ALTERNATIVE_WORK_TITLES LIKE MASTER.ALTERNATIVE_WORK_TITLES;
                               CREATE TABLE IF NOT EXISTS {schemaNm}.PARTIES LIKE MASTER.PARTIES;
                               CREATE TABLE IF NOT EXISTS {schemaNm}.RECORDINGS LIKE MASTER.RECORDINGS;
                               CREATE TABLE IF NOT EXISTS {schemaNm}.RECORDING_IDENTIFIERS LIKE MASTER.RECORDING_IDENTIFIERS;
                               CREATE TABLE IF NOT EXISTS {schemaNm}.RELEASES LIKE MASTER.RELEASES;
                               CREATE TABLE IF NOT EXISTS {schemaNm}.RELEASE_IDENTIFIERS LIKE MASTER.RELEASE_IDENTIFIERS;
                               CREATE TABLE IF NOT EXISTS {schemaNm}.UNCLAIMED_WORKS LIKE MASTER.UNCLAIMED_WORKS;
                               CREATE TABLE IF NOT EXISTS {schemaNm}.WORKS LIKE MASTER.WORKS;
                               CREATE TABLE IF NOT EXISTS {schemaNm}.WORK_IDENTIFIERS LIKE MASTER.WORK_IDENTIFIERS;
                               CREATE TABLE IF NOT EXISTS {schemaNm}.WORK_RECORDINGS LIKE MASTER.WORK_RECORDINGS;
                               CREATE TABLE IF NOT EXISTS {schemaNm}.WORK_RIGHT_SHARES LIKE MASTER.WORK_RIGHT_SHARES;
                            END;";

                    using (var command = new SnowflakeDbCommand(connection, commandTxt))
                    {
                        await command.ExecuteNonQueryAsync();
                    }

                    await connection.CloseAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error creating a schema in Snowflake {MethodName}", nameof(CreateStageAsync));
                    return false;
                }

            }
            return true;
        }

        public async Task UploadFilesToSfStageAndLoad(string dirPath, string filePrefix, string sfTableNm, string schemaNm)
        {
            sfTableNm = $"{schemaNm}.{sfTableNm}";
            var files = new ConcurrentQueue<string>();

            // Populate the queue with file paths to upload
            if (!Directory.Exists(dirPath))
                return;

            var tsvFiles = Directory.GetFiles(dirPath, $"{filePrefix}*.tsv");

            if(tsvFiles.Length < 1)
                return;
            var stageNm = $"{schemaNm}.{filePrefix.ToUpper()}";
            var stageCreated = await CreateStageAsync(stageNm);

            if (!stageCreated)
                return;

            foreach (string filePath in tsvFiles)
            {
                files.Enqueue(filePath);
            }


            var now = DateTime.Now;

            // Start processing files with a limited number of concurrent tasks
            var uploadTasks = new List<Task>();
            while (!files.IsEmpty)
            {
                if (Semaphore.CurrentCount > 0 && files.TryDequeue(out string file))
                {
                    Semaphore.Wait(); // Reserve a slot
                    uploadTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await UploadFileAndLoadAsync(file, connectionString, stageNm, sfTableNm);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, $"Error uploading file {file}");
                        }
                        finally
                        {
                            Semaphore.Release(); // Release the slot
                        }
                    }));
                }

                // Clean up completed tasks to prevent memory overhead
                uploadTasks.RemoveAll(task => task.IsCompleted);
            }

            // Wait for all remaining tasks to finish
            await Task.WhenAll(uploadTasks);

            var timeTook = (DateTime.Now - now).TotalHours;
            logger.LogInformation($"All {filePrefix} files uploaded. It took {timeTook} hours.");
        }

        private async Task<bool> CreateStageAsync(string name)
        {
            using (var connection = new SnowflakeDbConnection(connectionString))
            {
                try
                {

                    await connection.OpenAsync();

                    var commandTxt = $"CREATE STAGE IF NOT EXISTS {name} FILE_FORMAT = (FORMAT_NAME = 'MASTER.TSV_FORMAT');";

                    using (var command = new SnowflakeDbCommand(connection, commandTxt))
                    {
                        command.ExecuteNonQuery();
                    }

                    await connection.CloseAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error creating a stage in Snowflake {MethodName}", nameof(CreateStageAsync));
                    return false;
                }

            }
            return true;
        }

        private async Task UploadFileAndLoadAsync(string filePath, string connectionString, string stageNm, string sfTableNm)
        {
            using (var connection = new SnowflakeDbConnection(connectionString))
            {
                try
                {
                    
                    await connection.OpenAsync();

                    var putCommand = $"PUT file://{filePath} @{stageNm} AUTO_COMPRESS=TRUE";

                    using (var command = new SnowflakeDbCommand(connection, putCommand))
                    {
                        command.CommandTimeout = 3600;
                        command.ExecuteNonQuery();
                    }

                    var fileName = Path.GetFileName(filePath) + ".gz";
                    var copyCommand = $"COPY INTO {sfTableNm} from @{stageNm}/{fileName} FILE_FORMAT = (FORMAT_NAME = 'MASTER.TSV_FORMAT') ON_ERROR = CONTINUE ENFORCE_LENGTH = FALSE PURGE = TRUE;";
                    using (var command = new SnowflakeDbCommand(connection, copyCommand))
                    {
                        command.CommandTimeout = 3600;
                        await command.ExecuteNonQueryAsync();
                    }

                    await connection.CloseAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error uploading to Snowflake: {MethodName}.", nameof(UploadFileAndLoadAsync));
                }
                
            }
        }
    }

}
