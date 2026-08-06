const fs = require("fs");
const path = require("path");
const { loadStaticConfig } = require("./config/env");
const { createLogger } = require("./utils/logger");
const { TABLE_MAP, REVERSED_TABLE_MAP } = require("./constants/tableMap");
const { createSnowflakeClient } = require("./services/snowflakeClient");
const { createConfigRepository } = require("./services/configRepository");
const { createSftpService } = require("./services/sftpService");
const { createSnapshotLoader } = require("./services/snapshotLoader");

const RAW_LOAD_ORDER = [
  "unclaimedworkrightshares",
  "recordings",
  "works",
  "workrightshares",
  "workalternativetitles",
  "releases",
  "workidentifiers",
  "worksrecordings",
  "parties",
  "releaseidentifiers",
  "recordingidentifiers"
];

function parseAutoUpdate(value) {
  return String(value || "").trim().toLowerCase() === "on";
}

function parseIntervalHours(value) {
  const parsed = Number.parseFloat(String(value || ""));
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 1;
}

function snapshotSchemaFromName(snapshotName) {
  const stamp = snapshotName.split("_").at(-1).slice(0, 8);
  if (/^\d{8}$/.test(stamp)) {
    return `SNAP_${stamp}`;
  }
  const date = new Date();
  const yyyy = date.getUTCFullYear();
  const mm = String(date.getUTCMonth() + 1).padStart(2, "0");
  const dd = String(date.getUTCDate()).padStart(2, "0");
  return `SNAP_${yyyy}${mm}${dd}`;
}

function clearSnapshotDirectories(localRoot) {
  if (!fs.existsSync(localRoot)) {
    return;
  }
  for (const name of fs.readdirSync(localRoot)) {
    const full = path.join(localRoot, name);
    if (fs.statSync(full).isDirectory()) {
      fs.rmSync(full, { recursive: true, force: true });
    }
  }
}

async function runMainLoop() {
  const config = loadStaticConfig();
  const logger = createLogger(config.runtime.logFilePath);

  fs.mkdirSync(config.sftp.localPath, { recursive: true });

  const snowflakeClient = createSnowflakeClient(config.snowflake, logger);
  const configRepository = createConfigRepository(snowflakeClient, logger);
  const sftpService = createSftpService(config.sftp, logger);
  const snapshotLoader = createSnapshotLoader({
    snowflakeClient,
    sftpService,
    config,
    logger,
    tableMap: TABLE_MAP,
    configRepository
  });

  while (true) {
    let appSettings = {};

    try {
      appSettings = await configRepository.readAppSettings();
      if (!Object.keys(appSettings).length) {
        logger.warn("MASTER.APPSETTINGS returned no rows; sleeping 5 minutes");
        await new Promise((resolve) => setTimeout(resolve, 5 * 60 * 1000));
        continue;
      }

      const latestSnapshot = await sftpService.readLatestSnapshotName();
      const { lastSyncTime } = await configRepository.readLastSyncTime(latestSnapshot);

      if (parseAutoUpdate(appSettings.AutoSnapshotUpdate) && !lastSyncTime) {
        const schemaName = snapshotSchemaFromName(latestSnapshot);
        await configRepository.ensureSnapshotSchemaAndTables(schemaName);

        clearSnapshotDirectories(config.sftp.localPath);

        for (const rawName of RAW_LOAD_ORDER) {
          await snapshotLoader.loadSnapshotTable(latestSnapshot, rawName, schemaName, false);
        }
      }

      const tablesToReload = String(appSettings.TablesToReloadImmediately || "").trim();
      if (tablesToReload) {
        const entries = tablesToReload.split(",").map((x) => x.trim()).filter(Boolean);

        for (const entry of entries) {
          const parts = entry.split(".");
          if (parts.length !== 2) {
            logger.warn(`Skipping invalid table reload entry: ${entry}`);
            continue;
          }

          const schemaName = parts[0].toUpperCase();
          const tableName = parts[1].toUpperCase();
          const stamp = schemaName.split("_").at(-1);
          if (!/^\d{8}$/.test(stamp)) {
            logger.warn(`Skipping reload entry with invalid schema timestamp: ${entry}`);
            continue;
          }

          const rawName = REVERSED_TABLE_MAP[tableName];
          if (!rawName) {
            logger.warn(`Skipping reload entry with unknown table mapping: ${entry}`);
            continue;
          }

          const snapshotName = await sftpService.getSnapshotByDateTag(stamp);
          if (!snapshotName) {
            logger.warn(`No snapshot found on SFTP for ${stamp}; skipping ${entry}`);
            continue;
          }

          clearSnapshotDirectories(config.sftp.localPath);
          await snapshotLoader.loadSnapshotTable(snapshotName, rawName, schemaName, true);
        }

        await configRepository.resetTableReloadSettings();
      }
    } catch (error) {
      console.error(error);
    }

    const intervalHours = parseIntervalHours(appSettings.SnapshotCheckIntervalInHours);
    await new Promise((resolve) => setTimeout(resolve, Math.round(intervalHours * 60 * 60 * 1000)));
  }
}

runMainLoop().catch((error) => {
  console.error(error);
  process.exit(1);
});
