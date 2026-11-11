import { computed } from 'vue'
import { useDisplay } from 'vuetify'

/**
 * The styles of a recipe form's Amount, Unit and Notes fields, which share one line on wider screens and on a phone
 * split the first line between Amount and Unit with Notes on its own line below.
 */
export function useIngredientRowLayout({ amountWidth }) {
  const { xs } = useDisplay()

  return computed(() => xs.value
    ? {
        amount:     { flex: '1 1 0' },
        unit:       { flex: '1 1 0', maxWidth: 'none' },
        notesClass: 'w-100',
      }
    : {
        amount:     { maxWidth: amountWidth },
        unit:       {},
        notesClass: 'flex-grow-1',
      })
}
