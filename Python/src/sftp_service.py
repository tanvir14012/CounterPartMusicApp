from __future__ import annotations

import time
from pathlib import Path
from typing import Any, Callable, TypeVar

import paramiko

T = TypeVar("T")


class SftpService:
    def __init__(self, config: dict, logger: Any):
        self.config = config
        self.logger = logger

    def _connect(self):
        private_key_path = Path(self.config["private_key_path"]).expanduser().resolve()
        pkey = paramiko.RSAKey.from_private_key_file(str(private_key_path), password=self.config["private_key_passphrase"] or None)
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(
            hostname=self.config["host"],
            port=self.config["port"],
            username=self.config["username"],
            pkey=pkey,
            timeout=60,
            banner_timeout=60,
        )
        return client

    def _with_client(self, callback: Callable[[Any], T]) -> T:
        ssh = self._connect()
        sftp = ssh.open_sftp()
        try:
            return callback(sftp)
        finally:
            sftp.close()
            ssh.close()

    def list_snapshots(self):
        def _list(sftp):
            entries = sftp.listdir(self.config["remote_root"])
            return [
                entry
                for entry in entries
                if entry.startswith(self.config["snapshot_prefix"])
            ]

        return self._with_client(_list)

    def read_latest_snapshot_name(self):
        snapshots = self.list_snapshots()
        if not snapshots:
            raise RuntimeError("No snapshots found on remote SFTP root")
        return sorted(snapshots)[-1]

    def get_snapshot_by_date_tag(self, yyyymmdd: str):
        snapshots = self.list_snapshots()
        for snapshot in sorted(snapshots):
            if snapshot.split("_")[-1][:8] == yyyymmdd:
                return snapshot
        return None

    def download_snapshot_file(self, snapshot_dir: str, raw_file_name: str, local_directory: str, retry_count: int):
        remote_file = f"{self.config['remote_root']}/{snapshot_dir}/{raw_file_name}.tsv"
        local_file = str(Path(local_directory) / f"{raw_file_name}.tsv")
        Path(local_directory).mkdir(parents=True, exist_ok=True)

        attempt = 0
        while attempt <= retry_count:
            try:
                def _download(sftp):
                    sftp.get(remote_file, local_file)
                    return local_file

                return self._with_client(_download)
            except Exception as exc:
                attempt += 1
                if attempt > retry_count:
                    raise
                wait_ms = 2 ** attempt * 1000
                self.logger.warn(f"SFTP download retry {attempt}/{retry_count} for {raw_file_name}: {exc}")
                time.sleep(wait_ms / 1000)

        return local_file
