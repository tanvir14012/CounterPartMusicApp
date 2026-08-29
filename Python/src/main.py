from __future__ import annotations

import os
import time
from pathlib import Path

from config import load_static_config
from config_repository import ConfigRepository
from constants import REVERSED_TABLE_MAP
from logger import Logger
from sftp_service import SftpService
from snapshot_loader import SnapshotLoader
from snowflake_client import SnowflakeClient

RAW_LOAD_ORDER = [
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
    "recordingidentifiers",
]


def parse_auto_update(value):
    return str(value or "").strip().lower() == "on"


def parse_interval_hours(value):
    try:
        parsed = float(str(value or ""))
    except ValueError:
        return 1.0
    return parsed if parsed > 0 else 1.0


def snapshot_schema_from_name(snapshot_name):
    stamp = snapshot_name.split("_")[-1][:8]
    if stamp.isdigit() and len(stamp) == 8:
        return f"SNAP_{stamp}"
    now = time.gmtime()
    return f"SNAP_{now.tm_year}{now.tm_mon:02d}{now.tm_mday:02d}"


def clear_snapshot_directories(local_root):
    if not os.path.exists(local_root):
        return
    for name in os.listdir(local_root):
        full = os.path.join(local_root, name)
        if os.path.isdir(full):
            for child in os.listdir(full):
                child_path = os.path.join(full, child)
                if os.path.isfile(child_path):
                    os.unlink(child_path)


def run_main_loop():
    config = load_static_config()
    logger = Logger(config["runtime"]["log_file_path"])
    os.makedirs(config["sftp"]["local_path"], exist_ok=True)

    snowflake_client = SnowflakeClient(config["snowflake"], logger)
    config_repository = ConfigRepository(snowflake_client, logger)
    sftp_service = SftpService(config["sftp"], logger)
    snapshot_loader = SnapshotLoader(
        snowflake_client=snowflake_client,
        sftp_service=sftp_service,
        config=config,
        logger=logger,
        config_repository=config_repository,
    )

    while True:
        app_settings = {}
        try:
            app_settings = config_repository.read_app_settings()
            if not app_settings:
                logger.warn("MASTER.APPSETTINGS returned no rows; sleeping 5 minutes")
                time.sleep(5 * 60)
                continue

            latest_snapshot = sftp_service.read_latest_snapshot_name()
            last_sync = config_repository.read_last_sync_time(latest_snapshot)

            if parse_auto_update(app_settings.get("AutoSnapshotUpdate")) and not last_sync["last_sync_time"]:
                schema_name = snapshot_schema_from_name(latest_snapshot)
                config_repository.ensure_snapshot_schema_and_tables(schema_name)
                clear_snapshot_directories(config["sftp"]["local_path"])
                for raw_name in RAW_LOAD_ORDER:
                    snapshot_loader.load_snapshot_table(latest_snapshot, raw_name, schema_name, False)

            tables_to_reload = str(app_settings.get("TablesToReloadImmediately") or "").strip()
            if tables_to_reload:
                entries = [entry.strip() for entry in tables_to_reload.split(",") if entry.strip()]
                for entry in entries:
                    parts = entry.split(".")
                    if len(parts) != 2:
                        logger.warn(f"Skipping invalid table reload entry: {entry}")
                        continue

                    schema_name = parts[0].upper()
                    table_name = parts[1].upper()
                    stamp = schema_name.split("_")[-1]
                    if len(stamp) != 8 or not stamp.isdigit():
                        logger.warn(f"Skipping reload entry with invalid schema timestamp: {entry}")
                        continue

                    raw_name = REVERSED_TABLE_MAP.get(table_name)
                    if not raw_name:
                        logger.warn(f"Skipping reload entry with unknown table mapping: {entry}")
                        continue

                    snapshot_name = sftp_service.get_snapshot_by_date_tag(stamp)
                    if not snapshot_name:
                        logger.warn(f"No snapshot found on SFTP for {stamp}; skipping {entry}")
                        continue

                    clear_snapshot_directories(config["sftp"]["local_path"])
                    snapshot_loader.load_snapshot_table(snapshot_name, raw_name, schema_name, True)

                config_repository.reset_table_reload_settings()
        except Exception as exc:  # pragma: no cover - runtime guard
            logger.error(str(exc))

        interval_hours = parse_interval_hours(app_settings.get("SnapshotCheckIntervalInHours"))
        time.sleep(int(interval_hours * 60 * 60))


if __name__ == "__main__":
    run_main_loop()
