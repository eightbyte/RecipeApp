import { setActivePinia, createPinia } from 'pinia'
import { useRecipeStore } from './recipes'
import { makeRecipe, makeRecipeDetail, makeRecipePage } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: {
    get:    vi.fn(),
    post:   vi.fn(),
    put:    vi.fn(),
    delete: vi.fn(),
  },
  assetUrl: vi.fn((path) => path ?? ''),
}))

import api, { assetUrl } from '@/services/api'

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

// ── fetchRecipePage ───────────────────────────────────────────────────────────

describe('fetchRecipePage', () => {
  it('returns the page items and the total count from the API', async () => {
    const recipes = [makeRecipe({ id: '1', name: 'A' }), makeRecipe({ id: '2', name: 'B' })]
    api.get.mockResolvedValue({ data: makeRecipePage(recipes, { totalCount: 30 }) })

    const store = useRecipeStore()
    const page = await store.fetchRecipePage({ search: 'a', page: 2 })

    expect(api.get).toHaveBeenCalledWith('/recipes', { params: { search: 'a', page: 2 }, signal: undefined })
    expect(page.items.map(r => r.id)).toEqual(['1', '2'])
    expect(page.totalCount).toBe(30)
  })

  it('passes the abort signal to the request', async () => {
    api.get.mockResolvedValue({ data: makeRecipePage([]) })
    const controller = new AbortController()

    const store = useRecipeStore()
    await store.fetchRecipePage({ page: 1 }, { signal: controller.signal })

    expect(api.get.mock.lastCall[1].signal).toBe(controller.signal)
  })

  it('rejects on API failure without touching the store loading or error state', async () => {
    api.get.mockRejectedValue({ message: 'Network error' })

    const store = useRecipeStore()
    await expect(store.fetchRecipePage({ page: 1 })).rejects.toMatchObject({ message: 'Network error' })

    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('normalises a relative imageUrl using assetUrl', async () => {
    assetUrl.mockImplementation((p) => p ? `http://localhost:5000${p}` : '')
    api.get.mockResolvedValue({ data: makeRecipePage([makeRecipe({ imageUrl: '/uploads/images/foo.jpg' })]) })

    const store = useRecipeStore()
    const page = await store.fetchRecipePage({ page: 1 })

    expect(page.items[0].imageUrl).toBe('http://localhost:5000/uploads/images/foo.jpg')
  })

  it('leaves an absolute imageUrl unchanged', async () => {
    const absUrl = 'http://cdn.example.com/img.jpg'
    assetUrl.mockImplementation((p) => p ?? '')
    api.get.mockResolvedValue({ data: makeRecipePage([makeRecipe({ imageUrl: absUrl })]) })

    const store = useRecipeStore()
    const page = await store.fetchRecipePage({ page: 1 })

    expect(page.items[0].imageUrl).toBe(absUrl)
  })

  it('converts null imageUrl to empty string via assetUrl', async () => {
    assetUrl.mockImplementation((p) => p ?? '')
    api.get.mockResolvedValue({ data: makeRecipePage([makeRecipe({ imageUrl: null })]) })

    const store = useRecipeStore()
    const page = await store.fetchRecipePage({ page: 1 })

    expect(page.items[0].imageUrl).toBe('')
  })
})

// ── fetchRecipe ───────────────────────────────────────────────────────────────

describe('fetchRecipe', () => {
  it('populates currentRecipe from API response', async () => {
    const detail = makeRecipeDetail({ id: 'r1', name: 'My Recipe' })
    api.get.mockResolvedValue({ data: detail })

    const store = useRecipeStore()
    await store.fetchRecipe('r1')

    expect(store.currentRecipe).toMatchObject({ id: 'r1', name: 'My Recipe' })
  })
})

// ── createRecipe ──────────────────────────────────────────────────────────────

describe('createRecipe', () => {
  it('calls POST /recipes with correct body', async () => {
    const payload = { name: 'New', servings: 4, ingredients: [], steps: [] }
    const created = makeRecipeDetail()
    api.post.mockResolvedValue({ data: created })

    const store = useRecipeStore()
    await store.createRecipe(payload)

    expect(api.post).toHaveBeenCalledWith('/recipes', payload)
  })
})

// ── updateRecipe ──────────────────────────────────────────────────────────────

describe('updateRecipe', () => {
  it('calls PUT /recipes/:id with correct path and body', async () => {
    const payload = { name: 'Updated', servings: 2, ingredients: [], steps: [] }
    api.put.mockResolvedValue({ data: makeRecipeDetail({ id: 'r1' }) })

    const store = useRecipeStore()
    await store.updateRecipe('r1', payload)

    expect(api.put).toHaveBeenCalledWith('/recipes/r1', payload)
  })
})

// ── deleteRecipe ──────────────────────────────────────────────────────────────

describe('deleteRecipe', () => {
  it('calls DELETE /recipes/:id', async () => {
    api.delete.mockResolvedValue({})

    const store = useRecipeStore()
    await store.deleteRecipe('r1')

    expect(api.delete).toHaveBeenCalledWith('/recipes/r1')
  })
})

// ── markCooked ────────────────────────────────────────────────────────────────

describe('markCooked', () => {
  it('calls POST /recipes/:id/cook', async () => {
    api.post.mockResolvedValue({ data: makeRecipeDetail() })

    const store = useRecipeStore()
    await store.markCooked('r1')

    expect(api.post).toHaveBeenCalledWith('/recipes/r1/cook')
  })
})

// ── isRecentlyCooked ──────────────────────────────────────────────────────────

describe('isRecentlyCooked', () => {
  it('returns true when cooked within 7 days', () => {
    const store = useRecipeStore()
    const recipe = makeRecipe({ lastCookedAt: new Date(Date.now() - 3 * 86400_000).toISOString() })
    expect(store.isRecentlyCooked(recipe)).toBe(true)
  })

  it('returns false when cooked more than 7 days ago', () => {
    const store = useRecipeStore()
    const recipe = makeRecipe({ lastCookedAt: new Date(Date.now() - 8 * 86400_000).toISOString() })
    expect(store.isRecentlyCooked(recipe)).toBe(false)
  })

  it('returns false when lastCookedAt is null', () => {
    const store = useRecipeStore()
    expect(store.isRecentlyCooked(makeRecipe({ lastCookedAt: null }))).toBe(false)
  })

  it('respects custom days window', () => {
    const store = useRecipeStore()
    const recipe = makeRecipe({ lastCookedAt: new Date(Date.now() - 3 * 86400_000).toISOString() })
    expect(store.isRecentlyCooked(recipe, 2)).toBe(false)
  })
})

// ── scrapeRecipe ──────────────────────────────────────────────────────────────

const makeScrapePreview = (overrides = {}) => ({
  name: 'Pasta Bolognese',
  description: 'A classic dish',
  servings: 4,
  sourceUrl: 'https://example.com/bolognese',
  ingredients: [],
  steps: [],
  ...overrides,
})

describe('scrapeRecipe', () => {
  it('sets scrapeLoading true during fetch, false after', async () => {
    let resolve
    api.post.mockReturnValue(new Promise(r => { resolve = r }))

    const store = useRecipeStore()
    const promise = store.scrapeRecipe('https://example.com')
    expect(store.scrapeLoading).toBe(true)

    resolve({ data: makeScrapePreview() })
    await promise
    expect(store.scrapeLoading).toBe(false)
  })

  it('stores response in scrapePreview on success', async () => {
    const preview = makeScrapePreview({ name: 'My Recipe' })
    api.post.mockResolvedValue({ data: preview })

    const store = useRecipeStore()
    await store.scrapeRecipe('https://example.com')

    expect(store.scrapePreview).toMatchObject({ name: 'My Recipe' })
    expect(store.scrapeError).toBeNull()
  })

  it('sets scrapeError on API failure', async () => {
    api.post.mockRejectedValue({ message: 'URL unreachable' })

    const store = useRecipeStore()
    await store.scrapeRecipe('https://bad-url.com')

    expect(store.scrapeError).toBe('URL unreachable')
    expect(store.scrapePreview).toBeNull()
  })
})

// ── confirmScrape ─────────────────────────────────────────────────────────────

describe('confirmScrape', () => {
  it('calls POST /recipes/scrape/confirm and returns the new recipe ID with whether its photo was skipped', async () => {
    const recipe = makeRecipeDetail({ id: 'new-recipe-id' })
    api.post.mockResolvedValue({ data: { recipe, photoSkipped: true } })

    const store = useRecipeStore()
    const saved = await store.confirmScrape({ name: 'Bolognese' })

    expect(api.post).toHaveBeenCalledWith('/recipes/scrape/confirm', { name: 'Bolognese' })
    expect(saved).toEqual({ id: 'new-recipe-id', photoSkipped: true })
  })
})

// ── clearScrapePreview ────────────────────────────────────────────────────────

describe('clearScrapePreview', () => {
  it('resets scrapePreview and scrapeError to null', async () => {
    api.post.mockResolvedValue({ data: makeScrapePreview() })

    const store = useRecipeStore()
    await store.scrapeRecipe('https://example.com')
    expect(store.scrapePreview).not.toBeNull()

    store.clearScrapePreview()
    expect(store.scrapePreview).toBeNull()
    expect(store.scrapeError).toBeNull()
  })
})
