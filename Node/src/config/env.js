const path = require("path");
const dotenv = require("dotenv");

dotenv.config({ path: path.resolve(process.cwd(), "Node", ".env") });
dotenv.config();

function must(name) {
  const value = process.env[name];
  if (!value || !value.trim()) {
    throw new Error(`Missing required environment variable: ${name}`);
  }
  return value.trim();
}

function intFromEnv(name, fallback) {
  const raw = process.env[name];
  if (!raw || !raw.trim()) {
    return fallback;
  }
  const parsed = Number.parseInt(raw, 10);
  if (Number.isNaN(parsed) || parsed <= 0) {
    throw new Error(`Invalid integer environment variable: ${name}`);
  }
  return parsed;
}

function loadStaticConfig() {
  return {
    nodeEnv: process.env.NODE_ENV || "production",
    snowflake: {
      account: must("SNOWFLAKE_ACCOUNT"),
      username: must("SNOWFLAKE_USERNAME"),
      password: must("SNOWFLAKE_PASSWORD"),
      role: process.env.SNOWFLAKE_ROLE || "ACCOUNTADMIN",
      database: process.env.SNOWFLAKE_DATABASE || "COUNTERPARTMUSIC",
      schema: process.env.SNOWFLAKE_SCHEMA || "MASTER",
      warehouse: process.env.SNOWFLAKE_WAREHOUSE || "COMPUTE_WH",
      application: process.env.SNOWFLAKE_APPLICATION || "CounterPartMusicNode",
      authenticator: process.env.SNOWFLAKE_AUTHENTICATOR || "snowflake",
      stageFileFormat: process.env.SNOWFLAKE_STAGE_FILE_FORMAT || "MASTER.TSV_FORMAT",
      maxParallelUploads: intFromEnv("MAX_PARALLEL_UPLOADS", 5)
    },
    sftp: {
      host: must("SFTP_HOST"),
      port: intFromEnv("SFTP_PORT", 22),
      username: must("SFTP_USERNAME"),
      privateKeyPath: must("SFTP_PRIVATE_KEY_PATH"),
      privateKeyPassphrase: process.env.SFTP_PRIVATE_KEY_PASSPHRASE || "",
      remoteRoot: process.env.SFTP_REMOTE_ROOT || "/public-database",
      snapshotPrefix: process.env.SFTP_SNAPSHOT_PREFIX || "BWARM_PADPIDA2020062405C_",
      manifestPrefix: process.env.SFTP_MANIFEST_PREFIX || "BWARM_Manifest_PADPIDA2020062405C_",
      localPath: path.resolve(process.env.LOCAL_SNAPSHOT_PATH || "./snapshots")
    },
    runtime: {
      logFilePath: process.env.LOG_FILE_PATH || "./logs/counterpartmusic-node.log",
      chunkSizeBytes: intFromEnv("TSV_CHUNK_SIZE_BYTES", 240 * 1024 * 1024),
      splitBufferBytes: intFromEnv("TSV_SPLIT_BUFFER_BYTES", 32 * 1024 * 1024),
      tsvEncoding: process.env.TSV_FILE_ENCODING || "utf8",
      retryCount: intFromEnv("INITIAL_RETRY_COUNT", 7)
    }
  };
}

module.exports = {
  loadStaticConfig
};
