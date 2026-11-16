import { BUILT_IN_THEMES, DEFAULT_THEME_ID, THEME_STORAGE_KEY } from '@/constants/themes'
import { readStoredJson, writeStoredJson } from '@/utils/storage'

/** The id of the Theme this device uses: its stored choice, or Light when there is none or it no longer exists. */
export function readThemeChoice() {
  const storedId = readStoredJson(THEME_STORAGE_KEY, null)
  return BUILT_IN_THEMES.some((theme) => theme.id === storedId) ? storedId : DEFAULT_THEME_ID
}

/** Remembers `themeId` as this device's Theme. */
export function writeThemeChoice(themeId) {
  writeStoredJson(THEME_STORAGE_KEY, themeId)
}
