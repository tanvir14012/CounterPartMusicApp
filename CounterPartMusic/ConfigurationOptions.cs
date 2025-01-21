namespace CounterPartMusic
{
    public static class ConfigurationOptions
    {
        public static readonly int BufferSize = 32 * 1024 * 1024;
        public static readonly int ChunkSizeInBytes = 240 * 1024 * 1024;
        public static readonly int MaxLeftoverLineInBytes = 5099;

        public static readonly Dictionary<string, string> TableMap = new Dictionary<string, string>{
            {"parties", "PARTIES"},
            {"recordingidentifiers", "RECORDING_IDENTIFIERS" },
            {"recordings", "RECORDINGS" },
            {"releaseidentifiers", "RELEASE_IDENTIFIERS" },
            {"releases", "RELEASES" },
            {"unclaimedworkrightshares", "UNCLAIMED_WORKS" },
            {"workalternativetitles", "ALTERNATIVE_WORK_TITLES" },
            {"workidentifiers", "WORK_IDENTIFIERS" },
            {"worksrecordings", "WORK_RECORDINGS" },
            {"workrightshares", "WORK_RIGHT_SHARES" },
            {"works", "WORKS" }
        };

        public static readonly Dictionary<string, string> ReversedTableMap = TableMap
        .ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    }
}
