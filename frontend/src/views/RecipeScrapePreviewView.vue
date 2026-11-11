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
            <v-chip v-if="rowChip(idx)" size="x-small" color="error" variant="flat">{{ rowChip(idx) }}</v-chip>
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
                hide-details="auto"
                :error-messages="validation.messagesFor('step', sIdx, 'instruction')"
                class="mb-2"
                @update:focused="validation.leaveOnBlur($event, 'instruction', step)"
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
  isPickableUnit, setIngredientUnit, toFormMeasurement, toPayloadMeasurement,
} from '@/constants/units'
import { MIN_SERVINGS, MAX_SERVINGS } from '@/constants/recipeLimits'
import MeasurementUnitSelect from '@/components/MeasurementUnitSelect.vue'
import RecipeProblemsBanner from '@/components/RecipeProblemsBanner.vue'
import { INGREDIENT_CATEGORIES } from '@/constants/categories'
import { useIngredientRowLayout } from '@/composables/useIngredientRowLayout'
import { useRecipeValidation } from '@/composables/useRecipeValidation'

const router = useRouter()
const store  = useRecipeStore()

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

  validation.markArrived()
})

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
