namespace CounterPartMusic.DataIngestion.Utility
{
    public static class SftpSettingsParser
    {
        public static SftpSettings Parse(Dictionary<string, string> appSettings)
        {
            var sftpSettings = new SftpSettings();
            sftpSettings.Host = appSettings["Host"];
            sftpSettings.Username = appSettings["Username"];
            sftpSettings.RemoteRoot = appSettings["RemoteRoot"];
            sftpSettings.SnapshotPrefix = appSettings["SnapshotPrefix"];
            sftpSettings.LocalPath = appSettings["LocalPath"];
            sftpSettings.PrivateKeyPath = appSettings["PrivateKeyPath"];
            return sftpSettings;
        }
    }
}
