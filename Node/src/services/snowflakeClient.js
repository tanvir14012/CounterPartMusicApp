const snowflake = require("snowflake-sdk");

function createSnowflakeClient(config, logger) {
  function connect() {
    return new Promise((resolve, reject) => {
      const connection = snowflake.createConnection({
        account: config.account,
        username: config.username,
        password: config.password,
        role: config.role,
        database: config.database,
        schema: config.schema,
        warehouse: config.warehouse,
        application: config.application,
        authenticator: config.authenticator
      });

      connection.connect((err, conn) => {
        if (err) {
          reject(err);
          return;
        }
        resolve(conn);
      });
    });
  }

  async function execute(sqlText, binds = []) {
    const conn = await connect();
    try {
      return await new Promise((resolve, reject) => {
        conn.execute({
          sqlText,
          binds,
          complete: (err, _stmt, rows) => {
            if (err) {
              reject(err);
              return;
            }
            resolve(rows || []);
          }
        });
      });
    } finally {
      conn.destroy((destroyErr) => {
        if (destroyErr) {
          logger.error(`Snowflake connection destroy error: ${destroyErr.message}`);
        }
      });
    }
  }

  return {
    execute
  };
}

module.exports = {
  createSnowflakeClient
};
