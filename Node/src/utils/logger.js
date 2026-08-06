const fs = require("fs");
const path = require("path");
const winston = require("winston");

function createLogger(logFilePath) {
  const resolvedPath = path.resolve(logFilePath);
  const dir = path.dirname(resolvedPath);
  fs.mkdirSync(dir, { recursive: true });

  return winston.createLogger({
    level: "info",
    format: winston.format.combine(
      winston.format.timestamp(),
      winston.format.printf(
        ({ timestamp, level, message }) => `${timestamp} [${level.toUpperCase()}] ${message}`
      )
    ),
    transports: [
      new winston.transports.Console(),
      new winston.transports.File({ filename: resolvedPath, maxsize: 10 * 1024 * 1024, maxFiles: 15 })
    ]
  });
}

module.exports = {
  createLogger
};
