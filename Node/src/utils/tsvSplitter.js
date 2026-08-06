const fs = require("fs");
const path = require("path");

async function splitTsvIfNeeded(filePath, chunkSizeBytes, bufferSize) {
  const fullPath = path.resolve(filePath);
  const stats = fs.statSync(fullPath);
  if (stats.size <= chunkSizeBytes) {
    return [fullPath];
  }

  const fh = fs.openSync(fullPath, "r+");
  const resultFiles = [];

  try {
    let currentSize = fs.fstatSync(fh).size;
    let chunkNo = Math.ceil(currentSize / chunkSizeBytes);

    while (currentSize > chunkSizeBytes) {
      let startPos = currentSize - chunkSizeBytes;
      if (startPos <= 0) {
        break;
      }

      const oneByte = Buffer.alloc(1);
      while (startPos < currentSize) {
        fs.readSync(fh, oneByte, 0, 1, startPos);
        if (oneByte[0] === 10) {
          startPos += 1;
          break;
        }
        startPos += 1;
      }

      if (startPos >= currentSize) {
        fs.ftruncateSync(fh, currentSize - chunkSizeBytes);
        currentSize = fs.fstatSync(fh).size;
        continue;
      }

      const parsed = path.parse(fullPath);
      const chunkName = `${parsed.name}_${chunkNo--}${parsed.ext}`;
      const chunkPath = path.join(parsed.dir, chunkName);
      const writeFd = fs.openSync(chunkPath, "w");

      try {
        let readPos = startPos;
        const tempBuffer = Buffer.alloc(bufferSize);
        while (readPos < currentSize) {
          const toRead = Math.min(bufferSize, currentSize - readPos);
          const read = fs.readSync(fh, tempBuffer, 0, toRead, readPos);
          if (read <= 0) {
            break;
          }
          fs.writeSync(writeFd, tempBuffer, 0, read);
          readPos += read;
        }
      } finally {
        fs.closeSync(writeFd);
      }

      resultFiles.push(chunkPath);
      fs.ftruncateSync(fh, startPos);
      currentSize = fs.fstatSync(fh).size;
    }
  } finally {
    fs.closeSync(fh);
  }

  return [fullPath, ...resultFiles].filter((p) => fs.existsSync(p));
}

module.exports = {
  splitTsvIfNeeded
};
