import { setActivePinia, createPinia } from 'pinia'
import { useRecipePages } from './useRecipePages'
import { makeRecipe, makeRecipePage } from '@/test/factories'

vi.mock('@/services/api', () => ({
  default: { get: vi.fn() },
  assetUrl: vi.fn((path) => path ?? ''),
}))

import api from '@/services/api'

const recipes = (...ids) => ids.map(id => makeRecipe({ id }))
const pageResponse = (items, totalCount) => ({ data: makeRecipePage(items, { totalCount }) })
const requestedParams = () => api.get.mock.calls.map(([, config]) => config.params)
const loadedIds = (pages) => pages.items.value.map(recipe => recipe.id)

function deferred() {
  let resolve, reject
  const promise = new Promise((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

describe('useRecipePages', () => {
  it('loads page 1 for the list params, then appends the next page', async () => {
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('a', 'b'), 5))
      .mockResolvedValueOnce(pageResponse(recipes('c', 'd'), 5))
    const pages = useRecipePages(() => ({ search: 'soup' }))

    await pages.reload()
    const status = await pages.loadMore()

    expect(status).toBe('ok')
    expect(requestedParams()).toEqual([{ search: 'soup', page: 1 }, { search: 'soup', page: 2 }])
    expect(loadedIds(pages)).toEqual(['a', 'b', 'c', 'd'])
    expect(pages.totalCount.value).toBe(5)
    expect(pages.pageCount.value).toBe(2)
  })

  it('resolves empty once every recipe is in, without requesting past the end', async () => {
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('a', 'b'), 3))
      .mockResolvedValueOnce(pageResponse(recipes('c'), 3))
    const pages = useRecipePages(() => ({}))

    await pages.reload()
    expect(pages.isComplete.value).toBe(false)
    expect(await pages.loadMore()).toBe('empty')
    expect(await pages.loadMore()).toBe('empty')

    expect(api.get).toHaveBeenCalledTimes(2)
    expect(pages.isComplete.value).toBe(true)
  })

  it('is complete after page 1 when page 1 holds every recipe', async () => {
    api.get.mockResolvedValueOnce(pageResponse(recipes('a', 'b'), 2))
    const pages = useRecipePages(() => ({}))

    await pages.reload()

    expect(pages.isComplete.value).toBe(true)
    expect(await pages.loadMore()).toBe('empty')
    expect(api.get).toHaveBeenCalledTimes(1)
  })

  it('skips a recipe that shifted onto the next page, and ends at a page with no recipes', async () => {
    // A recipe added at the top between loads pushes "b" onto page 2 and leaves the count one short of the total.
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('a', 'b'), 4))
      .mockResolvedValueOnce(pageResponse(recipes('b', 'c'), 5))
      .mockResolvedValueOnce(pageResponse(recipes('d'), 5))
      .mockResolvedValueOnce(pageResponse([], 5))
    const pages = useRecipePages(() => ({}))

    await pages.reload()
    expect(await pages.loadMore()).toBe('ok')
    expect(await pages.loadMore()).toBe('ok')
    expect(await pages.loadMore()).toBe('empty')

    expect(loadedIds(pages)).toEqual(['a', 'b', 'c', 'd'])
    expect(pages.isComplete.value).toBe(true)
  })

  it('sends no second request while a page is in flight', async () => {
    const pageTwo = deferred()
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('a', 'b'), 6))
      .mockReturnValueOnce(pageTwo.promise)
    const pages = useRecipePages(() => ({}))
    await pages.reload()

    const first  = pages.loadMore()
    const second = pages.loadMore()
    pageTwo.resolve(pageResponse(recipes('c', 'd'), 6))

    expect(await first).toBe('ok')
    expect(await second).toBe('ok')
    expect(api.get).toHaveBeenCalledTimes(2)
    expect(loadedIds(pages)).toEqual(['a', 'b', 'c', 'd'])
  })

  it('keeps the loaded recipes when a later page fails, and asks for the same page on retry', async () => {
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('a', 'b'), 4))
      .mockRejectedValueOnce({ message: 'Network error' })
      .mockResolvedValueOnce(pageResponse(recipes('c', 'd'), 4))
    const pages = useRecipePages(() => ({}))
    await pages.reload()

    expect(await pages.loadMore()).toBe('error')
    expect(loadedIds(pages)).toEqual(['a', 'b'])
    expect(pages.error.value).toBeNull()

    expect(await pages.loadMore()).toBe('empty')
    expect(requestedParams().map(params => params.page)).toEqual([1, 2, 2])
    expect(loadedIds(pages)).toEqual(['a', 'b', 'c', 'd'])
  })

  it('flags page 1 as loading, then reports why it failed', async () => {
    const pageOne = deferred()
    api.get.mockReturnValueOnce(pageOne.promise)
    const pages = useRecipePages(() => ({}))

    const reloading = pages.reload()
    expect(pages.loading.value).toBe(true)
    pageOne.reject({ message: 'Network error' })
    await reloading

    expect(pages.loading.value).toBe(false)
    expect(pages.error.value).toBe('Network error')
    expect(pages.items.value).toEqual([])
  })

  it('keeps the shown recipes until the new page 1 replaces them, loading no more meanwhile', async () => {
    const newPageOne = deferred()
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('a', 'b'), 4))
      .mockReturnValueOnce(newPageOne.promise)
    const pages = useRecipePages(() => ({}))
    await pages.reload()

    const reloading = pages.reload()
    expect(await pages.loadMore()).toBe('empty')
    expect(loadedIds(pages)).toEqual(['a', 'b'])

    newPageOne.resolve(pageResponse(recipes('c'), 1))
    await reloading

    expect(api.get).toHaveBeenCalledTimes(2)
    expect(loadedIds(pages)).toEqual(['c'])
  })

  it('clears the shown recipes when the new page 1 fails', async () => {
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('a', 'b'), 4))
      .mockRejectedValueOnce({ message: 'Network error' })
    const pages = useRecipePages(() => ({}))
    await pages.reload()

    await pages.reload()

    expect(pages.items.value).toEqual([])
    expect(pages.pageCount.value).toBe(0)
    expect(pages.error.value).toBe('Network error')
  })

  it('starts again from page 1 when the params change, aborting the page in flight and ignoring its response', async () => {
    const stalePageTwo = deferred()
    const newPageTwo   = deferred()
    api.get
      .mockResolvedValueOnce(pageResponse(recipes('a1', 'a2'), 4))
      .mockReturnValueOnce(stalePageTwo.promise)
      .mockResolvedValueOnce(pageResponse(recipes('b1', 'b2'), 4))
      .mockReturnValueOnce(newPageTwo.promise)
    let search = 'a'
    const pages = useRecipePages(() => ({ search }))
    await pages.reload()
    const staleLoadMore = pages.loadMore()

    search = 'b'
    await pages.reload()
    const newLoadMore = pages.loadMore()
    stalePageTwo.resolve(pageResponse(recipes('a3', 'a4'), 4))
    await staleLoadMore
    newPageTwo.resolve(pageResponse(recipes('b3', 'b4'), 4))

    expect(await newLoadMore).toBe('empty')
    expect(api.get.mock.calls[1][1].signal.aborted).toBe(true)
    expect(requestedParams()).toEqual([
      { search: 'a', page: 1 }, { search: 'a', page: 2 },
      { search: 'b', page: 1 }, { search: 'b', page: 2 },
    ])
    expect(loadedIds(pages)).toEqual(['b1', 'b2', 'b3', 'b4'])
  })

  it('ignores a page 1 that a newer reload replaced, resolving false for it', async () => {
    const stalePageOne = deferred()
    const newPageOne   = deferred()
    api.get
      .mockReturnValueOnce(stalePageOne.promise)
      .mockReturnValueOnce(newPageOne.promise)
    const pages = useRecipePages(() => ({}))

    const staleReload = pages.reload()
    const newReload   = pages.reload()
    stalePageOne.resolve(pageResponse(recipes('stale'), 1))

    expect(await staleReload).toBe(false)
    expect(pages.items.value).toEqual([])
    expect(pages.loading.value).toBe(true)

    newPageOne.resolve(pageResponse(recipes('fresh'), 1))

    expect(await newReload).toBe(true)
    expect(loadedIds(pages)).toEqual(['fresh'])
    expect(pages.loading.value).toBe(false)
    expect(api.get.mock.calls[0][1].signal.aborted).toBe(true)
  })
})
