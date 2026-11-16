import { readThemeChoice, writeThemeChoice } from './themeChoice'
import { THEME_STORAGE_KEY } from '@/constants/themes'

beforeEach(() => {
  window.localStorage.clear()
})

describe('readThemeChoice', () => {
  it('is Light when this device has not chosen a Theme', () => {
    expect(readThemeChoice()).toBe('recipeLight')
  })

  it("is the device's stored Theme", () => {
    window.localStorage.setItem(THEME_STORAGE_KEY, JSON.stringify('recipeDark'))
    expect(readThemeChoice()).toBe('recipeDark')
  })

  it('falls back to Light when the stored Theme no longer exists', () => {
    window.localStorage.setItem(THEME_STORAGE_KEY, JSON.stringify('deletedCustomTheme'))
    expect(readThemeChoice()).toBe('recipeLight')
  })

  it('falls back to Light when the stored value is unreadable', () => {
    window.localStorage.setItem(THEME_STORAGE_KEY, '{not json')
    expect(readThemeChoice()).toBe('recipeLight')
  })
})

describe('writeThemeChoice', () => {
  it('stores the id, so the next read returns it', () => {
    writeThemeChoice('recipeDark')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('"recipeDark"')
    expect(readThemeChoice()).toBe('recipeDark')
  })
})
