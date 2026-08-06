const fs = require("fs");
const path = require("path");
const { splitTsvIfNeeded } = require("../utils/tsvSplitter");
const { sanitizeObjectRef } = require("../utils/sql");

function createSnapshotLoader(deps) {
  const {
    snowflakeClient,
    sftpService,
    config,
    logger,
    tableMap,
    configRepository
  } = deps;

  async function ensureStage(stageName) {
    const safeStage = sanitizeObjectRef(stageName);
    await snowflakeClient.execute(`CREATE STAGE IF NOT EXISTS ${safeStage} FILE_FORMAT = (FORMAT_NAME = '${config.snowflake.stageFileFormat}')`);
  }

  async function putAndCopy(filePath, stageName, qualifiedTableName) {
    const normalized = path.resolve(filePath).replace(/\\/g, "/");
    const putSql = `PUT 'file://${normalized}' @${stageName} AUTO_COMPRESS=TRUE`;
    await snowflakeClient.execute(putSql);

    const gzName = `${path.basename(filePath)}.gz`;
    const copySql = `COPY INTO ${qualifiedTableName} FROM @${stageName}/${gzName} FILE_FORMAT = (FORMAT_NAME = '${config.snowflake.stageFileFormat}') ON_ERROR = CONTINUE ENFORCE_LENGTH = FALSE PURGE = TRUE`;
    await snowflakeClient.execute(copySql);
  }

  async function runWithConcurrency(items, worker, max) {
    let index = 0;
    const runners = Array.from({ length: Math.min(max, items.length) }, async () => {
      while (index < items.length) {
        const current = index;
        index += 1;
        await worker(items[current]);
      }
    });
    await Promise.all(runners);
  }

  function clearFiles(dirPath) {
    if (!fs.existsSync(dirPath)) {
      return;
    }
    for (const name of fs.readdirSync(dirPath)) {
      const full = path.join(dirPath, name);
      if (fs.statSync(full).isFile()) {
        fs.unlinkSync(full);
      }
    }
  }

  async function loadSnapshotTable(snapshotName, rawFileName, schemaName, isReload) {
    const tableName = tableMap[rawFileName];
    if (!tableName) {
      throw new Error(`No Snowflake table map found for raw file: ${rawFileName}`);
    }

    const localDir = path.join(config.sftp.localPath, snapshotName);
    fs.mkdirSync(localDir, { recursive: true });

    const downloadStarted = Date.now();
    const downloadedFile = await sftpService.downloadSnapshotFile(snapshotName, rawFileName, localDir, config.runtime.retryCount);
    const downloadTimeMin = ((Date.now() - downloadStarted) / 60000).toFixed(2);

    const fileSizeMb = (fs.statSync(downloadedFile).size / (1024 * 1024)).toFixed(2);
    const chunks = await splitTsvIfNeeded(downloadedFile, config.runtime.chunkSizeBytes, config.runtime.splitBufferBytes);

    const stageName = `${sanitizeObjectRef(schemaName)}.${rawFileName.toUpperCase()}`;
    const qualifiedTable = `${sanitizeObjectRef(schemaName)}.${sanitizeObjectRef(tableName)}`;
    await ensureStage(stageName);

    const loadStarted = Date.now();
    await runWithConcurrency(chunks, async (chunkFile) => {
      await putAndCopy(chunkFile, stageName, qualifiedTable);
    }, config.snowflake.maxParallelUploads);

    const loadTimeMin = ((Date.now() - loadStarted) / 60000).toFixed(2);
    clearFiles(localDir);

    await configRepository.insertRunMetric({
      snapshotName,
      tableName,
      schemaName,
      isReload,
      rawFileSizeMb: fileSizeMb,
      downloadTimeMin,
      loadTimeMin
    });

    logger.info(`Loaded ${rawFileName} into ${qualifiedTable} from snapshot ${snapshotName}`);
  }

  return {
    loadSnapshotTable
  };
}

module.exports = {
  createSnapshotLoader
};
