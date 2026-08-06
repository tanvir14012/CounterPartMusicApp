const fs = require("fs");
const path = require("path");
const SftpClient = require("ssh2-sftp-client");

function createSftpService(config, logger) {
  async function withClient(callback) {
    const client = new SftpClient();
    try {
      await client.connect({
        host: config.host,
        port: config.port,
        username: config.username,
        privateKey: fs.readFileSync(path.resolve(config.privateKeyPath)),
        passphrase: config.privateKeyPassphrase || undefined,
        readyTimeout: 60000,
        retries: 2
      });
      return await callback(client);
    } finally {
      await client.end();
    }
  }

  async function listSnapshots() {
    return withClient(async (client) => {
      const entries = await client.list(config.remoteRoot);
      return entries
        .filter((item) => item.type === "d" && item.name.startsWith(config.snapshotPrefix))
        .map((item) => item.name)
        .sort();
    });
  }

  async function readLatestSnapshotName() {
    const snapshots = await listSnapshots();
    if (!snapshots.length) {
      throw new Error("No snapshots found on remote SFTP root");
    }
    return snapshots[snapshots.length - 1];
  }

  async function getSnapshotByDateTag(yyyymmdd) {
    const snapshots = await listSnapshots();
    return snapshots.find((snapshot) => snapshot.split("_").at(-1).slice(0, 8) === yyyymmdd) || null;
  }

  async function downloadSnapshotFile(snapshotDir, rawFileName, localDirectory, retryCount) {
    const remoteFile = `${config.remoteRoot}/${snapshotDir}/${rawFileName}.tsv`;
    const localFile = path.resolve(localDirectory, `${rawFileName}.tsv`);
    fs.mkdirSync(localDirectory, { recursive: true });

    let attempt = 0;
    while (attempt <= retryCount) {
      try {
        await withClient(async (client) => {
          await client.fastGet(remoteFile, localFile);
        });
        return localFile;
      } catch (error) {
        attempt += 1;
        if (attempt > retryCount) {
          throw error;
        }
        const waitMs = 2 ** attempt * 1000;
        logger.warn(`SFTP download retry ${attempt}/${retryCount} for ${rawFileName}: ${error.message}`);
        await new Promise((resolve) => setTimeout(resolve, waitMs));
      }
    }

    return localFile;
  }

  return {
    readLatestSnapshotName,
    getSnapshotByDateTag,
    downloadSnapshotFile
  };
}

module.exports = {
  createSftpService
};
