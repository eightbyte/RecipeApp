export const DISPLAY_LOCALE = 'en-AU'

/** Formats a date-only value such as '2026-06-03' as the calendar day it names, in every timezone, because the string parses as UTC midnight. */
export function formatDateOnly(value, options) {
  return new Date(value).toLocaleDateString(DISPLAY_LOCALE, { ...options, timeZone: 'UTC' })
}
