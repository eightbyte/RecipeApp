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
    const payload = { name: 'sugar', displayName: 'Sugar', category: 'DRY_GOODS', defaultUnit: 'g' }
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
