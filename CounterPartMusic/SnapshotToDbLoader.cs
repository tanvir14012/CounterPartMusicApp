using CounterPartMusic.DataIngestion.Utility;
using DataIngestion.Db;
using Microsoft.Extensions.Logging;
using Snowflake.Data.Client;
using System.Diagnostics;

namespace CounterPartMusic
{
    public class SnapshotToDbLoader
    {
        private readonly SftpSettings _settings;
        private readonly ILogger _logger;
        private readonly string _localPath;
        private readonly string _connectionString;
        private readonly string _rawFileNm;
        private readonly SnapshotDownloader _downloader;
        private readonly SfConnector _sfConnector;

        public SnapshotToDbLoader(
            ILogger logger,
            string localPath,
            string connectionString,
            SnapshotDownloader downloader,
            SfConnector sfConnector)
        {
            _logger = logger;
            _localPath = localPath;
            _connectionString = connectionString;
            _downloader = downloader;
            _sfConnector = sfConnector;
        }

        public async Task LoadAsync(string rawFileNm, string schemaNm, bool isReload = false)
        {
            try
            {
                var snapshotNm = Path.GetFileName(_localPath);
                var downloadTime = await _downloader.DownloadLastSnapshotFromSftpAsync(snapshotNm, rawFileNm);
                Thread.Sleep(1000);

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                var filePath = Path.Combine(_localPath, $"{rawFileNm}.tsv");
                var fileSize = new FileInfo(filePath).Length / (1024 * 1024);
                var tblName = ConfigurationOptions.TableMap[rawFileNm];
                DiskSavingTsvSplitter.Split(filePath);
                Thread.Sleep(1000);

                await _sfConnector.UploadFilesToSfStageAndLoad(_localPath, rawFileNm, tblName, schemaNm);
                stopwatch.Stop();
                var loadTime = stopwatch.Elapsed.TotalMinutes;
                Thread.Sleep(1000);

                //Delete the chunk files
                _downloader.DeleteAllFiles(_localPath);

                //Save to the app log
                using (var connection = new SnowflakeDbConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = $"INSERT INTO MASTER.APPLOG (UPDATED_ON_DTTM, SNAPSHOT_NM, TABLE_NM, SCHEMA_NM, IS_RELOAD, RAW_FILE_SIZE_MB, SFTP_DOWNLOADED_TIME_MINUTES, TABLE_LOAD_TIME_MINUTES) VALUES(CURRENT_TIMESTAMP, '{snapshotNm}', '{tblName}', '{schemaNm}', {isReload}, {fileSize}, {downloadTime}, {Double.Round(loadTime, 2)});";

                    using (var command = new SnowflakeDbCommand(connection, query))
                    {
                        await command.ExecuteNonQueryAsync();
                    }

                    await connection.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in {MethodName}", nameof(LoadAsync));
            }

        }
    }
}
