import { defineComponent, h, shallowRef } from 'vue'

/**
 * Stands in for Vuetify's VInfiniteScroll, which never loads under jsdom because jsdom has no IntersectionObserver.
 * `vm.scrollToEnd()` emits `load` as reaching the end of the list would, and the end of the list renders the slot for
 * the status passed to `done`, as Vuetify's does in intersect mode.
 */
export const InfiniteScrollStub = defineComponent({
  name: 'VInfiniteScroll',
  emits: ['load'],
  setup(_props, { emit }) {
    const status = shallowRef('ok')

    function scrollToEnd() {
      if (status.value === 'loading' || status.value === 'empty') return Promise.resolve()
      status.value = 'loading'
      return new Promise((resolve) => {
        emit('load', { side: 'end', done: (newStatus) => { status.value = newStatus; resolve() } })
      })
    }

    return { status, scrollToEnd }
  },
  render() {
    const slotProps = { side: 'end', props: { onClick: this.scrollToEnd } }
    const endSlot = { error: 'error', empty: 'empty' }[this.status] ?? 'loading'
    return h('div', { class: 'v-infinite-scroll' }, [
      this.$slots.default?.(),
      h('div', { class: 'v-infinite-scroll__side' }, this.$slots[endSlot]?.(slotProps)),
    ])
  },
})
