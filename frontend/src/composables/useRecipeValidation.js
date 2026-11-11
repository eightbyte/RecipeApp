import { computed, nextTick, reactive, ref, toRaw, watch } from 'vue'
import { validateRecipe, LENGTH_RULE } from '@/utils/recipeValidation'

/** The class `RecipeProblemsBanner` carries, which Save scrolls to when no field is in error. */
export const PROBLEMS_BANNER_CLASS = 'recipe-problems'

/**
 * Decides which of a recipe form's problems are showing: those it arrived with until fixed, those in a field the
 * user has left (except length, which waits for Save), and every one once Save has been pressed.
 */
export function useRecipeValidation(form) {
  const result = computed(() => validateRecipe(form))

  const saveAttempted = ref(false)
  // Each map is keyed by the form for recipe fields and by the row object otherwise, so removing a row does not pass its state to the next one.
  const leftFields  = reactive(new WeakMap())
  const arrived     = reactive(new Map())
  const freshRows   = reactive(new WeakSet())

  function ownerOf({ section, index }) {
    if (section === 'ingredient') return form.ingredients[index]
    if (section === 'step')       return form.steps[index]
    return form
  }

  const has = (map, owner, field) => !!map.get(owner)?.has(field)

  function add(map, owner, field) {
    if (!map.has(owner)) map.set(owner, new Set())
    map.get(owner).add(field)
  }

  function wasLeft(entry) {
    const owner = ownerOf(entry)
    // A row just added has nothing worth flagging until the user has been through one of its fields.
    return freshRows.has(owner) ? !!leftFields.get(owner)?.size : has(leftFields, owner, entry.field)
  }

  const shownProblems = computed(() => result.value.summary.filter(entry =>
    saveAttempted.value || has(arrived, ownerOf(entry), entry.field)
    || (entry.rule !== LENGTH_RULE && wasLeft(entry))))

  const isOnForm = owner => toRaw(owner) === toRaw(form)
    || form.ingredients.includes(owner) || form.steps.includes(owner)

  // An arrived problem the user fixes is forgotten, so breaking it again follows the rules for the user's own problems; a removed row is not a fix, since Undo can bring it back.
  watch(() => result.value.summary, summary => {
    for (const [owner, fields] of arrived) {
      if (!isOnForm(owner)) continue
      for (const field of fields) {
        const broken = summary.some(entry => entry.field === field && toRaw(ownerOf(entry)) === toRaw(owner))
        if (!broken) fields.delete(field)
      }
    }
  })

  /** Records the problems the loaded recipe already has, so they show before the user touches anything. */
  function markArrived() {
    for (const entry of result.value.summary) add(arrived, ownerOf(entry), entry.field)
  }

  /** Marks a row the user has just added, whose problems wait until one of its fields has been left. */
  function markFresh(row) {
    freshRows.add(row)
  }

  /** Handles a field's `update:focused`, marking it left on blur; a recipe field is recorded on the form, a row field on its row. */
  function leaveOnBlur(focused, field, row = form) {
    if (!focused) add(leftFields, row, field)
  }

  const shownIn = (section, index) =>
    shownProblems.value.filter(entry => entry.section === section && entry.index === index)

  /** The messages showing for one field, for a Vuetify `error-messages` prop. */
  function messagesFor(section, index, field) {
    return shownIn(section, index).filter(entry => entry.field === field).map(entry => entry.message)
  }

  /** The fields of one ingredient or step row with a problem showing. */
  function shownFieldsOf(section, index) {
    return shownIn(section, index).map(entry => entry.field)
  }

  /** Shows every problem and scrolls the first field in error, or else the banner, into view, returning whether the recipe can be saved. */
  async function attemptSave(rootEl) {
    saveAttempted.value = true
    if (!result.value.summary.length) return true
    await nextTick()
    const target = rootEl?.querySelector('.v-input--error') ?? rootEl?.querySelector(`.${PROBLEMS_BANNER_CLASS}`)
    target?.scrollIntoView?.({ behavior: 'smooth', block: 'center' })
    return false
  }

  return { shownProblems, markArrived, markFresh, leaveOnBlur, messagesFor, shownFieldsOf, attemptSave }
}
