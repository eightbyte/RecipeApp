import { computed, nextTick, reactive, ref } from 'vue'
import { validateRecipe, LENGTH_RULE } from '@/utils/recipeValidation'

/**
 * Decides which of a recipe form's problems are showing: those it arrived with, those in a field the user has left
 * (except length, which waits for Save), and every one once Save has been pressed.
 */
export function useRecipeValidation(form, options = {}) {
  const result = computed(() => validateRecipe(form, options))

  const saveAttempted = ref(false)
  const touchedFields = reactive(new Set())
  // Rows are tracked by object rather than index, so removing a row does not pass its state to the next one.
  const touchedRows   = reactive(new WeakSet())
  const arrivedKeys   = reactive(new Set())
  const arrivedRows   = reactive(new WeakMap())

  const rowOf = ({ section, index }) => {
    if (section === 'ingredient') return form.ingredients[index]
    if (section === 'step')       return form.steps[index]
    return null
  }

  function arrivedWith(entry) {
    const row = rowOf(entry)
    return row ? !!arrivedRows.get(row)?.has(entry.field) : arrivedKeys.has(entry.field)
  }

  function touched(entry) {
    const row = rowOf(entry)
    return row ? touchedRows.has(row) : touchedFields.has(entry.field)
  }

  const shownProblems = computed(() => result.value.summary.filter(entry =>
    saveAttempted.value || arrivedWith(entry) || (entry.rule !== LENGTH_RULE && touched(entry))))

  /** Records the problems the loaded recipe already has, so they show before the user touches anything. */
  function markArrived() {
    for (const entry of result.value.summary) {
      const row = rowOf(entry)
      if (!row) { arrivedKeys.add(entry.field); continue }
      if (!arrivedRows.has(row)) arrivedRows.set(row, new Set())
      arrivedRows.get(row).add(entry.field)
    }
  }

  /** Marks a recipe field as left, so its problems show from now on. */
  function touchField(field) {
    touchedFields.add(field)
  }

  /** Marks an ingredient or step row as left, so a freshly added blank row shows its problems once any field is left. */
  function touchRow(row) {
    touchedRows.add(row)
  }

  /** The messages showing for one field, for a Vuetify `error-messages` prop. */
  function messagesFor(section, index, field) {
    return shownProblems.value
      .filter(entry => entry.section === section && entry.index === index && entry.field === field)
      .map(entry => entry.message)
  }

  /** The fields of one ingredient or step row with a problem showing. */
  function shownFieldsOf(section, index) {
    return shownProblems.value
      .filter(entry => entry.section === section && entry.index === index)
      .map(entry => entry.field)
  }

  /** Shows every problem and scrolls the first field in error into view, returning whether the recipe can be saved. */
  async function attemptSave(rootEl) {
    saveAttempted.value = true
    if (!result.value.summary.length) return true
    await nextTick()
    rootEl?.querySelector('.v-input--error')?.scrollIntoView?.({ behavior: 'smooth', block: 'center' })
    return false
  }

  return { shownProblems, markArrived, touchField, touchRow, messagesFor, shownFieldsOf, attemptSave }
}
