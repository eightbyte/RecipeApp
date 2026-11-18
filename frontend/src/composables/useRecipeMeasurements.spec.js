import { effectScope, nextTick, ref } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import api from '@/services/api'
import { useRecipeStore } from '@/stores/recipes'
import { useUiStore } from '@/stores/ui'
import { US_MEASUREMENT_DEBOUNCE_MS } from '@/constants/unitSystems'
import { useRecipeMeasurements } from './useRecipeMeasurements'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn() },
  assetUrl: vi.fn((p) => p ?? ''),
}))

const oil   = { id: 'oil',   amount: 5,   unit: 'ml',  sourceAmount: null, sourceUnit: null }
const flour = { id: 'flour', amount: 120, unit: 'g',   sourceAmount: 1,    sourceUnit: 'cup' }
const eggs  = { id: 'eggs',  amount: 2,   unit: 'pcs', sourceAmount: null, sourceUnit: null }
const recipe = ref({ id: 'r1', servings: 4, ingredients: [oil, flour, eggs] })

let scope
let servings
let ui
let store

/** Starts the composable at 4 servings, in `unitSystem`, with US Measurements known at those servings unless told otherwise. */
function start({ unitSystem = 'us', known = true } = {}) {
  ui.unitSystem = unitSystem
  if (known) {
    store.setUsMeasurements('r1', 4, [
      { recipeIngredientId: 'oil',   amount: 1,   unit: 'tsp', fromSource: false },
      { recipeIngredientId: 'flour', amount: 1,   unit: 'cup', fromSource: true },
    ])
  }
  scope = effectScope()
  return scope.run(() => useRecipeMeasurements(recipe, servings))
}

/** A promise for the next measurements request and its settle handles, so a test decides when and how it answers. */
function nextRequest() {
  let settle
  const promise = new Promise((resolve, reject) => { settle = { resolve, reject } })
  api.get.mockImplementationOnce(() => promise)
  return settle
}

const answer = rows => ({ data: rows })

beforeEach(() => {
  vi.useFakeTimers()
  api.get.mockReset()
  window.localStorage.clear()
  setActivePinia(createPinia())
  ui = useUiStore()
  store = useRecipeStore()
  store.currentRecipe = recipe.value
  servings = ref(4)
})

afterEach(() => {
  scope?.stop()
  vi.useRealTimers()
})

describe('useRecipeMeasurements', () => {
  it('reads as Metric with no server call in the Metric Unit System', async () => {
    const { measurementOf } = start({ unitSystem: 'metric' })
    servings.value = 8
    await vi.advanceTimersByTimeAsync(US_MEASUREMENT_DEBOUNCE_MS * 2)

    expect(measurementOf(oil)).toEqual({ amount: '10 ml', source: null })
    expect(measurementOf(flour)).toEqual({ amount: '240 g', source: '(2 cup)' })
    expect(api.get).not.toHaveBeenCalled()
  })

  it('reads the known US Measurements as fractions, hiding a source they were read from', () => {
    const { measurementOf } = start()

    expect(measurementOf(oil)).toEqual({ amount: '1 tsp', source: null })
    expect(measurementOf(flour)).toEqual({ amount: '1 cup', source: null })
    expect(measurementOf(eggs)).toEqual({ amount: '2', source: null })
    expect(api.get).not.toHaveBeenCalled()
  })

  it('scales on the device at once, then swaps in the server units about a second after the last change', async () => {
    const { measurementOf } = start()
    const request = nextRequest()

    servings.value = 8
    await nextTick()
    servings.value = 12
    await nextTick()
    expect(measurementOf(oil)).toEqual({ amount: '3 tsp', source: null })

    await vi.advanceTimersByTimeAsync(US_MEASUREMENT_DEBOUNCE_MS - 1)
    expect(api.get).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1)
    expect(api.get).toHaveBeenCalledOnce()
    expect(api.get).toHaveBeenCalledWith('/recipes/r1/measurements',
      expect.objectContaining({ params: { servings: 12, system: 'us' } }))

    request.resolve(answer([{ recipeIngredientId: 'oil', amount: 1, unit: 'tbsp', fromSource: false }]))
    await vi.runAllTimersAsync()
    expect(measurementOf(oil)).toEqual({ amount: '1 tbsp', source: null })
  })

  it('cancels a superseded request and ignores its late answer', async () => {
    const { measurementOf } = start()
    const first  = nextRequest()
    const second = nextRequest()

    servings.value = 12
    await vi.advanceTimersByTimeAsync(US_MEASUREMENT_DEBOUNCE_MS)
    const firstSignal = api.get.mock.calls[0][1].signal

    servings.value = 2
    await vi.advanceTimersByTimeAsync(US_MEASUREMENT_DEBOUNCE_MS)
    expect(firstSignal.aborted).toBe(true)

    second.resolve(answer([{ recipeIngredientId: 'oil', amount: 0.5, unit: 'tsp', fromSource: false }]))
    await vi.runAllTimersAsync()
    first.resolve(answer([{ recipeIngredientId: 'oil', amount: 1, unit: 'tbsp', fromSource: false }]))
    await vi.runAllTimersAsync()

    expect(measurementOf(oil)).toEqual({ amount: '½ tsp', source: null })
  })

  it('keeps the on-device value when the correction fails, showing no error', async () => {
    const { measurementOf } = start()
    const request = nextRequest()

    servings.value = 12
    await vi.advanceTimersByTimeAsync(US_MEASUREMENT_DEBOUNCE_MS)
    request.reject(new Error('Network Error'))
    await vi.runAllTimersAsync()

    expect(measurementOf(oil)).toEqual({ amount: '3 tsp', source: null })
    expect(ui.snackbar.show).toBe(false)
  })

  it('fetches at once when switched to US with no US Measurements known', async () => {
    const { measurementOf } = start({ unitSystem: 'metric', known: false })
    nextRequest().resolve(answer([{ recipeIngredientId: 'oil', amount: 1, unit: 'tsp', fromSource: false }]))

    ui.chooseUnitSystem('us')
    await nextTick()
    expect(api.get).toHaveBeenCalledOnce()
    await vi.runAllTimersAsync()

    expect(measurementOf(oil)).toEqual({ amount: '1 tsp', source: null })
  })
})
