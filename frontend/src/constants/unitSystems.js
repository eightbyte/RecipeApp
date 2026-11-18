/** The Unit Systems a single recipe's Measurements are read in, mirroring `UnitSystem.All` on the backend. */
export const UNIT_SYSTEMS = [
  { value: 'metric', title: 'Metric' },
  { value: 'us',     title: 'US' },
]

export const METRIC_UNIT_SYSTEM = 'metric'
export const US_UNIT_SYSTEM     = 'us'

export const DEFAULT_UNIT_SYSTEM = METRIC_UNIT_SYSTEM

/** The `localStorage` key holding this device's Unit System. */
export const UNIT_SYSTEM_STORAGE_KEY = 'recipeapp.unitSystem'

/** How long after the last servings change the US Measurements are fetched to correct the units scaled on the device. */
export const US_MEASUREMENT_DEBOUNCE_MS = 1000
