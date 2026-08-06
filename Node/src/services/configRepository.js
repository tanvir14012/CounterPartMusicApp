const { sanitizeObjectRef } = require("../utils/sql");

function createConfigRepository(snowflakeClient, logger) {
  async function readAppSettings() {
    const rows = await snowflakeClient.execute("SELECT KEY, VALUE FROM MASTER.APPSETTINGS");
    const settings = {};
    for (const row of rows) {
      settings[String(row.KEY)] = row.VALUE == null ? "" : String(row.VALUE);
    }
    return settings;
  }

  async function resetTableReloadSettings() {
    await snowflakeClient.execute("UPDATE MASTER.APPSETTINGS SET VALUE = NULL WHERE KEY = 'TablesToReloadImmediately'");
  }

  async function readLastSyncTime(snapshotName) {
    const safeName = String(snapshotName).replace(/'/g, "''");
    const sql = `SELECT UPDATED_ON_DTTM, TO_TIMESTAMP_NTZ(CURRENT_TIMESTAMP) AS NOW FROM MASTER.APPLOG WHERE SNAPSHOT_NM = '${safeName}' AND IS_RELOAD = FALSE ORDER BY ID DESC LIMIT 1`;
    const rows = await snowflakeClient.execute(sql);
    if (!rows.length) {
      return { lastSyncTime: null, now: null };
    }
    return {
      lastSyncTime: rows[0].UPDATED_ON_DTTM || null,
      now: rows[0].NOW || null
    };
  }

  async function ensureSnapshotSchemaAndTables(schemaName) {
    const safeSchema = sanitizeObjectRef(schemaName);
    await snowflakeClient.execute(`CREATE SCHEMA IF NOT EXISTS ${safeSchema}`);
    await snowflakeClient.execute(`
      BEGIN
        CREATE TABLE IF NOT EXISTS ${safeSchema}.ALTERNATIVE_WORK_TITLES LIKE MASTER.ALTERNATIVE_WORK_TITLES;
        CREATE TABLE IF NOT EXISTS ${safeSchema}.PARTIES LIKE MASTER.PARTIES;
        CREATE TABLE IF NOT EXISTS ${safeSchema}.RECORDINGS LIKE MASTER.RECORDINGS;
        CREATE TABLE IF NOT EXISTS ${safeSchema}.RECORDING_IDENTIFIERS LIKE MASTER.RECORDING_IDENTIFIERS;
        CREATE TABLE IF NOT EXISTS ${safeSchema}.RELEASES LIKE MASTER.RELEASES;
        CREATE TABLE IF NOT EXISTS ${safeSchema}.RELEASE_IDENTIFIERS LIKE MASTER.RELEASE_IDENTIFIERS;
        CREATE TABLE IF NOT EXISTS ${safeSchema}.UNCLAIMED_WORKS LIKE MASTER.UNCLAIMED_WORKS;
        CREATE TABLE IF NOT EXISTS ${safeSchema}.WORKS LIKE MASTER.WORKS;
        CREATE TABLE IF NOT EXISTS ${safeSchema}.WORK_IDENTIFIERS LIKE MASTER.WORK_IDENTIFIERS;
        CREATE TABLE IF NOT EXISTS ${safeSchema}.WORK_RECORDINGS LIKE MASTER.WORK_RECORDINGS;
        CREATE TABLE IF NOT EXISTS ${safeSchema}.WORK_RIGHT_SHARES LIKE MASTER.WORK_RIGHT_SHARES;
      END;
    `);
  }

  async function insertRunMetric(metric) {
    const escaped = {
      snapshot: String(metric.snapshotName).replace(/'/g, "''"),
      table: String(metric.tableName).replace(/'/g, "''"),
      schema: String(metric.schemaName).replace(/'/g, "''")
    };
    await snowflakeClient.execute(`
      INSERT INTO MASTER.APPLOG
      (UPDATED_ON_DTTM, SNAPSHOT_NM, TABLE_NM, SCHEMA_NM, IS_RELOAD, RAW_FILE_SIZE_MB, SFTP_DOWNLOADED_TIME_MINUTES, TABLE_LOAD_TIME_MINUTES)
      VALUES
      (CURRENT_TIMESTAMP, '${escaped.snapshot}', '${escaped.table}', '${escaped.schema}', ${metric.isReload ? "TRUE" : "FALSE"}, ${metric.rawFileSizeMb}, ${metric.downloadTimeMin}, ${metric.loadTimeMin})
    `);
  }

  return {
    readAppSettings,
    resetTableReloadSettings,
    readLastSyncTime,
    ensureSnapshotSchemaAndTables,
    insertRunMetric
  };
}

module.exports = {
  createConfigRepository
};
