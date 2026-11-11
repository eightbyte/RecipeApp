import { toRaw } from 'vue'

const keys    = new WeakMap()
let   lastKey = 0

/** A `v-for` key that stays with a row object, because rows removed and put back mid-list would otherwise hand their fields' state to a neighbour. */
export function rowKey(row) {
  const raw = toRaw(row)
  if (!keys.has(raw)) keys.set(raw, ++lastKey)
  return keys.get(raw)
}

/**
 * Removes a recipe form's ingredient row, unselecting it from every step and shifting later step indexes down,
 * and returns what `restoreIngredient` needs to undo it; instructions are left alone for the user to check.
 */
export function removeIngredient(form, index) {
  const [row] = form.ingredients.splice(index, 1)
  const usedBy = form.steps.filter(step => step.ingredientIndexes.includes(index))
  for (const step of form.steps) {
    step.ingredientIndexes = step.ingredientIndexes
      .filter(i => i !== index)
      .map(i => (i > index ? i - 1 : i))
  }
  return { row, index, usedBy }
}

/** Puts a removed ingredient row back at its position, or at the end if the list has since shrunk, reselecting it in the steps that used it. */
export function restoreIngredient(form, { row, index, usedBy }) {
  const at = Math.min(index, form.ingredients.length)
  form.ingredients.splice(at, 0, row)
  for (const step of form.steps) {
    step.ingredientIndexes = step.ingredientIndexes.map(i => (i >= at ? i + 1 : i))
    // Steps are matched by object, because a step removed or moved since keeps its identity but not its position.
    if (usedBy.includes(step)) step.ingredientIndexes.push(at)
  }
}

/** Removes a recipe form's step and returns what `restoreStep` needs to undo it. */
export function removeStep(form, index) {
  const [step] = form.steps.splice(index, 1)
  return { step, index }
}

/** Puts a removed step back at its position, or at the end if the list has since shrunk. */
export function restoreStep(form, { step, index }) {
  form.steps.splice(Math.min(index, form.steps.length), 0, step)
}
