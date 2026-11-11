import {
  NAME_MAX_LENGTH, DESCRIPTION_MAX_LENGTH, MIN_SERVINGS, MAX_SERVINGS,
  INGREDIENT_NOTES_MAX_LENGTH, STEP_INSTRUCTION_MAX_LENGTH,
} from '@/constants/recipeLimits'
import { hasAmount } from '@/constants/units'

/** A problem with what a field holds, such as an empty name or an amount of 0. */
export const VALUE_RULE = 'value'

/** A field over its length limit, which shows only once Save has been pressed unless the value arrived that way. */
export const LENGTH_RULE = 'length'

const RECIPE_FIELD_LABELS = {
  name:           'Recipe name',
  description:    'Description',
  servings:       'Servings',
  ingredientList: 'Ingredients',
}

const problem = (message, rule = VALUE_RULE) => ({ message, rule })

const trimmed = value => String(value ?? '').trim()

function tooLong(value, maxLength) {
  return trimmed(value).length > maxLength
    ? problem(`Must be ${maxLength} characters or fewer`, LENGTH_RULE)
    : null
}

function nameProblem(name) {
  if (!trimmed(name)) return problem('Required')
  return tooLong(name, NAME_MAX_LENGTH)
}

function servingsProblem(servings) {
  const count = trimmed(servings) === '' ? NaN : Number(servings)
  return Number.isInteger(count) && count >= MIN_SERVINGS && count <= MAX_SERVINGS
    ? null
    : problem(`Must be a whole number from ${MIN_SERVINGS} to ${MAX_SERVINGS}`)
}

/** True while a row states an amount, has no unit, and arrived with one the app cannot store, such as "can", whose size pcs would lose. */
function mustPickUnit(ing) {
  return hasAmount(ing.amount) && !ing.unit && !!ing.unstorableUnit
}

function ingredientRowProblems(ing, { requireIngredient }) {
  return {
    ingredient: requireIngredient && !ing.ingredientId ? problem('Pick an ingredient') : null,
    amount:     hasAmount(ing.amount) && Number(ing.amount) <= 0
      ? problem('Must be more than 0, or left empty')
      : null,
    unit:       mustPickUnit(ing)
      ? problem(`Source said "${ing.unstorableUnit}": pick a unit or clear the amount.`)
      : null,
    notes:      tooLong(ing.notes, INGREDIENT_NOTES_MAX_LENGTH),
  }
}

function stepProblems(step) {
  return {
    instruction: trimmed(step.instruction)
      ? tooLong(step.instruction, STEP_INSTRUCTION_MAX_LENGTH)
      : problem('Required'),
  }
}

/** The problems in a field map as summary entries, keeping the map's field order, where `whereOf` names a field's place on the page. */
function summaryEntries(problems, { section, index, whereOf }) {
  return Object.entries(problems)
    .filter(([, found]) => found)
    .map(([field, { message, rule }]) => ({ section, index, field, rule, where: whereOf(field), message }))
}

/** Every problem that would stop a recipe form from saving, per field and as a summary in page order; the recipe form sets `requireIngredient`, because its rows pick from the catalogue. */
export function validateRecipe(form, { requireIngredient = false } = {}) {
  const recipe = {
    name:           nameProblem(form.name),
    description:    tooLong(form.description, DESCRIPTION_MAX_LENGTH),
    servings:       servingsProblem(form.servings),
    ingredientList: form.ingredients.length ? null : problem('Add at least one ingredient'),
  }
  const ingredients = form.ingredients.map(ing => ingredientRowProblems(ing, { requireIngredient }))
  const steps       = form.steps.map(stepProblems)

  const summary = [
    ...summaryEntries(recipe, { section: 'recipe', index: null, whereOf: field => RECIPE_FIELD_LABELS[field] }),
    ...ingredients.flatMap((problems, index) => {
      const name = form.ingredients[index].displayName
      const where = name ? `Ingredient ${index + 1} (${name})` : `Ingredient ${index + 1}`
      return summaryEntries(problems, { section: 'ingredient', index, whereOf: () => where })
    }),
    ...steps.flatMap((problems, index) =>
      summaryEntries(problems, { section: 'step', index, whereOf: () => `Step ${index + 1}` })),
  ]

  return { ...recipe, ingredients, steps, summary }
}
