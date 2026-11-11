<template>
  <div v-if="store.loading" class="pa-4">
    <v-skeleton-loader type="image, article" />
  </div>

  <!-- ── Fetch error (network/5xx) — distinct from "not found" below ───── -->
  <ErrorState v-else-if="store.error" :message="store.error" @retry="reload" />

  <div v-else-if="recipe">

    <!-- ── Hero image ───────────────────────────────────────────────────── -->
    <v-img
      :src="recipe.imageUrl || ''"
      :alt="recipe.name"
      :class="{ grayscale: store.isRecentlyCooked(recipe) }"
      height="240"
      cover
    >
      <template #placeholder>
        <div class="d-flex align-center justify-center fill-height bg-grey-lighten-3">
          <v-icon size="64" color="grey-lighten-2">mdi-food</v-icon>
        </div>
      </template>

      <!-- Edit button overlay -->
      <div class="position-absolute" style="top: 8px; right: 8px">
        <v-btn
          icon="mdi-pencil"
          size="small"
          color="white"
          variant="tonal"
          aria-label="Edit recipe"
          :to="{ name: 'recipe-edit', params: { id: recipe.id } }"
        />
      </div>
    </v-img>

    <v-container class="pa-4" max-width="600">

      <!-- ── Title + meta ─────────────────────────────────────────────── -->
      <div class="d-flex align-start justify-space-between ga-2 mb-2">
        <div class="flex-grow-1">
          <h1 class="text-h5 font-weight-bold mb-2">{{ recipe.name }}</h1>
          <div v-if="recipe.description" class="text-body-2 text-medium-emphasis mt-1">
            {{ recipe.description }}
          </div>
        </div>
      </div>

      <!-- Chips row -->
      <div class="d-flex ga-2 flex-wrap mb-6">
        <v-chip size="small" variant="tonal" color="primary" class="servings-chip">
          <v-icon start>mdi-account-group</v-icon>
          {{ formatServings(servings) }}
        </v-chip>
        <v-chip size="small" variant="tonal" color="secondary">
          <v-icon start>mdi-format-list-bulleted</v-icon>
          {{ recipe.ingredients.length }} ingredients
        </v-chip>
        <v-chip size="small" variant="tonal" color="info">
          <v-icon start>mdi-list-box-outline</v-icon>
          {{ recipe.steps.length }} steps
        </v-chip>
        <v-chip
          v-if="recipe.sourceUrl"
          size="small"
          variant="tonal"
          :href="recipe.sourceUrl"
          target="_blank"
        >
          <v-icon start>mdi-link</v-icon>
          Source
        </v-chip>
      </div>

      <!-- ── Start cooking CTA ─────────────────────────────────────────── -->
      <v-btn
        block
        color="primary"
        size="large"
        prepend-icon="mdi-chef-hat"
        class="mb-6"
        :to="cookingRoute(recipe.id, servings)"
      >
        Start cooking
      </v-btn>

      <!-- ── Servings selector ─────────────────────────────────────────── -->
      <v-card class="mb-12 pa-3" variant="outlined">
        <div class="d-flex align-center flex-wrap ga-4">
          <ServingsStepper :model-value="servings" @update:model-value="chooseServings" />
          <BatchSizeToggle
            v-model="matchingBatchSize"
            :mandatory="false"
            button-size="small"
            density="compact"
          />
        </div>
      </v-card>

      <!-- ── Ingredients ───────────────────────────────────────────────── -->
      <h2 class="text-subtitle-1 font-weight-bold mb-2">Ingredients</h2>
      <v-card class="mb-12">
        <v-list density="compact">
          <template v-for="(ing, idx) in recipe.ingredients" :key="ing.id">
            <v-list-item
              :id="`ingredient-${ing.id}`"
              :class="{ 'bg-primary-lighten-5': highlightedIngredientId === ing.id }"
              style="transition: background-color 0.3s"
            >
              <template #prepend>
                <v-icon size="8" color="grey" class="mr-2">mdi-circle</v-icon>
              </template>
              <v-list-item-title>
                <!-- Absent for an ingredient whose source stated no quantity -->
                <span v-if="formatAmount(ing)" class="font-weight-medium">
                  {{ formatAmount(ing) }}
                </span>
                <span
                  v-if="formatSource(ing)"
                  class="text-caption text-medium-emphasis mr-1"
                >{{ formatSource(ing) }}</span>
                {{ ing.ingredientDisplayName }}
              </v-list-item-title>
              <v-list-item-subtitle v-if="ing.notes">{{ ing.notes }}</v-list-item-subtitle>
            </v-list-item>
            <v-divider v-if="idx < recipe.ingredients.length - 1" />
          </template>
        </v-list>
      </v-card>

      <!-- ── Steps ────────────────────────────────────────────────────── -->
      <h2 class="text-subtitle-1 font-weight-bold mb-2">Instructions</h2>
      <div v-for="step in recipe.steps" :key="step.id" class="mb-4">
        <v-card>
          <v-card-item>
            <template #prepend>
              <v-avatar color="primary" size="32" class="text-white font-weight-bold">
                {{ step.stepNumber }}
              </v-avatar>
            </template>
            <v-card-text class="pa-0 text-body-1">
              {{ step.instruction }}
            </v-card-text>
          </v-card-item>

          <!-- Ingredient chips for this step -->
          <div
            v-if="stepIngredients(step).length"
            class="step-ingredients px-6 pb-4 d-flex flex-column align-center ga-2"
          >
            <StepIngredientChip
              v-for="ing in stepIngredients(step)"
              :key="ing.id"
              :ingredient="ing"
              :multiplier="servingsMultiplier"
              size="small"
              color="secondary"
              variant="tonal"
              @click="highlightIngredient(ing.id)"
            />
          </div>
        </v-card>
      </div>

      <!-- ── Mark as cooked ────────────────────────────────────────────── -->
      <v-btn
        block
        color="success"
        size="large"
        prepend-icon="mdi-chef-hat"
        class="mt-6"
        :loading="markingCooked"
        @click="markCooked"
      >
        Mark as cooked
      </v-btn>

    </v-container>
  </div>

  <!-- ── Not found (404 — distinct from the fetch error above) ──────────── -->
  <EmptyState
    v-else
    icon="mdi-help-circle-outline"
    title="Recipe not found"
    action-label="Back to recipes"
    :action-to="{ name: 'recipes' }"
  />
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useRecipeStore } from '@/stores/recipes'
import { formatIngredientMeasurement } from '@/constants/units'
import { BATCH_SIZES, batchSizeMultiplier } from '@/constants/batchSizes'
import { formatServings } from '@/constants/servings'
import { cookingRoute, recipeRoute, servingsFromQuery } from '@/router/recipeRoutes'
import { useUiStore } from '@/stores/ui'
import BatchSizeToggle from '@/components/BatchSizeToggle.vue'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'
import ServingsStepper from '@/components/ServingsStepper.vue'
import StepIngredientChip from '@/components/StepIngredientChip.vue'

const props = defineProps({ id: { type: String, required: true } })
const route  = useRoute()
const router = useRouter()
const store  = useRecipeStore()
const ui     = useUiStore()

const highlightedIngredientId = ref(null)
const markingCooked           = ref(false)

const recipe = computed(() => store.currentRecipe)

// Carried in the query so a Planned Meal opens at its Servings and a reload or a return from cooking keeps the chosen servings.
const servings = computed(() => servingsFromQuery(route.query, recipe.value.servings))

function chooseServings(chosen) {
  router.replace(recipeRoute(recipe.value.id, chosen))
}

const servingsMultiplier = computed(() => servings.value / recipe.value.servings)

/** The Batch Size shortcut whose multiplier the chosen servings match exactly, or null when none does. */
const matchingBatchSize = computed({
  get: () => BATCH_SIZES.find(batchSize => batchSize.multiplier === servingsMultiplier.value)?.value ?? null,
  set: batchSize => {
    // Tapping the highlighted shortcut again clears the toggle, which leaves the chosen servings as they are.
    if (batchSize) chooseServings(recipe.value.servings * batchSizeMultiplier(batchSize))
  },
})

function reload() {
  store.fetchRecipe(props.id ?? route.params.id)
}

onMounted(reload)

/** The row's stored amount at the chosen servings, or null when it states no quantity or its source says the same count. */
function formatAmount(ing) {
  return formatIngredientMeasurement(ing, servingsMultiplier.value).amount
}

/** The measurement as the source recipe stated it, or null when the row was hand-entered or nothing was converted. */
function formatSource(ing) {
  return formatIngredientMeasurement(ing, servingsMultiplier.value).source
}

function stepIngredients(step) {
  if (!recipe.value) return []
  return step.recipeIngredientIds
    .map(riId => recipe.value.ingredients.find(i => i.id === riId))
    .filter(Boolean)
}

function highlightIngredient(recipeIngredientId) {
  highlightedIngredientId.value = recipeIngredientId
  const el = document.getElementById(`ingredient-${recipeIngredientId}`)
  el?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  setTimeout(() => { highlightedIngredientId.value = null }, 2000)
}

async function markCooked() {
  markingCooked.value = true
  try {
    await store.markCooked(recipe.value.id)
    ui.notify({ message: `Marked “${recipe.value.name}” as cooked`, color: 'success' })
  } catch (e) {
    ui.notify({ message: e?.message ?? 'Could not mark as cooked.', color: 'error' })
  } finally {
    markingCooked.value = false
  }
}
</script>

<style scoped>
.grayscale { filter: grayscale(100%); }
.bg-primary-lighten-5 { background-color: rgba(46, 125, 50, 0.08) !important; }
</style>
