function quoteSnowflakeIdent(name) {
  return `"${String(name).replace(/"/g, "\"\"")}"`;
}

function sanitizeObjectRef(name) {
  if (!/^[A-Za-z0-9_\.]+$/.test(name)) {
    throw new Error(`Invalid Snowflake identifier: ${name}`);
  }
  return name;
}

module.exports = {
  quoteSnowflakeIdent,
  sanitizeObjectRef
};
