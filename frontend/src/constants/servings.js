import { formatMeasurement } from '@/constants/units'

/** The fewest Servings a recipe may have, mirroring `RecipeLimits.MinServings` on the backend. */
export const MIN_SERVINGS = 1

/** The most Servings a recipe may have, mirroring `RecipeLimits.MaxServings` on the backend. */
export const MAX_SERVINGS = 100

/** A number of servings rounded the way amounts are, so a scaled count such as 1.5 reads "1.5 servings". */
export function formatServings(servings) {
  const count = formatMeasurement(servings)
  return count === '1' ? `${count} serving` : `${count} servings`
}
