import { validateRecipe, LENGTH_RULE, VALUE_RULE } from './recipeValidation'

const validForm = (overrides = {}) => ({
  name:        'Pasta',
  description: '',
  servings:    4,
  ingredients: [{ ingredientId: 'flour', displayName: 'Flour', amount: 250, unit: 'g', notes: '' }],
  steps:       [{ instruction: 'Boil the pasta.' }],
  ...overrides,
})

describe('validateRecipe', () => {
  it('finds nothing wrong with a valid recipe', () => {
    const result = validateRecipe(validForm())

    expect(result.name).toBeNull()
    expect(result.ingredients).toEqual([{ ingredient: null, amount: null, unit: null, notes: null }])
    expect(result.steps).toEqual([{ instruction: null }])
    expect(result.summary).toEqual([])
  })

  describe('name', () => {
    it.each(['', '   '])('is required (%j)', name => {
      expect(validateRecipe(validForm({ name })).name).toEqual({ message: 'Required', rule: VALUE_RULE })
    })

    it('allows 200 characters', () => {
      expect(validateRecipe(validForm({ name: 'x'.repeat(200) })).name).toBeNull()
    })

    it('rejects 201 characters as a length problem', () => {
      expect(validateRecipe(validForm({ name: 'x'.repeat(201) })).name)
        .toEqual({ message: 'Must be 200 characters or fewer', rule: LENGTH_RULE })
    })
  })

  describe('description', () => {
    it('is optional', () => {
      expect(validateRecipe(validForm({ description: '' })).description).toBeNull()
      expect(validateRecipe(validForm({ description: null })).description).toBeNull()
    })

    it('allows 2000 characters and rejects 2001 as a length problem', () => {
      expect(validateRecipe(validForm({ description: 'x'.repeat(2000) })).description).toBeNull()
      expect(validateRecipe(validForm({ description: 'x'.repeat(2001) })).description)
        .toEqual({ message: 'Must be 2000 characters or fewer', rule: LENGTH_RULE })
    })
  })

  describe('servings', () => {
    it.each([1, 100])('allows %d', servings => {
      expect(validateRecipe(validForm({ servings })).servings).toBeNull()
    })

    // An emptied number field reads as '' through v-model.number, and the API takes only a whole number.
    it.each([0, 101, -2, 2.5, '', null])('rejects %j', servings => {
      expect(validateRecipe(validForm({ servings })).servings)
        .toEqual({ message: 'Must be a whole number from 1 to 100', rule: VALUE_RULE })
    })
  })

  describe('ingredient list', () => {
    it('needs at least one ingredient', () => {
      expect(validateRecipe(validForm({ ingredients: [] })).ingredientList)
        .toEqual({ message: 'Add at least one ingredient', rule: VALUE_RULE })
    })

    it('is fine with one', () => {
      expect(validateRecipe(validForm()).ingredientList).toBeNull()
    })
  })

  describe('ingredient row', () => {
    const rowOf = row =>
      validateRecipe(validForm({ ingredients: [{ ...validForm().ingredients[0], ...row }] })).ingredients[0]

    it('needs an ingredient picked', () => {
      expect(rowOf({ ingredientId: null }).ingredient)
        .toEqual({ message: 'Pick an ingredient', rule: VALUE_RULE })
    })

    it('accepts a pending New ingredient in place of a picked one', () => {
      expect(rowOf({ ingredientId: null, isNew: true, name: 'bok choy' }).ingredient).toBeNull()
    })

    it.each(['', '  ', null])('needs a pending New ingredient to have a name (%j)', name => {
      expect(rowOf({ ingredientId: null, isNew: true, name }).ingredient)
        .toEqual({ message: 'Pick an ingredient', rule: VALUE_RULE })
    })

    it.each([0, -1, '0'])('rejects an amount of %j', amount => {
      expect(rowOf({ amount }).amount)
        .toEqual({ message: 'Must be more than 0, or left empty', rule: VALUE_RULE })
    })

    it.each(['', null, 0.5])('allows an amount of %j', amount => {
      expect(rowOf({ amount }).amount).toBeNull()
    })

    it('asks for a unit where the scraped one cannot be stored, naming what the source said', () => {
      expect(rowOf({ amount: 1, unit: null, unstorableUnit: 'can' }).unit)
        .toEqual({ message: 'Source said "can": pick a unit or clear the amount.', rule: VALUE_RULE })
    })

    it('needs no unit once the amount is cleared, or once one is picked', () => {
      expect(rowOf({ amount: '', unit: null, unstorableUnit: 'can' }).unit).toBeNull()
      expect(rowOf({ amount: 1, unit: 'g', unstorableUnit: 'can' }).unit).toBeNull()
    })

    it('lets an amount with no unit stand for a count', () => {
      expect(rowOf({ amount: 2, unit: null }).unit).toBeNull()
    })

    it('allows 500 characters of notes and rejects 501 as a length problem', () => {
      expect(rowOf({ notes: 'x'.repeat(500) }).notes).toBeNull()
      expect(rowOf({ notes: 'x'.repeat(501) }).notes)
        .toEqual({ message: 'Must be 500 characters or fewer', rule: LENGTH_RULE })
    })
  })

  describe('step', () => {
    const stepOf = instruction => validateRecipe(validForm({ steps: [{ instruction }] })).steps[0]

    it.each(['', '  '])('needs an instruction (%j)', instruction => {
      expect(stepOf(instruction).instruction).toEqual({ message: 'Required', rule: VALUE_RULE })
    })

    it('allows 2000 characters and rejects 2001 as a length problem', () => {
      expect(stepOf('x'.repeat(2000)).instruction).toBeNull()
      expect(stepOf('x'.repeat(2001)).instruction)
        .toEqual({ message: 'Must be 2000 characters or fewer', rule: LENGTH_RULE })
    })
  })

  describe('summary', () => {
    it('lists every problem in page order, saying where each one is', () => {
      const result = validateRecipe(validForm({
        name:        '',
        servings:    0,
        ingredients: [
          { ingredientId: 'a', displayName: 'Tomatoes', amount: 1, unit: null, unstorableUnit: 'can', notes: '' },
          { ingredientId: 'b', displayName: 'Beans',    amount: 1, unit: 'g', notes: '' },
          { ingredientId: null,                         amount: 0, unit: 'g', notes: 'x'.repeat(501) },
        ],
        steps: [{ instruction: 'Fine.' }, { instruction: '' }],
      }))

      expect(result.summary).toEqual([
        { section: 'recipe',     index: null, field: 'name',       rule: VALUE_RULE,  where: 'Recipe name',
          message: 'Required' },
        { section: 'recipe',     index: null, field: 'servings',   rule: VALUE_RULE,  where: 'Servings',
          message: 'Must be a whole number from 1 to 100' },
        { section: 'ingredient', index: 0,    field: 'unit',       rule: VALUE_RULE,  where: 'Ingredient 1 (Tomatoes)',
          message: 'Source said "can": pick a unit or clear the amount.' },
        { section: 'ingredient', index: 2,    field: 'ingredient', rule: VALUE_RULE,  where: 'Ingredient 3',
          message: 'Pick an ingredient' },
        { section: 'ingredient', index: 2,    field: 'amount',     rule: VALUE_RULE,  where: 'Ingredient 3',
          message: 'Must be more than 0, or left empty' },
        { section: 'ingredient', index: 2,    field: 'notes',      rule: LENGTH_RULE, where: 'Ingredient 3',
          message: 'Must be 500 characters or fewer' },
        { section: 'step',       index: 1,    field: 'instruction', rule: VALUE_RULE, where: 'Step 2',
          message: 'Required' },
      ])
    })

    it('names a missing ingredient list', () => {
      expect(validateRecipe(validForm({ ingredients: [] })).summary).toEqual([
        { section: 'recipe', index: null, field: 'ingredientList', rule: VALUE_RULE, where: 'Ingredients',
          message: 'Add at least one ingredient' },
      ])
    })
  })
})
