const TABLE_MAP = Object.freeze({
  parties: "PARTIES",
  recordingidentifiers: "RECORDING_IDENTIFIERS",
  recordings: "RECORDINGS",
  releaseidentifiers: "RELEASE_IDENTIFIERS",
  releases: "RELEASES",
  unclaimedworkrightshares: "UNCLAIMED_WORKS",
  workalternativetitles: "ALTERNATIVE_WORK_TITLES",
  workidentifiers: "WORK_IDENTIFIERS",
  worksrecordings: "WORK_RECORDINGS",
  workrightshares: "WORK_RIGHT_SHARES",
  works: "WORKS"
});

const REVERSED_TABLE_MAP = Object.freeze(
  Object.fromEntries(Object.entries(TABLE_MAP).map(([k, v]) => [v, k]))
);

module.exports = {
  TABLE_MAP,
  REVERSED_TABLE_MAP
};
