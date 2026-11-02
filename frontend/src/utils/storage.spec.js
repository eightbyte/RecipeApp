import { readStoredJson, writeStoredJson } from './storage'

const KEY = 'test.storage.key'

function denyStorageAccess() {
  vi.spyOn(window, 'localStorage', 'get').mockImplementation(() => {
    throw new DOMException('Storage is disabled', 'SecurityError')
  })
}

afterEach(() => {
  vi.restoreAllMocks()
  window.localStorage.clear()
})

describe('readStoredJson', () => {
  it('returns the value a write stored', () => {
    writeStoredJson(KEY, { sort: 'NAME', direction: 'ASC' })

    expect(readStoredJson(KEY, null)).toEqual({ sort: 'NAME', direction: 'ASC' })
  })

  it('returns the fallback when nothing is stored', () => {
    expect(readStoredJson(KEY, 'fallback')).toBe('fallback')
  })

  it('returns the fallback when the stored text is not JSON', () => {
    window.localStorage.setItem(KEY, '{not json')

    expect(readStoredJson(KEY, 'fallback')).toBe('fallback')
  })

  it('returns the fallback when storage access throws', () => {
    denyStorageAccess()

    expect(readStoredJson(KEY, 'fallback')).toBe('fallback')
  })
})

describe('writeStoredJson', () => {
  it('stores the value as JSON', () => {
    writeStoredJson(KEY, { sort: 'NAME' })

    expect(window.localStorage.getItem(KEY)).toBe('{"sort":"NAME"}')
  })

  it('swallows the error when storage access throws', () => {
    denyStorageAccess()

    expect(() => writeStoredJson(KEY, { sort: 'NAME' })).not.toThrow()
  })

  it('swallows the error when the write is refused, such as when storage is full', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Quota exceeded', 'QuotaExceededError')
    })

    expect(() => writeStoredJson(KEY, { sort: 'NAME' })).not.toThrow()
  })
})
