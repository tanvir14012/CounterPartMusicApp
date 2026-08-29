from __future__ import annotations

from typing import Any, Dict


class ConfigRepository:
    def __init__(self, snowflake_client: Any, logger: Any):
        self.snowflake_client = snowflake_client
        self.logger = logger

    def read_app_settings(self):
        rows = self.snowflake_client.execute("SELECT KEY, VALUE FROM MASTER.APPSETTINGS")
        settings = {}
        for row in rows:
            key = str(row.get("KEY") or "")
            value = row.get("VALUE")
            settings[key] = "" if value is None else str(value)
        return settings

    def reset_table_reload_settings(self):
        self.snowflake_client.execute("UPDATE MASTER.APPSETTINGS SET VALUE = NULL WHERE KEY = 'TablesToReloadImmediately'")

    def read_last_sync_time(self, snapshot_name: str):
        safe_name = str(snapshot_name).replace("'", "''")
        sql = (
            f"SELECT UPDATED_ON_DTTM, TO_TIMESTAMP_NTZ(CURRENT_TIMESTAMP) AS NOW "
            f"FROM MASTER.APPLOG WHERE SNAPSHOT_NM = '{safe_name}' AND IS_RELOAD = FALSE "
            f"ORDER BY ID DESC LIMIT 1"
        )
        rows = self.snowflake_client.execute(sql)
        if not rows:
            return {"last_sync_time": None, "now": None}
        first = rows[0]
        return {
            "last_sync_time": first.get("UPDATED_ON_DTTM"),
            "now": first.get("NOW"),
        }

    def ensure_snapshot_schema_and_tables(self, schema_name: str):
        safe_schema = schema_name
        self.snowflake_client.execute(f"CREATE SCHEMA IF NOT EXISTS {safe_schema}")
        self.snowflake_client.execute(
            f"""
            BEGIN
              CREATE TABLE IF NOT EXISTS {safe_schema}.ALTERNATIVE_WORK_TITLES LIKE MASTER.ALTERNATIVE_WORK_TITLES;
              CREATE TABLE IF NOT EXISTS {safe_schema}.PARTIES LIKE MASTER.PARTIES;
              CREATE TABLE IF NOT EXISTS {safe_schema}.RECORDINGS LIKE MASTER.RECORDINGS;
              CREATE TABLE IF NOT EXISTS {safe_schema}.RECORDING_IDENTIFIERS LIKE MASTER.RECORDING_IDENTIFIERS;
              CREATE TABLE IF NOT EXISTS {safe_schema}.RELEASES LIKE MASTER.RELEASES;
              CREATE TABLE IF NOT EXISTS {safe_schema}.RELEASE_IDENTIFIERS LIKE MASTER.RELEASE_IDENTIFIERS;
              CREATE TABLE IF NOT EXISTS {safe_schema}.UNCLAIMED_WORKS LIKE MASTER.UNCLAIMED_WORKS;
              CREATE TABLE IF NOT EXISTS {safe_schema}.WORKS LIKE MASTER.WORKS;
              CREATE TABLE IF NOT EXISTS {safe_schema}.WORK_IDENTIFIERS LIKE MASTER.WORK_IDENTIFIERS;
              CREATE TABLE IF NOT EXISTS {safe_schema}.WORK_RECORDINGS LIKE MASTER.WORK_RECORDINGS;
              CREATE TABLE IF NOT EXISTS {safe_schema}.WORK_RIGHT_SHARES LIKE MASTER.WORK_RIGHT_SHARES;
            END;
            """
        )

    def insert_run_metric(self, metric: Dict[str, Any]):
        snapshot_value = str(metric["snapshot_name"]).replace("'", "''")
        table_value = str(metric["table_name"]).replace("'", "''")
        schema_value = str(metric["schema_name"]).replace("'", "''")
        sql = (
            "INSERT INTO MASTER.APPLOG "
            "(UPDATED_ON_DTTM, SNAPSHOT_NM, TABLE_NM, SCHEMA_NM, IS_RELOAD, RAW_FILE_SIZE_MB, SFTP_DOWNLOADED_TIME_MINUTES, TABLE_LOAD_TIME_MINUTES) "
            f"VALUES (CURRENT_TIMESTAMP, '{snapshot_value}', '{table_value}', '{schema_value}', {{}} , {{}} , {{}} , {{}})"
        )
        sql = sql.format(
            "TRUE" if metric["is_reload"] else "FALSE",
            metric["raw_file_size_mb"],
            metric["download_time_min"],
            metric["load_time_min"],
        )
        self.snowflake_client.execute(sql)
