using CounterPartMusic.Extensions;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace CounterPartMusic
{
    public class SftpReader
    {
        private readonly SftpSettings _settings;
        private readonly ILogger _logger;
        private List<string> _snapshots;

        public SftpReader(SftpSettings settings,
            ILogger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public string ReadLastSnapshotFromSftp()
        {
            var privateKeyFile = new PrivateKeyFile(_settings.PrivateKeyPath);

            var connectionInfo = new ConnectionInfo(_settings.Host, _settings.Username, new AuthenticationMethod[]
            {
                new PrivateKeyAuthenticationMethod(_settings.Username, privateKeyFile)
            });

            using (var sftp = new SftpClient(connectionInfo))
            {
                sftp.Connect();

                _snapshots = new List<string>();

                var filesAndDirectories = sftp.ListDirectory(_settings.RemoteRoot);

                foreach (var item in filesAndDirectories)
                {
                    if (item.IsDirectory && item.Name != "." && item.Name != ".." && item.Name.StartsWith(_settings.SnapshotPrefix))
                    {
                        _snapshots.Add(item.Name);
                    }
                }

                _snapshots.Sort();

                sftp.Disconnect();

            }

            return _snapshots.Last();
        }

        public string GetSnapshotByPattern(string yyyymmdd)
        {
            var privateKeyFile = new PrivateKeyFile(_settings.PrivateKeyPath);

            var connectionInfo = new ConnectionInfo(_settings.Host, _settings.Username, new AuthenticationMethod[]
            {
                new PrivateKeyAuthenticationMethod(_settings.Username, privateKeyFile)
            });

            string snapshot = null;

            using (var sftp = new SftpClient(connectionInfo))
            {
                sftp.Connect();

                _snapshots = new List<string>();

                var filesAndDirectories = sftp.ListDirectory(_settings.RemoteRoot);

                foreach (var item in filesAndDirectories)
                {
                    if (item.IsDirectory && item.Name != "." && item.Name != ".." && item.Name.StartsWith(_settings.SnapshotPrefix))
                    {
                        var timeStamp = item.Name.Split("_").Last().ForgivingSubstring(0, 8);
                        if (timeStamp == yyyymmdd)
                        {
                            snapshot = item.Name;
                            break;
                        }
                    }
                }

                sftp.Disconnect();

            }

            return snapshot;
        }
    }

}
