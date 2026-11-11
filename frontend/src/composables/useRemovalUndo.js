import { onBeforeUnmount } from 'vue'
import { useUiStore } from '@/stores/ui'
import { removeIngredient, restoreIngredient, removeStep, restoreStep } from '@/utils/recipeRows'

/**
 * Removes a recipe form's ingredient rows and steps, offering Undo in the snackbar; only the most recent removal can be
 * undone, and it is final once the snackbar goes.
 */
export function useRemovalUndo(form) {
  const ui = useUiStore()
  let offeredId = null

  function offerUndo(message, undo) {
    ui.notify({ message, color: 'info', action: { label: 'Undo', onClick: undo } })
    offeredId = ui.snackbar.id
  }

  // An Undo left showing after the page has gone would restore a row into a form nobody can see.
  onBeforeUnmount(() => {
    if (ui.snackbar.show && ui.snackbar.id === offeredId) ui.dismiss()
  })

  return {
    removeIngredient(index) {
      const removal = removeIngredient(form, index)
      offerUndo('Ingredient removed', () => restoreIngredient(form, removal))
    },
    removeStep(index) {
      const removal = removeStep(form, index)
      offerUndo('Step removed', () => restoreStep(form, removal))
    },
  }
}
