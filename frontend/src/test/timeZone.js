/** Runs every test in the enclosing describe block in the given IANA timezone, restoring the original afterwards. */
export function runInTimeZone(timeZone) {
  const originalTimeZone = process.env.TZ

  beforeEach(() => {
    process.env.TZ = timeZone
  })

  afterEach(() => {
    if (originalTimeZone === undefined) delete process.env.TZ
    else process.env.TZ = originalTimeZone
  })
}

/** A timezone west of UTC, where a date-only string read as local time falls on the previous day. */
export const WEST_OF_UTC = 'America/Los_Angeles'
