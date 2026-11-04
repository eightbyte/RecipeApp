// Every access sits in a try, because private browsing, blocked site data or a full quota make localStorage throw.

/** The JSON value stored under `key`, or `fallback` when there is none, it is unreadable, or storage is unavailable. */
export function readStoredJson(key, fallback) {
  try {
    const storedText = window.localStorage.getItem(key)
    return storedText === null ? fallback : JSON.parse(storedText)
  } catch {
    return fallback
  }
}

/** Stores `value` as JSON under `key`, doing nothing when storage is unavailable. */
export function writeStoredJson(key, value) {
  try {
    window.localStorage.setItem(key, JSON.stringify(value))
  } catch {
    // A preference that cannot be saved is simply not remembered.
  }
}
