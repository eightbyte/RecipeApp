import { DEFAULT_UNIT_SYSTEM, UNIT_SYSTEMS, UNIT_SYSTEM_STORAGE_KEY } from '@/constants/unitSystems'
import { readStoredJson, writeStoredJson } from '@/utils/storage'

/** The Unit System this device reads recipes in: its stored choice, or Metric when there is none or it is unknown. */
export function readUnitSystemChoice() {
  const stored = readStoredJson(UNIT_SYSTEM_STORAGE_KEY, null)
  return UNIT_SYSTEMS.some((system) => system.value === stored) ? stored : DEFAULT_UNIT_SYSTEM
}

/** Remembers `unitSystem` as this device's Unit System. */
export function writeUnitSystemChoice(unitSystem) {
  writeStoredJson(UNIT_SYSTEM_STORAGE_KEY, unitSystem)
}
