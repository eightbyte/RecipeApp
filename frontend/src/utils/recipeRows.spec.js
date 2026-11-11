import { removeIngredient, restoreIngredient, removeStep, restoreStep } from './recipeRows'

const makeForm = () => ({
  ingredients: [{ name: 'flour' }, { name: 'sugar' }, { name: 'eggs' }, { name: 'milk' }],
  steps: [
    { instruction: 'Mix dry', ingredientIndexes: [0, 1] },
    { instruction: 'Beat in', ingredientIndexes: [2, 1, 3] },
    { instruction: 'Bake',    ingredientIndexes: [] },
  ],
})

describe('removeIngredient', () => {
  it('unselects the row from every step and shifts later indexes down', () => {
    const form = makeForm()

    removeIngredient(form, 1)

    expect(form.ingredients.map(i => i.name)).toEqual(['flour', 'eggs', 'milk'])
    expect(form.steps.map(s => s.ingredientIndexes)).toEqual([[0], [1, 2], []])
  })

  it('leaves every instruction as it was', () => {
    const form = makeForm()

    removeIngredient(form, 0)

    expect(form.steps.map(s => s.instruction)).toEqual(['Mix dry', 'Beat in', 'Bake'])
  })
})

describe('restoreIngredient', () => {
  it('puts the row back at its position with its step selections', () => {
    const form = makeForm()

    restoreIngredient(form, removeIngredient(form, 1))

    expect(form.ingredients.map(i => i.name)).toEqual(['flour', 'sugar', 'eggs', 'milk'])
    expect(form.steps.map(s => [...s.ingredientIndexes].sort())).toEqual([[0, 1], [1, 2, 3], []])
  })

  it('restores the same row object, so its edits come back too', () => {
    const form = makeForm()
    const sugar = form.ingredients[1]

    restoreIngredient(form, removeIngredient(form, 1))

    expect(form.ingredients[1]).toBe(sugar)
  })

  it('reselects the row in a step that was moved since the removal', () => {
    const form = makeForm()
    const removal = removeIngredient(form, 2)
    const beatIn = form.steps[1]
    form.steps.splice(0, 1)

    restoreIngredient(form, removal)

    expect(beatIn.ingredientIndexes).toContain(2)
  })

  it('puts the row at the end when rows have been removed since', () => {
    const form = makeForm()
    const removal = removeIngredient(form, 3)
    form.ingredients.splice(0, 2)

    restoreIngredient(form, removal)

    expect(form.ingredients.map(i => i.name)).toEqual(['eggs', 'milk'])
  })
})

describe('removeStep and restoreStep', () => {
  it('removes a step and puts the same step back at its position', () => {
    const form = makeForm()
    const beatIn = form.steps[1]

    const removal = removeStep(form, 1)
    expect(form.steps.map(s => s.instruction)).toEqual(['Mix dry', 'Bake'])

    restoreStep(form, removal)
    expect(form.steps[1]).toBe(beatIn)
    expect(form.steps.map(s => s.instruction)).toEqual(['Mix dry', 'Beat in', 'Bake'])
  })
})
