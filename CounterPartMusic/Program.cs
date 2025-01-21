using CounterPartMusic;
using CounterPartMusic.DataIngestion.Utility;
using CounterPartMusic.Extensions;
using DataIngestion.Db;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text.RegularExpressions;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File("logs/counterpartmusic.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 15)
    .CreateLogger();

// Setup Dependency Injection
var serviceProvider = new ServiceCollection()
    .AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true))
    .BuildServiceProvider();

// Get the Logger service
var logger = serviceProvider.GetService<ILogger<Program>>();

var builder = new ConfigurationBuilder()
           .SetBasePath(Directory.GetCurrentDirectory())
           .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

IConfiguration configuration = builder.Build();

var dbConn = configuration.GetSection("ConnectionStrings:DefaultConnection").Value;
Dictionary<string, string> appSettings = new();

while(true)
{
    try
    {
        var configDataReader = new ConfigDataReader(dbConn, logger);
        appSettings = await configDataReader.GetAppSettingsAsync();

        if(!appSettings.Any())
        {
            Thread.Sleep(1000 * 60 * 5); //5 minutes
            continue;
        }

        var sftpSettings = SftpSettingsParser.Parse(appSettings);
        var sftpReader = new SftpReader(sftpSettings, logger);
        var lastSnap = sftpReader.ReadLastSnapshotFromSftp();

        var lastSyncTime = await configDataReader.ReadLastSyncTimeAsync(lastSnap);
        var sfConnector = new SfConnector(dbConn, logger);

        var autoUpdateSettings = appSettings["AutoSnapshotUpdate"];
        bool autoUpdateEnabled = false;
        if (!string.IsNullOrWhiteSpace(autoUpdateSettings) && autoUpdateSettings.ToLower().Trim().Equals("on"))
            autoUpdateEnabled = true;


        //Regular Snapshot update
        if (autoUpdateEnabled && lastSyncTime.Item1 is null)
        {
            var schemaNm = lastSnap.Split('_').Last().ForgivingSubstring(0, 8);
            if(string.IsNullOrWhiteSpace(schemaNm))
            {
                schemaNm = $"{DateTime.Now.Year}{DateTime.Now.Month}{DateTime.Now.Date}";
            }
            schemaNm = "SNAP_" + schemaNm;

            var schemaCreated = await sfConnector.CreateSchemaAsync(schemaNm);
            var tablesCreated = await sfConnector.CreateTablesAsync(schemaNm);
            if (!schemaCreated || !tablesCreated)
            {
                Thread.Sleep(1000 * 60 * 5); //5 minutes
                continue;
            }

            //Remove previous directories
            if(Directory.Exists(sftpSettings.LocalPath))
            {
                var allSnapshots = Directory.GetDirectories(sftpSettings.LocalPath);
                foreach (var snapshot in allSnapshots)
                {
                    Directory.Delete(snapshot, true);
                }

            }

            //Create snap directory
            var localPath = Path.Combine(sftpSettings.LocalPath, lastSnap);
            if (!Directory.Exists(localPath))
            {
                Directory.CreateDirectory(localPath);
            }
            
            var snapshotDownloader = new SnapshotDownloader(sftpSettings, logger);
            var dbLoader = new SnapshotToDbLoader(logger, localPath, dbConn, snapshotDownloader, sfConnector);
            await dbLoader.LoadAsync("unclaimedworkrightshares", schemaNm, false);
            await dbLoader.LoadAsync("recordings", schemaNm, false);
            await dbLoader.LoadAsync("works", schemaNm, false);
            await dbLoader.LoadAsync("workrightshares", schemaNm, false);
            await dbLoader.LoadAsync("workalternativetitles", schemaNm, false);
            await dbLoader.LoadAsync("releases", schemaNm, false);
            await dbLoader.LoadAsync("workidentifiers", schemaNm, false);
            await dbLoader.LoadAsync("worksrecordings", schemaNm, false);
            await dbLoader.LoadAsync("parties", schemaNm, false);
            await dbLoader.LoadAsync("releaseidentifiers", schemaNm, false);
            await dbLoader.LoadAsync("recordingidentifiers", schemaNm, false);
        }

        //Table reload
        var tablesToReload = appSettings["TablesToReloadImmediately"];
        if(!string.IsNullOrWhiteSpace(tablesToReload))
        {
            var tableNames = tablesToReload.Split(',');
            foreach (var tableName in tableNames)
            {
                var schemaTableNm = tableName.Split('.');
                if(schemaTableNm.Length > 1)
                {
                    var timestamp = schemaTableNm[0].Split('_').Last();
                    if(timestamp.Length == 8 && new Regex(@"^\d{4}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])$").Match(timestamp).Success)
                    {
                        if (ConfigurationOptions.ReversedTableMap.ContainsKey(schemaTableNm[1].ToUpper().Trim()))
                        {
                            var snapshot = sftpReader.GetSnapshotByPattern(timestamp);
                            if (snapshot != null)
                            {
                                //Remove previous directories
                                var allSnapshots = Directory.GetDirectories(sftpSettings.LocalPath);
                                foreach (var snapshotDir in allSnapshots)
                                {
                                    Directory.Delete(snapshotDir, true);
                                }

                                //Create snap directory
                                var localPath = Path.Combine(sftpSettings.LocalPath, snapshot);
                                if (!Directory.Exists(localPath))
                                {
                                    Directory.CreateDirectory(localPath);
                                }

                                var snapshotDownloader = new SnapshotDownloader(sftpSettings, logger);
                                var dbLoader = new SnapshotToDbLoader(logger, localPath, dbConn, snapshotDownloader, sfConnector);
                                var rawFileNm = ConfigurationOptions.ReversedTableMap[schemaTableNm[1].ToUpper().Trim()];
                                await dbLoader.LoadAsync(rawFileNm, schemaTableNm[0].Trim().ToUpper(), true);
                            }
                        }
                    }
                }

            }

            await configDataReader.ResetTableReloadSettingsAsync();
        }


    }
    catch(Exception ex)
    {
        logger.LogError(ex, "An unexpected error occurred in the main loop {MethodName}", nameof(Program));
    }

    Thread.Sleep(1000);
    double.TryParse(appSettings["SnapshotCheckIntervalInHours"], out double interval);
    if (interval == 0)
        interval = 1 * 62;

    // Wait for the configured hours
    await Task.Delay(TimeSpan.FromHours(interval));
}