import { setActivePinia, createPinia } from 'pinia'
import { useIngredientStore } from './ingredients'
import { makeIngredient } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: {
    get:    vi.fn(),
    post:   vi.fn(),
    put:    vi.fn(),
    delete: vi.fn(),
  },
  assetUrl: vi.fn((path) => path ?? ''),
}))

import api from '@/services/api'

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

// ── fetchIngredients ──────────────────────────────────────────────────────────

describe('fetchIngredients', () => {
  it('populates store.ingredients from API response', async () => {
    const ingredients = [makeIngredient({ id: '1' }), makeIngredient({ id: '2', name: 'salt' })]
    api.get.mockResolvedValue({ data: ingredients })

    const store = useIngredientStore()
    await store.fetchIngredients()

    expect(store.ingredients).toHaveLength(2)
  })

  it('passes search query param to API when provided', async () => {
    api.get.mockResolvedValue({ data: [] })

    const store = useIngredientStore()
    await store.fetchIngredients('fl')

    expect(api.get).toHaveBeenCalledWith('/ingredients', { params: { search: 'fl' } })
  })

  it('sends no search param when search is empty', async () => {
    api.get.mockResolvedValue({ data: [] })

    const store = useIngredientStore()
    await store.fetchIngredients('')

    expect(api.get).toHaveBeenCalledWith('/ingredients', { params: {} })
  })
})

// ── fetchCatalogue ────────────────────────────────────────────────────────────

describe('fetchCatalogue', () => {
  it('loads the whole catalogue once, however often it is called', async () => {
    api.get.mockResolvedValue({ data: [makeIngredient()] })

    const store = useIngredientStore()
    await Promise.all([store.fetchCatalogue(), store.fetchCatalogue()])
    await store.fetchCatalogue()

    expect(api.get).toHaveBeenCalledTimes(1)
    expect(api.get).toHaveBeenCalledWith('/ingredients')
    expect(store.catalogue).toEqual([makeIngredient()])
  })

  it('stays silent when the load fails, and retries on the next call', async () => {
    api.get.mockRejectedValueOnce(new Error('offline'))
    api.get.mockResolvedValueOnce({ data: [makeIngredient()] })

    const store = useIngredientStore()
    await expect(store.fetchCatalogue()).resolves.toBeUndefined()
    expect(store.catalogue).toEqual([])
    expect(store.error).toBeNull()

    await store.fetchCatalogue()
    expect(store.catalogue).toEqual([makeIngredient()])
  })

  it('is kept current by createIngredient and updateIngredient', async () => {
    const flour = makeIngredient({ id: 'i1' })
    api.get.mockResolvedValue({ data: [flour] })
    const store = useIngredientStore()
    await store.fetchCatalogue()

    const sugar = makeIngredient({ id: 'i2', name: 'sugar', displayName: 'Sugar' })
    api.post.mockResolvedValue({ data: sugar })
    await store.createIngredient({})

    const strongFlour = makeIngredient({ id: 'i1', displayName: 'Strong Flour' })
    api.put.mockResolvedValue({ data: strongFlour })
    await store.updateIngredient('i1', {})

    expect(store.catalogue).toEqual([strongFlour, sugar])
  })
})

// ── fetchCategories ───────────────────────────────────────────────────────────

describe('fetchCategories', () => {
  it('fetches and populates categories on first call', async () => {
    const cats = ['DRY_GOODS', 'PRODUCE']
    api.get.mockResolvedValue({ data: cats })

    const store = useIngredientStore()
    await store.fetchCategories()

    expect(store.categories).toEqual(cats)
    expect(api.get).toHaveBeenCalledTimes(1)
  })

  it('skips the API on subsequent calls (cache hit)', async () => {
    api.get.mockResolvedValue({ data: ['DRY_GOODS'] })

    const store = useIngredientStore()
    await store.fetchCategories()
    await store.fetchCategories()

    expect(api.get).toHaveBeenCalledTimes(1)
  })
})

// ── createIngredient ──────────────────────────────────────────────────────────

describe('createIngredient', () => {
  it('calls POST /ingredients with correct body', async () => {
    const payload = { displayName: 'Sugar', category: 'DRY_GOODS', defaultUnit: 'g' }
    api.post.mockResolvedValue({ data: makeIngredient({ name: 'sugar' }) })

    const store = useIngredientStore()
    await store.createIngredient(payload)

    expect(api.post).toHaveBeenCalledWith('/ingredients', payload)
  })
})

// ── updateIngredient ──────────────────────────────────────────────────────────

describe('updateIngredient', () => {
  it('calls PUT /ingredients/:id with correct path and body', async () => {
    const payload = { displayName: 'Updated', category: 'DRY_GOODS' }
    api.put.mockResolvedValue({ data: makeIngredient({ id: 'i1', displayName: 'Updated' }) })

    const store = useIngredientStore()
    await store.updateIngredient('i1', payload)

    expect(api.put).toHaveBeenCalledWith('/ingredients/i1', payload)
  })
})

// ── Admin: every Ingredient, removed ones included ────────────────────────────

describe('fetchAllIngredients', () => {
  it('loads Removed Ingredients too, kept apart from the pickers\' lists', async () => {
    const onion = makeIngredient({ id: 'i1', removedAt: '2026-11-01T00:00:00Z' })
    api.get.mockResolvedValue({ data: [onion] })

    const store = useIngredientStore()
    await store.fetchAllIngredients()

    expect(api.get).toHaveBeenCalledWith('/ingredients', { params: { includeRemoved: true } })
    expect(store.allIngredients).toEqual([onion])
    expect(store.ingredients).toEqual([])
  })

  it('is kept current by createIngredient and updateIngredient', async () => {
    const flour = makeIngredient({ id: 'i1' })
    api.get.mockResolvedValue({ data: [flour] })
    const store = useIngredientStore()
    await store.fetchAllIngredients()

    const sugar = makeIngredient({ id: 'i2', displayName: 'Sugar' })
    api.post.mockResolvedValue({ data: sugar })
    await store.createIngredient({})

    const strongFlour = makeIngredient({ id: 'i1', displayName: 'Strong Flour' })
    api.put.mockResolvedValue({ data: strongFlour })
    await store.updateIngredient('i1', {})

    expect(store.allIngredients).toEqual([strongFlour, sugar])
  })
})

describe('removeIngredient', () => {
  it('removes it, marking it removed in the admin list and dropping it from the pickers', async () => {
    const flour = makeIngredient({ id: 'i1', removedAt: null })
    const salt  = makeIngredient({ id: 'i2', displayName: 'Salt', removedAt: null })
    api.get.mockResolvedValue({ data: [flour, salt] })
    const store = useIngredientStore()
    await Promise.all([store.fetchAllIngredients(), store.fetchCatalogue(), store.fetchIngredients()])
    api.delete.mockResolvedValue({})

    await store.removeIngredient('i1')

    expect(api.delete).toHaveBeenCalledWith('/ingredients/i1')
    expect(store.allIngredients.find(i => i.id === 'i1').removedAt).toEqual(expect.any(String))
    expect(store.catalogue.map(i => i.id)).toEqual(['i2'])
    expect(store.ingredients.map(i => i.id)).toEqual(['i2'])
  })
})

describe('restoreIngredient', () => {
  it('restores it into the admin list and the loaded catalogue', async () => {
    const removed = makeIngredient({ id: 'i1', removedAt: '2026-11-01T00:00:00Z' })
    api.get.mockResolvedValueOnce({ data: [removed] }).mockResolvedValueOnce({ data: [] })
    const store = useIngredientStore()
    await store.fetchAllIngredients()
    await store.fetchCatalogue()
    const restored = { ...removed, removedAt: null }
    api.post.mockResolvedValue({ data: restored })

    await store.restoreIngredient('i1')

    expect(api.post).toHaveBeenCalledWith('/ingredients/i1/restore')
    expect(store.allIngredients).toEqual([restored])
    expect(store.catalogue).toEqual([restored])
  })

  it('passes on a refusal, leaving it removed', async () => {
    const removed = makeIngredient({ id: 'i1', removedAt: '2026-11-01T00:00:00Z' })
    api.get.mockResolvedValue({ data: [removed] })
    const store = useIngredientStore()
    await store.fetchAllIngredients()
    api.post.mockRejectedValue({ status: 409, message: "'flour' is already used by Plain Flour." })

    await expect(store.restoreIngredient('i1')).rejects.toMatchObject({ status: 409 })
    expect(store.allIngredients).toEqual([removed])
  })
})

describe('fetchUsage', () => {
  it('returns how many recipes use the Ingredient', async () => {
    api.get.mockResolvedValue({ data: { recipeCount: 4 } })

    const store = useIngredientStore()

    await expect(store.fetchUsage('i1')).resolves.toBe(4)
    expect(api.get).toHaveBeenCalledWith('/ingredients/i1/usage')
  })
})
