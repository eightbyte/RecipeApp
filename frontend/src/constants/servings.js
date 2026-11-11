import { formatMeasurement } from '@/constants/units'

/** A number of servings rounded the way amounts are, so a scaled count such as 1.5 reads "1.5 servings". */
export function formatServings(servings) {
  const count = formatMeasurement(servings)
  return count === '1' ? `${count} serving` : `${count} servings`
}
