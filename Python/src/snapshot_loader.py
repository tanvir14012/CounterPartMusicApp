from __future__ import annotations

import os
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from config import load_static_config
from constants import TABLE_MAP
from tsv_splitter import split_tsv_if_needed


class SnapshotLoader:
    def __init__(self, snowflake_client, sftp_service, config, logger, config_repository):
        self.snowflake_client = snowflake_client
        self.sftp_service = sftp_service
        self.config = config
        self.logger = logger
        self.config_repository = config_repository

    def ensure_stage(self, stage_name: str):
        safe_stage = stage_name
        self.snowflake_client.execute(
            f"CREATE STAGE IF NOT EXISTS {safe_stage} FILE_FORMAT = (FORMAT_NAME = '{self.config['snowflake']['stage_file_format']}')"
        )

    def put_and_copy(self, file_path: str, stage_name: str, qualified_table_name: str):
        normalized = os.path.abspath(file_path).replace("\\", "/")
        put_sql = f"PUT 'file://{normalized}' @{stage_name} AUTO_COMPRESS=TRUE"
        self.snowflake_client.execute(put_sql)

        gz_name = f"{Path(file_path).name}.gz"
        copy_sql = (
            f"COPY INTO {qualified_table_name} FROM @{stage_name}/{gz_name} "
            f"FILE_FORMAT = (FORMAT_NAME = '{self.config['snowflake']['stage_file_format']}') "
            "ON_ERROR = CONTINUE ENFORCE_LENGTH = FALSE PURGE = TRUE"
        )
        self.snowflake_client.execute(copy_sql)

    def run_with_concurrency(self, items, worker, max_workers: int):
        if not items:
            return
        max_workers = min(max_workers, len(items))
        with ThreadPoolExecutor(max_workers=max_workers) as executor:
            list(executor.map(worker, items))

    def clear_files(self, dir_path: str):
        if not os.path.isdir(dir_path):
            return
        for name in os.listdir(dir_path):
            full = os.path.join(dir_path, name)
            if os.path.isfile(full):
                os.unlink(full)

    def load_snapshot_table(self, snapshot_name: str, raw_file_name: str, schema_name: str, is_reload: bool):
        table_name = TABLE_MAP.get(raw_file_name)
        if not table_name:
            raise ValueError(f"No Snowflake table map found for raw file: {raw_file_name}")

        local_dir = os.path.join(self.config["sftp"]["local_path"], snapshot_name)
        os.makedirs(local_dir, exist_ok=True)

        download_started = __import__("time").time()
        downloaded_file = self.sftp_service.download_snapshot_file(
            snapshot_name,
            raw_file_name,
            local_dir,
            self.config["runtime"]["retry_count"],
        )
        download_time_min = ( (__import__("time").time() - download_started) / 60 )

        file_size_mb = os.path.getsize(downloaded_file) / (1024 * 1024)
        chunks = split_tsv_if_needed(
            downloaded_file,
            self.config["runtime"]["chunk_size_bytes"],
            self.config["runtime"]["split_buffer_bytes"],
        )

        stage_name = f"{schema_name}.{raw_file_name.upper()}"
        qualified_table = f"{schema_name}.{table_name}"
        self.ensure_stage(stage_name)

        load_started = __import__("time").time()

        def upload(chunk_file: str):
            self.put_and_copy(chunk_file, stage_name, qualified_table)

        self.run_with_concurrency(chunks, upload, self.config["snowflake"]["max_parallel_uploads"])

        load_time_min = ( (__import__("time").time() - load_started) / 60 )
        self.clear_files(local_dir)

        self.config_repository.insert_run_metric({
            "snapshot_name": snapshot_name,
            "table_name": table_name,
            "schema_name": schema_name,
            "is_reload": is_reload,
            "raw_file_size_mb": round(file_size_mb, 2),
            "download_time_min": round(download_time_min, 2),
            "load_time_min": round(load_time_min, 2),
        })

        self.logger.info(f"Loaded {raw_file_name} into {qualified_table} from snapshot {snapshot_name}")
