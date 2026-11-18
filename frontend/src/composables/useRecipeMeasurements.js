import { computed, onScopeDispose, watch } from 'vue'
import { formatIngredientMeasurement, formatUsIngredientMeasurement } from '@/constants/units'
import { US_MEASUREMENT_DEBOUNCE_MS, US_UNIT_SYSTEM } from '@/constants/unitSystems'
import { useRecipeStore } from '@/stores/recipes'
import { useUiStore } from '@/stores/ui'

/**
 * How a single recipe's rows read at `servings` in this device's Unit System. In US, a servings change scales the known US
 * Measurements on the device at once, in their existing units, and about a second later the server's units replace them.
 */
export function useRecipeMeasurements(recipe, servings) {
  const ui    = useUiStore()
  const store = useRecipeStore()

  const inUs = computed(() => ui.unitSystem === US_UNIT_SYSTEM)

  /** The US Measurements known for this recipe, at the servings they were computed for. */
  const known = computed(() => {
    const measurements = store.usMeasurements
    return measurements && measurements.recipeId === recipe.value?.id ? measurements : null
  })

  /** The row's display parts, `{ amount, source }`, at the chosen servings. */
  function measurementOf(ing) {
    const multiplier = servings.value / recipe.value.servings
    if (!inUs.value || !known.value) return formatIngredientMeasurement(ing, multiplier)

    const us = known.value.byIngredientId[ing.id]
    const onDevice = us && { ...us, amount: us.amount * servings.value / known.value.servings }
    return formatUsIngredientMeasurement(ing, onDevice, multiplier)
  }

  let debounceTimer = null
  let inFlight      = null

  function cancelCorrection() {
    clearTimeout(debounceTimer)
    debounceTimer = null
    inFlight?.abort()
    inFlight = null
  }

  async function correct(recipeId, wanted) {
    const request = new AbortController()
    inFlight = request
    try {
      const rows = await store.fetchUsMeasurements(recipeId, wanted, { signal: request.signal })
      if (inFlight === request) store.setUsMeasurements(recipeId, wanted, rows)
    } catch {
      // A failed or cancelled correction leaves the value scaled on the device, which is close enough to cook from.
    } finally {
      if (inFlight === request) inFlight = null
    }
  }

  watch(
    [inUs, () => recipe.value?.id, () => (recipe.value ? servings.value : null)],
    ([us, recipeId, wanted]) => {
      cancelCorrection()
      if (!us || !recipeId || known.value?.servings === wanted) return
      // Nothing to scale yet, so there is no reason to wait.
      if (!known.value) return correct(recipeId, wanted)
      debounceTimer = setTimeout(() => correct(recipeId, wanted), US_MEASUREMENT_DEBOUNCE_MS)
    },
    { immediate: true },
  )

  onScopeDispose(cancelCorrection)

  return { measurementOf }
}
