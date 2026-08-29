from __future__ import annotations

import math
import os
from pathlib import Path


def split_tsv_if_needed(file_path: str, chunk_size_bytes: int, buffer_size: int):
    full_path = os.path.abspath(file_path)
    size = os.path.getsize(full_path)
    if size <= chunk_size_bytes:
        return [full_path]

    result_files = []
    with open(full_path, "r+b") as handle:
        current_size = os.path.getsize(full_path)
        chunk_no = int(math.ceil(current_size / chunk_size_bytes))

        while current_size > chunk_size_bytes:
            start_pos = current_size - chunk_size_bytes
            if start_pos <= 0:
                break

            while start_pos < current_size:
                handle.seek(start_pos)
                if handle.read(1) == b"\n":
                    start_pos += 1
                    break
                start_pos += 1

            if start_pos >= current_size:
                handle.truncate(current_size - chunk_size_bytes)
                current_size = os.path.getsize(full_path)
                continue

            parsed = Path(full_path)
            chunk_name = f"{parsed.stem}_{chunk_no}{parsed.suffix}"
            chunk_path = str(parsed.with_name(chunk_name))
            chunk_no -= 1
            with open(chunk_path, "wb") as chunk_file:
                handle.seek(start_pos)
                while handle.tell() < current_size:
                    read_len = min(buffer_size, current_size - handle.tell())
                    data = handle.read(read_len)
                    if not data:
                        break
                    chunk_file.write(data)

            result_files.append(chunk_path)
            handle.seek(0, os.SEEK_END)
            handle.truncate(start_pos)
            current_size = os.path.getsize(full_path)

    return [full_path, *result_files]
