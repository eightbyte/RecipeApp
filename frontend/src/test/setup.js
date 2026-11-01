import { setupServer } from 'msw/node'
import { enableAutoUnmount } from '@vue/test-utils'

// Unmount every wrapper after each test, because a mounted v-img polls window.setTimeout forever in jsdom and can fire after teardown (guarded by setup.spec.js).
enableAutoUnmount(afterEach)

// Vuetify uses ResizeObserver internally; jsdom doesn't implement it
global.ResizeObserver = class ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

// Vuetify overlay location strategies use visualViewport; jsdom doesn't implement it
if (!global.visualViewport) {
  global.visualViewport = {
    width: 1024,
    height: 768,
    offsetTop: 0,
    offsetLeft: 0,
    scale: 1,
    addEventListener: () => {},
    removeEventListener: () => {},
  }
}

// Global MSW server for tests that intercept real HTTP requests, whereas store and api tests typically use vi.mock.
export const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'warn' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())
