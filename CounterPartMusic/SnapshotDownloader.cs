using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Renci.SshNet;
using System.Diagnostics;

namespace CounterPartMusic
{
    public class SnapshotDownloader
    {
        private readonly SftpSettings _settings;
        private readonly ILogger _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        public SnapshotDownloader(SftpSettings settings, ILogger logger)
        {
            _settings = settings;
            _logger = logger;

            // Configure retry policy
            _retryPolicy = Policy.Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 7,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning($"Retry {retryCount} encountered a transient error. Waiting {timeSpan} before next retry. Exception: {exception.Message}");
                    });

        }

        public async Task<int> DownloadLastSnapshotFromSftpAsync(string snapshotDir, string rawFileNm)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var privateKeyFile = new PrivateKeyFile(_settings.PrivateKeyPath);

            var connectionInfo = new ConnectionInfo(_settings.Host, _settings.Username, new AuthenticationMethod[]
            {
                new PrivateKeyAuthenticationMethod(_settings.Username, privateKeyFile)
            });

            try
            {

                using (var sftp = new SftpClient(connectionInfo))
                {
                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        sftp.Connect();
                        await _retryPolicy.ExecuteAsync(
                                    async () => await DownloadFileAsync(sftp, $"{_settings.RemoteRoot}/{snapshotDir}", $"{rawFileNm}.tsv", $"{_settings.LocalPath}/{snapshotDir}")
                                );
                        sftp.Disconnect();
                    });

                }

            }
            catch (Exception ex) {
                _logger.LogError(ex, "An error occurred in {MethodName}", nameof(DownloadLastSnapshotFromSftpAsync));
            }

            stopwatch.Stop();

            return (int)stopwatch.Elapsed.TotalMinutes;
        }

        private async Task DownloadFileAsync(SftpClient client,
            string remoteDirectory,
            string rawFileNm,
            string localDirectory)
        {
            var files = client.ListDirectory(remoteDirectory);

            foreach (var file in files)
            {
                string remoteFileName = file.Name;
                string localFilePath = Path.Combine(localDirectory, remoteFileName);

                if (!file.Name.StartsWith("."))
                {
                    if (file.IsRegularFile && rawFileNm == remoteFileName)
                    {
                        var localFileInfo = new FileInfo(localFilePath);

                        if (!localFileInfo.Exists || localFileInfo.Length < file.Length)
                        {
                            //_logger.LogInformation($"Downloading file: {remoteFileName}");

                            // Download file with retry
                            await _retryPolicy.ExecuteAsync(async () =>
                            {
                                using (Stream fileStream = File.Create(localFilePath))
                                {
                                    await Task.Run(() => client.DownloadFile(file.FullName, fileStream));
                                }
                            });
                        }
                        else
                        {
                            _logger.LogInformation($"File already exists and matches size: {remoteFileName}");
                        }
                    }
                }
            }
        }

        public void DeleteAllFiles(string directoryPath)
        {
            if (Directory.Exists(directoryPath))
            {
                // Get all files in the directory
                string[] files = Directory.GetFiles(directoryPath);

                foreach (string file in files)
                {
                    try
                    {
                        // Delete the file
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An error occurred in {MethodName}", nameof(DeleteAllFiles));
                    }
                }
            }
            else
            {
                _logger.LogInformation($"The directory {directoryPath} does not exist.");
            }
        }
    }
}
