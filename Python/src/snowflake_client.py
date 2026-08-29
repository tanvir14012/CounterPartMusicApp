from __future__ import annotations

from typing import Any

import snowflake.connector
from snowflake.connector import DictCursor


class SnowflakeClient:
    def __init__(self, config: dict, logger: Any):
        self.config = config
        self.logger = logger

    def connect(self):
        return snowflake.connector.connect(
            account=self.config["account"],
            user=self.config["username"],
            password=self.config["password"],
            role=self.config["role"],
            database=self.config["database"],
            schema=self.config["schema"],
            warehouse=self.config["warehouse"],
            application=self.config["application"],
            authenticator=self.config["authenticator"],
        )

    def execute(self, sql_text: str, binds=None):
        binds = binds or []
        conn = self.connect()
        try:
            cur = conn.cursor(DictCursor)
            try:
                if binds:
                    cur.execute(sql_text, binds)
                else:
                    cur.execute(sql_text)
                rows = cur.fetchall() or []
                return list(rows)
            finally:
                cur.close()
        finally:
            conn.close()
