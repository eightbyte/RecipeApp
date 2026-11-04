import { formatDateOnly } from './dateOnly'
import { runInTimeZone, WEST_OF_UTC } from '@/test/timeZone'

describe('formatDateOnly', () => {
  describe.each([WEST_OF_UTC, 'UTC', 'Australia/Sydney'])('in %s', (timeZone) => {
    runInTimeZone(timeZone)

    it('shows the stored calendar date', () => {
      expect(formatDateOnly('2026-06-03', { weekday: 'short', day: 'numeric', month: 'short' })).toBe('Wed, 3 June')
    })
  })

  it('formats with the options it is given', () => {
    expect(formatDateOnly('2026-06-03', { day: 'numeric' })).toBe('3')
    expect(formatDateOnly('2026-06-03', { month: 'short' })).toBe('Jun')
    expect(formatDateOnly('2026-06-03', { year: 'numeric', month: 'short', day: 'numeric' })).toBe('3 June 2026')
  })
})
