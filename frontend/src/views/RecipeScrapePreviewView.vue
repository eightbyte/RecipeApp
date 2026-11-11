<template>
  <v-container class="pa-2 pa-sm-4" max-width="600">

    <!-- Loading bar during save -->
    <v-progress-linear v-if="store.confirmLoading" indeterminate color="primary" class="mb-2" />

    <h1 class="text-h6 font-weight-bold mb-6">Review Imported Recipe</h1>

    <!-- No preview guard -->
    <v-alert v-if="!store.scrapePreview" type="warning" class="mb-6">
      No preview available. Please <router-link :to="{ name: 'recipes' }">go back</router-link> and import a recipe.
    </v-alert>

    <template v-else>

      <!-- ── Basic info ──────────────────────────────────────────── -->
      <v-card class="mb-6 pa-3 pa-sm-4">
        <div class="text-subtitle-2 text-medium-emphasis mb-4">Basic Info</div>

        <v-text-field
          v-model="form.name"
          label="Recipe name"
          :rules="[v => !!v || 'Name is required']"
          required
          class="mb-2"
        />

        <v-textarea
          v-model="form.description"
          label="Description (optional)"
          rows="2"
          auto-grow
          class="mb-2"
        />

        <div class="d-flex ga-4">
          <v-text-field
            v-model.number="form.servings"
            label="Servings"
            type="number"
            min="1"
            max="100"
            :rules="[v => v > 0 || 'Must be at least 1']"
            style="max-width: 120px"
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
        <div class="text-subtitle-2 text-medium-emphasis mb-4">Ingredients</div>

        <div
          v-for="(ing, idx) in form.ingredients"
          :key="idx"
          class="mb-6"
        >
          <div class="d-flex align-center ga-2 mb-1">
            <span class="text-caption text-medium-emphasis" style="min-width: 20px">{{ idx + 1 }}.</span>
            <span class="text-body-2 font-weight-medium">{{ ing.displayName }}</span>
            <v-chip v-if="ing.isNew" size="x-small" color="info" variant="flat">New</v-chip>
          </div>

          <div class="d-flex flex-wrap flex-sm-nowrap ga-2 pl-3 pl-sm-6">
            <v-text-field
              v-model.number="ing.amount"
              label="Amount"
              type="number"
              min="0"
              step="0.001"
              density="compact"
              hide-details
              :style="rowLayout.amount"
            />
            <MeasurementUnitSelect
              :model-value="ing.unit"
              :amount="ing.amount"
              :requires-unit="!!ing.unstorableUnit"
              :style="rowLayout.unit"
              @update:model-value="setIngredientUnit(ing, $event)"
            />
            <v-text-field
              v-model="ing.notes"
              label="Notes"
              density="compact"
              hide-details
              placeholder="e.g. diced"
              :class="rowLayout.notesClass"
            />
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
      </v-card>

      <!-- ── Steps ───────────────────────────────────────────────── -->
      <v-card class="mb-6 pa-3 pa-sm-4">
        <div class="text-subtitle-2 text-medium-emphasis mb-4">Steps</div>

        <div
          v-for="(step, sIdx) in form.steps"
          :key="sIdx"
          class="mb-6"
        >
          <div class="d-flex align-start ga-2">
            <v-avatar color="primary" size="28" class="text-white text-caption font-weight-bold mt-4">
              {{ step.stepNumber }}
            </v-avatar>

            <div class="flex-grow-1">
              <v-textarea
                v-model="step.instruction"
                :label="`Step ${step.stepNumber} instruction`"
                rows="2"
                auto-grow
                density="compact"
                hide-details
                class="mb-2"
              />

              <div class="text-caption text-medium-emphasis mb-1">Ingredients used in this step:</div>
              <div class="d-flex flex-wrap ga-1">
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
              </div>
            </div>
          </div>
          <v-divider v-if="sIdx < form.steps.length - 1" class="mt-4" />
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
import { reactive, ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useRecipeStore } from '@/stores/recipes'
import {
  hasAmount, isPickableUnit, setIngredientUnit, toFormMeasurement, toPayloadMeasurement,
} from '@/constants/units'
import MeasurementUnitSelect from '@/components/MeasurementUnitSelect.vue'
import { INGREDIENT_CATEGORIES } from '@/constants/categories'
import { useIngredientRowLayout } from '@/composables/useIngredientRowLayout'

const router = useRouter()
const store  = useRecipeStore()

const categories = INGREDIENT_CATEGORIES.map(({ title, value }) => ({ title, value }))

const rowLayout = useIngredientRowLayout({ amountWidth: '110px' })

const submitError = ref(null)

const form = reactive({
  name:        '',
  description: '',
  servings:    4,
  sourceUrl:   '',
  ingredients: [],
  steps:       [],
})

onMounted(() => {
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
      displayOrder:   ing.displayOrder,
    }
  })

  form.steps = preview.steps.map(s => ({
    stepNumber:        s.stepNumber,
    instruction:       s.instruction,
    ingredientIndexes: [...s.ingredientIndexes],
  }))
})

/** True while a row states an amount, has no unit, and arrived with one the app cannot store, such as "can". */
function mustPickUnit(ing) {
  return hasAmount(ing.amount) && !ing.unit && !!ing.unstorableUnit
}

function toggleStepIngredient(stepIdx, ingIdx) {
  const step = form.steps[stepIdx]
  const pos  = step.ingredientIndexes.indexOf(ingIdx)
  if (pos === -1) step.ingredientIndexes.push(ingIdx)
  else            step.ingredientIndexes.splice(pos, 1)
}

async function saveRecipe() {
  submitError.value = null

  if (!form.name.trim()) {
    submitError.value = 'Recipe name is required.'
    return
  }
  if (!form.ingredients.length) {
    submitError.value = 'At least one ingredient is required.'
    return
  }
  // An amount with no unit saves as a count, except where the source's unit has a size pcs would lose.
  const unitToPick = form.ingredients.find(ing => mustPickUnit(ing))
  if (unitToPick) {
    submitError.value = `Pick a unit for "${unitToPick.displayName}", or clear its amount. ` +
      `The source said "${unitToPick.unstorableUnit}", which this app can't store.`
    return
  }

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
    steps: form.steps.map(s => ({
      stepNumber:        s.stepNumber,
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
