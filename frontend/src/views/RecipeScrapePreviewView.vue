<template>
  <v-container ref="containerRef" class="pa-2 pa-sm-4" max-width="600">

    <!-- Loading bar during save -->
    <v-progress-linear v-if="store.confirmLoading" indeterminate color="primary" class="mb-2" />

    <h1 class="text-h6 font-weight-bold mb-6">Review Imported Recipe</h1>

    <!-- No preview guard -->
    <v-alert v-if="!store.scrapePreview" type="warning" class="mb-6">
      No preview available. Please <router-link :to="{ name: 'recipes' }">go back</router-link> and import a recipe.
    </v-alert>

    <template v-else>

      <RecipeProblemsBanner :problems="validation.shownProblems.value" />

      <!-- ── Basic info ──────────────────────────────────────────── -->
      <v-card class="mb-6 pa-3 pa-sm-4">
        <div class="text-subtitle-2 text-medium-emphasis mb-4">Basic Info</div>

        <v-text-field
          v-model="form.name"
          label="Recipe name"
          :error-messages="validation.messagesFor('recipe', null, 'name')"
          required
          class="mb-2"
          @update:focused="validation.leaveOnBlur($event, 'name')"
        />

        <v-textarea
          v-model="form.description"
          label="Description (optional)"
          rows="2"
          auto-grow
          :error-messages="validation.messagesFor('recipe', null, 'description')"
          class="mb-2"
          @update:focused="validation.leaveOnBlur($event, 'description')"
        />

        <div class="d-flex ga-4">
          <v-text-field
            v-model.number="form.servings"
            label="Servings"
            type="number"
            :min="MIN_SERVINGS"
            :max="MAX_SERVINGS"
            :error-messages="validation.messagesFor('recipe', null, 'servings')"
            style="max-width: 120px"
            @update:focused="validation.leaveOnBlur($event, 'servings')"
          />
          <v-text-field
            :model-value="form.sourceUrl"
            label="Source URL"
            class="flex-grow-1"
            readonly
            variant="filled"
            hint="Source URL cannot be edited here"
            persistent-hint
          />
        </div>
      </v-card>

      <!-- ── Ingredients ─────────────────────────────────────────── -->
      <v-card class="mb-6 pa-3 pa-sm-4">
        <div class="d-flex align-center justify-space-between mb-4">
          <div class="text-subtitle-2 text-medium-emphasis">Ingredients</div>
          <v-btn
            size="small"
            variant="tonal"
            color="primary"
            prepend-icon="mdi-plus"
            @click="addIngredient"
          >
            Add
          </v-btn>
        </div>

        <div
          v-for="(ing, idx) in form.ingredients"
          :key="rowKey(ing)"
          class="mb-6"
        >
          <div class="d-flex align-start ga-2 mb-2">
            <span class="text-caption text-medium-emphasis mt-3" style="min-width: 20px">{{ idx + 1 }}.</span>
            <v-autocomplete
              :model-value="pickedValueOf(ing)"
              :items="itemsFor(ing)"
              :custom-filter="matchTitleOrAddNew"
              item-title="displayName"
              item-value="id"
              label="Ingredient"
              density="compact"
              hide-details="auto"
              class="flex-grow-1"
              style="min-width: 0"
              :error-messages="validation.messagesFor('ingredient', idx, 'ingredient')"
              @update:search="searches.set(ing, $event ?? '')"
              @update:model-value="onIngredientPicked(ing, $event)"
              @update:focused="validation.leaveOnBlur($event, 'ingredient', ing)"
            />
            <div class="d-flex flex-column align-end ga-1 mt-2">
              <v-chip v-if="ing.isNew" size="x-small" color="info" variant="flat">New</v-chip>
              <v-chip v-if="rowChip(idx)" size="x-small" color="error" variant="flat">{{ rowChip(idx) }}</v-chip>
            </div>
            <v-btn
              icon="mdi-close"
              size="small"
              variant="text"
              color="error"
              :aria-label="`Remove ingredient ${idx + 1}`"
              @click="removeIngredient(idx)"
            />
          </div>

          <div class="d-flex flex-wrap flex-sm-nowrap ga-2 pl-3 pl-sm-6">
            <v-text-field
              v-model.number="ing.amount"
              label="Amount"
              type="number"
              min="0"
              step="0.001"
              density="compact"
              hide-details="auto"
              :error-messages="validation.messagesFor('ingredient', idx, 'amount')"
              :style="rowLayout.amount"
              @update:focused="validation.leaveOnBlur($event, 'amount', ing)"
            />
            <MeasurementUnitSelect
              :model-value="ing.unit"
              :amount="ing.amount"
              :requires-unit="!!ing.unstorableUnit"
              :error-messages="validation.messagesFor('ingredient', idx, 'unit')"
              :style="rowLayout.unit"
              @update:model-value="setIngredientUnit(ing, $event)"
              @update:focused="validation.leaveOnBlur($event, 'unit', ing)"
            />
            <v-text-field
              v-model="ing.notes"
              label="Notes"
              density="compact"
              hide-details="auto"
              :error-messages="validation.messagesFor('ingredient', idx, 'notes')"
              placeholder="e.g. diced"
              :class="rowLayout.notesClass"
              @update:focused="validation.leaveOnBlur($event, 'notes', ing)"
            />
          </div>

          <div
            v-if="ing.stepAmounts.length"
            class="d-flex align-center ga-1 text-caption text-medium-emphasis pl-3 pl-sm-6 mt-1"
            data-test="step-amounts"
          >
            <v-icon icon="mdi-format-list-numbered" size="x-small" />
            <span>{{ stepAmountsHint(ing) }}</span>
          </div>

          <!-- Category selector for new ingredients -->
          <div v-if="ing.isNew" class="pl-3 pl-sm-6 mt-2">
            <v-select
              v-model="ing.category"
              :items="categories"
              label="Category"
              density="compact"
              hide-details
            />
          </div>
        </div>

        <div v-if="!form.ingredients.length" class="text-caption text-medium-emphasis text-center py-2">
          Add at least one ingredient
        </div>
      </v-card>

      <!-- ── Steps ───────────────────────────────────────────────── -->
      <v-card class="mb-6 pa-3 pa-sm-4">
        <div class="d-flex align-center justify-space-between mb-4">
          <div class="text-subtitle-2 text-medium-emphasis">Steps</div>
          <v-btn
            size="small"
            variant="tonal"
            color="primary"
            prepend-icon="mdi-plus"
            @click="addStep"
          >
            Add step
          </v-btn>
        </div>

        <div
          v-for="(step, sIdx) in form.steps"
          :key="rowKey(step)"
          class="mb-6"
        >
          <div class="d-flex align-start ga-2">
            <v-avatar color="primary" size="28" class="text-white text-caption font-weight-bold mt-4">
              {{ sIdx + 1 }}
            </v-avatar>

            <div class="flex-grow-1">
              <v-textarea
                v-model="step.instruction"
                :label="`Step ${sIdx + 1} instruction`"
                rows="2"
                auto-grow
                density="compact"
                hide-details="auto"
                :error-messages="validation.messagesFor('step', sIdx, 'instruction')"
                class="mb-2"
                @update:focused="validation.leaveOnBlur($event, 'instruction', step)"
              />

              <div class="text-caption text-medium-emphasis mb-1">Ingredients used in this step:</div>
              <!-- Every chip stays enabled, because a pending New ingredient is selectable before it exists. -->
              <div class="d-flex flex-wrap ga-1" data-test="step-ingredients">
                <v-chip
                  v-for="(ing, iIdx) in form.ingredients"
                  :key="iIdx"
                  :variant="step.ingredientIndexes.includes(iIdx) ? 'flat' : 'outlined'"
                  :color="step.ingredientIndexes.includes(iIdx) ? 'secondary' : 'default'"
                  size="x-small"
                  @click="toggleStepIngredient(sIdx, iIdx)"
                >
                  {{ ing.displayName || `Ingredient ${iIdx + 1}` }}
                </v-chip>
                <span v-if="!form.ingredients.length" class="text-caption text-medium-emphasis">
                  Add ingredients above first
                </span>
              </div>
            </div>

            <v-btn
              icon="mdi-close"
              size="small"
              variant="text"
              color="error"
              :aria-label="`Remove step ${sIdx + 1}`"
              @click="removeStep(sIdx)"
            />
          </div>
          <v-divider v-if="sIdx < form.steps.length - 1" class="mt-4" />
        </div>

        <div v-if="!form.steps.length" class="text-caption text-medium-emphasis text-center py-2">
          No steps yet — add your first step above
        </div>
      </v-card>

      <!-- ── Error ───────────────────────────────────────────────── -->
      <v-alert v-if="submitError" type="error" class="mb-6" closable @click:close="submitError = null">
        {{ submitError }}
      </v-alert>

      <!-- ── Actions ─────────────────────────────────────────────── -->
      <div class="d-flex ga-4">
        <v-btn variant="outlined" :to="{ name: 'recipes' }">Cancel</v-btn>
        <v-btn
          color="primary"
          class="flex-grow-1"
          :disabled="store.confirmLoading"
          :loading="store.confirmLoading"
          @click="saveRecipe"
        >
          Save Recipe
        </v-btn>
      </div>

    </template>
  </v-container>
</template>

<script setup>
import { computed, reactive, ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useRecipeStore } from '@/stores/recipes'
import { useIngredientStore } from '@/stores/ingredients'
import {
  DEFAULT_UNIT, formatMeasurement, isPickableUnit, setIngredientUnit, toFormMeasurement, toPayloadMeasurement,
} from '@/constants/units'
import { MIN_SERVINGS, MAX_SERVINGS } from '@/constants/recipeLimits'
import MeasurementUnitSelect from '@/components/MeasurementUnitSelect.vue'
import RecipeProblemsBanner from '@/components/RecipeProblemsBanner.vue'
import { DEFAULT_CATEGORY, INGREDIENT_CATEGORIES } from '@/constants/categories'
import { useIngredientRowLayout } from '@/composables/useIngredientRowLayout'
import { useRecipeValidation } from '@/composables/useRecipeValidation'
import { useRemovalUndo } from '@/composables/useRemovalUndo'
import { rowKey } from '@/utils/recipeRows'

/** The search box's value for a row holding a pending New ingredient, which has no catalogue id until save. */
const PENDING_NEW = '__pending-new__'

/** The search box's "Add … as new" entry, which turns the typed name into a pending New ingredient. */
const ADD_NEW = '__add-new__'

const router          = useRouter()
const store           = useRecipeStore()
const ingredientStore = useIngredientStore()

const categories = INGREDIENT_CATEGORIES.map(({ title, value }) => ({ title, value }))

const rowLayout = useIngredientRowLayout({ amountWidth: '110px' })

const submitError  = ref(null)
const containerRef = ref(null)

const form = reactive({
  name:        '',
  description: '',
  servings:    4,
  sourceUrl:   '',
  ingredients: [],
  steps:       [],
})

const validation = useRecipeValidation(form)
const { removeIngredient, removeStep } = useRemovalUndo(form)

// What each row's search box holds as typed, keyed by row so a removed row takes its text with it.
const searches = reactive(new WeakMap())

const catalogueIds = computed(() => new Set(ingredientStore.catalogue.map(i => i.id)))

const sameName = (a, b) => a.trim().toLocaleLowerCase() === (b ?? '').trim().toLocaleLowerCase()

onMounted(() => {
  // The catalogue is searched in the browser, so the scraped matches show by name from the start.
  ingredientStore.fetchCatalogue()

  const preview = store.scrapePreview
  if (!preview) return

  form.name        = preview.name
  form.description = preview.description ?? ''
  form.servings    = preview.servings
  form.sourceUrl   = preview.sourceUrl

  form.ingredients = preview.ingredients.map(ing => {
    const { amount, unit, entryUnit } = toFormMeasurement(ing)
    return {
      ingredientId:   ing.ingredientId,
      name:           ing.name,
      displayName:    ing.displayName,
      amount,
      unit:           isPickableUnit(unit) ? unit : null,
      // A scraped unit the picker doesn't offer, such as "can", shows unselected and has to be picked before the recipe can be saved.
      unstorableUnit: unit && !isPickableUnit(unit) ? unit : null,
      entryUnit,
      sourceAmount:   ing.sourceAmount ?? null,
      sourceUnit:     ing.sourceUnit ?? null,
      notes:          ing.notes ?? '',
      isNew:          ing.isNew,
      category:       ing.suggestedCategory,
      // Shown on the preview only and never sent, so a confirmed recipe looks the same as one whose list stated the amounts.
      amountFromSteps: ing.amountFromSteps ?? false,
      stepAmounts:     ing.stepAmounts ?? [],
    }
  })

  form.steps = preview.steps.map(s => ({
    instruction:       s.instruction,
    ingredientIndexes: [...s.ingredientIndexes],
  }))

  validation.markArrived()
})

function addIngredient() {
  // Hand-entered rows have no Source Measurement, because what is typed here is the measurement itself.
  const row = {
    ingredientId:   null,
    name:           '',
    displayName:    '',
    amount:         1,
    unit:           DEFAULT_UNIT,
    unstorableUnit: null,
    entryUnit:      null,
    sourceAmount:   null,
    sourceUnit:     null,
    notes:          '',
    isNew:          false,
    category:       null,
    amountFromSteps: false,
    stepAmounts:     [],
  }
  form.ingredients.push(row)
  validation.markFresh(row)
}

/** The amounts a row's steps state, as the hint under it lists them, where a Sizeless Unit stated with no amount shows its word. */
function stepAmountsHint(ing) {
  const amounts = ing.stepAmounts
    .map(({ amount, unit }) => formatMeasurement(amount, unit) ?? unit)
    .join(' + ')
  return ing.amountFromSteps ? `From steps: ${amounts}` : `Steps say ${amounts}. Enter the total.`
}

function addStep() {
  const step = { instruction: '', ingredientIndexes: [] }
  form.steps.push(step)
  validation.markFresh(step)
}

function pickedValueOf(ing) {
  if (ing.isNew) return PENDING_NEW
  return ing.ingredientId
}

/** A row's search box entries: the catalogue, led by the row's own ingredient when the catalogue lacks it, and ending with "Add … as new" for a typed name the catalogue does not have. */
function itemsFor(ing) {
  const own = ing.isNew || (ing.ingredientId && !catalogueIds.value.has(ing.ingredientId))
    ? [{ id: pickedValueOf(ing), displayName: ing.displayName }]
    : []
  const typed = (searches.get(ing) ?? '').trim()
  const known = !typed || sameName(typed, ing.displayName)
    || ingredientStore.catalogue.some(i => sameName(typed, i.displayName) || sameName(typed, i.name))
  const addNew = known ? [] : [{ id: ADD_NEW, displayName: `Add "${typed}" as new`, typed }]
  return [...own, ...ingredientStore.catalogue, ...addNew]
}

/** Vuetify's default title match, except that "Add … as new" always shows, since it names whatever was typed. */
function matchTitleOrAddNew(value, query, item) {
  if (item?.raw?.id === ADD_NEW) return true
  return String(value).toLocaleLowerCase().indexOf(query.toLocaleLowerCase())
}

function onIngredientPicked(ing, value) {
  if (value === PENDING_NEW) return
  if (value === ADD_NEW) {
    const typed = searches.get(ing).trim()
    Object.assign(ing, {
      ingredientId: null,
      isNew:        true,
      name:         typed.toLocaleLowerCase(),
      displayName:  typed,
      // A scraped row keeps the category the scrape guessed for it.
      category:     ing.category ?? DEFAULT_CATEGORY,
    })
    return
  }
  const found = ingredientStore.catalogue.find(i => i.id === value)
  Object.assign(ing, {
    ingredientId: found?.id ?? null,
    isNew:        false,
    name:         found?.name ?? '',
    displayName:  found?.displayName ?? '',
  })
}

/** The chip on an ingredient's name line while one of its fields shows a problem. */
function rowChip(idx) {
  const fields = validation.shownFieldsOf('ingredient', idx)
  if (!fields.length) return null
  return fields.includes('unit') ? 'Needs unit' : 'Needs attention'
}

function toggleStepIngredient(stepIdx, ingIdx) {
  const step = form.steps[stepIdx]
  const pos  = step.ingredientIndexes.indexOf(ingIdx)
  if (pos === -1) step.ingredientIndexes.push(ingIdx)
  else            step.ingredientIndexes.splice(pos, 1)
}

async function saveRecipe() {
  submitError.value = null
  if (!await validation.attemptSave(containerRef.value?.$el)) return

  const payload = {
    name:        form.name.trim(),
    description: form.description.trim() || null,
    sourceUrl:   form.sourceUrl,
    servings:    form.servings,
    ingredients: form.ingredients.map((ing, i) => ({
      ingredientId:           ing.isNew ? null : ing.ingredientId,
      newIngredientName:      ing.isNew ? ing.name : null,
      newIngredientDisplayName: ing.isNew ? ing.displayName : null,
      category:               ing.isNew ? ing.category : null,
      // An empty amount round-trips as null, never as 0.
      ...toPayloadMeasurement(ing.amount, ing.unit),
      sourceAmount:           ing.sourceAmount ?? null,
      sourceUnit:             ing.sourceUnit ?? null,
      notes:                  ing.notes?.trim() || null,
      displayOrder:           i,
    })),
    steps: form.steps.map((s, i) => ({
      stepNumber:        i + 1,
      instruction:       s.instruction.trim(),
      ingredientIndexes: s.ingredientIndexes,
    })),
  }

  try {
    const newId = await store.confirmScrape(payload)
    store.clearScrapePreview()
    router.push({ name: 'recipe-detail', params: { id: newId } })
  } catch (e) {
    submitError.value = e.message ?? 'Something went wrong. Please try again.'
  }
}
</script>
