namespace CounterPartMusic
{
    public class SftpSettings
    {
        public string Host { get; set; }
        public string Username { get; set; }
        public string PrivateKeyPath { get; set; }
        public string LocalPath { get; set; }
        public string RemoteRoot { get; set; }
        public string SnapshotPrefix { get; set; }
    }

}
