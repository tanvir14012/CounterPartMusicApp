from __future__ import annotations

import logging
from pathlib import Path


class Logger:
    def __init__(self, file_path: str):
        self.logger = logging.getLogger("counterpartmusic-python")
        self.logger.setLevel(logging.INFO)
        self.logger.handlers.clear()

        formatter = logging.Formatter("%(asctime)s %(levelname)s %(message)s")

        stream_handler = logging.StreamHandler()
        stream_handler.setFormatter(formatter)
        self.logger.addHandler(stream_handler)

        log_path = Path(file_path).resolve()
        log_path.parent.mkdir(parents=True, exist_ok=True)
        file_handler = logging.FileHandler(log_path)
        file_handler.setFormatter(formatter)
        self.logger.addHandler(file_handler)

    def info(self, message: str) -> None:
        self.logger.info(message)

    def warn(self, message: str) -> None:
        self.logger.warning(message)

    def error(self, message: str) -> None:
        self.logger.error(message)
