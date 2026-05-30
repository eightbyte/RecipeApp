import { setupServer } from 'msw/node'

// Vuetify uses ResizeObserver internally; jsdom doesn't implement it
global.ResizeObserver = class ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

// Global MSW server — used by tests that need to intercept real HTTP requests.
// Store and api tests typically use vi.mock instead.
export const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'warn' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())
