from __future__ import annotations

import os
from pathlib import Path

from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[1]
load_dotenv(ROOT / ".env")
load_dotenv()


def must(name: str) -> str:
    value = os.getenv(name, "").strip()
    if not value:
        raise ValueError(f"Missing required environment variable: {name}")
    return value


def int_from_env(name: str, fallback: int) -> int:
    raw = os.getenv(name, "").strip()
    if not raw:
        return fallback
    try:
        parsed = int(raw)
    except ValueError as exc:  # pragma: no cover - defensive guard
        raise ValueError(f"Invalid integer environment variable: {name}") from exc
    if parsed <= 0:
        raise ValueError(f"Invalid integer environment variable: {name}")
    return parsed


def load_static_config() -> dict:
    return {
        "node_env": os.getenv("NODE_ENV", "production"),
        "snowflake": {
            "account": must("SNOWFLAKE_ACCOUNT"),
            "username": must("SNOWFLAKE_USERNAME"),
            "password": must("SNOWFLAKE_PASSWORD"),
            "role": os.getenv("SNOWFLAKE_ROLE", "ACCOUNTADMIN"),
            "database": os.getenv("SNOWFLAKE_DATABASE", "COUNTERPARTMUSIC"),
            "schema": os.getenv("SNOWFLAKE_SCHEMA", "MASTER"),
            "warehouse": os.getenv("SNOWFLAKE_WAREHOUSE", "COMPUTE_WH"),
            "application": os.getenv("SNOWFLAKE_APPLICATION", "CounterPartMusicPython"),
            "authenticator": os.getenv("SNOWFLAKE_AUTHENTICATOR", "snowflake"),
            "stage_file_format": os.getenv("SNOWFLAKE_STAGE_FILE_FORMAT", "MASTER.TSV_FORMAT"),
            "max_parallel_uploads": int_from_env("MAX_PARALLEL_UPLOADS", 5),
        },
        "sftp": {
            "host": must("SFTP_HOST"),
            "port": int_from_env("SFTP_PORT", 22),
            "username": must("SFTP_USERNAME"),
            "private_key_path": must("SFTP_PRIVATE_KEY_PATH"),
            "private_key_passphrase": os.getenv("SFTP_PRIVATE_KEY_PASSPHRASE", "") or "",
            "remote_root": os.getenv("SFTP_REMOTE_ROOT", "/public-database"),
            "snapshot_prefix": os.getenv("SFTP_SNAPSHOT_PREFIX", "BWARM_PADPIDA2020062405C_"),
            "manifest_prefix": os.getenv("SFTP_MANIFEST_PREFIX", "BWARM_Manifest_PADPIDA2020062405C_"),
            "local_path": str(Path(os.getenv("LOCAL_SNAPSHOT_PATH", "./snapshots")).resolve()),
        },
        "runtime": {
            "log_file_path": os.getenv("LOG_FILE_PATH", "./logs/counterpartmusic-python.log"),
            "chunk_size_bytes": int_from_env("TSV_CHUNK_SIZE_BYTES", 240 * 1024 * 1024),
            "split_buffer_bytes": int_from_env("TSV_SPLIT_BUFFER_BYTES", 32 * 1024 * 1024),
            "tsv_encoding": os.getenv("TSV_FILE_ENCODING", "utf8"),
            "retry_count": int_from_env("INITIAL_RETRY_COUNT", 7),
        },
    }
