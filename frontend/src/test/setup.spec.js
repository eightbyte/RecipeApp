// Guards enableAutoUnmount in setup.js. jsdom never loads images, so a mounted v-img re-arms a
// window.setTimeout poll every 100 ms until it is unmounted. Left mounted, that timer can fire
// after Vitest's jsdom teardown removes `window`, failing the run with an unhandled
// "ReferenceError: window is not defined" even though every test passed.
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'

const vuetify = createVuetify({ components })

// Long enough for several of VImg's 100 ms polls.
const simulatedTeardownMs = 350

// Stand-in for Vitest's jsdom teardown: `window` is gone while the file's timers can still fire.
afterAll(async () => {
  const windowDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'window')
  delete globalThis.window
  await new Promise((resolve) => setTimeout(resolve, simulatedTeardownMs))
  Object.defineProperty(globalThis, 'window', windowDescriptor)
})

describe('test setup', () => {
  it('unmounts a v-img the test leaves mounted, so its poll timer stops before teardown', () => {
    const wrapper = mount({ template: '<v-img src="/uploads/images/soup.jpg" />' }, { global: { plugins: [vuetify] } })
    expect(wrapper.find('.v-img').exists()).toBe(true)
  })
})
