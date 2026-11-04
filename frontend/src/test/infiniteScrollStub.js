import { defineComponent, h, shallowRef } from 'vue'

/** Stands in for VInfiniteScroll, which never loads under jsdom for want of IntersectionObserver, so `vm.scrollToEnd()` plays the user reaching the end. */
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
  // Renders the end slot for the status passed to done, as Vuetify's does in intersect mode.
  render() {
    const slotProps = { side: 'end', props: { onClick: this.scrollToEnd } }
    const endSlot = { error: 'error', empty: 'empty' }[this.status] ?? 'loading'
    return h('div', { class: 'v-infinite-scroll' }, [
      this.$slots.default?.(),
      h('div', { class: 'v-infinite-scroll__side' }, this.$slots[endSlot]?.(slotProps)),
    ])
  },
})
