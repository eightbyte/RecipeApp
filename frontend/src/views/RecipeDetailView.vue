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
      <div class="d-flex align-start justify-space-between gap-2 mb-2">
        <div class="flex-grow-1">
          <h1 class="text-h5 font-weight-bold">{{ recipe.name }}</h1>
          <div v-if="recipe.description" class="text-body-2 text-medium-emphasis mt-1">
            {{ recipe.description }}
          </div>
        </div>
      </div>

      <!-- Chips row -->
      <div class="d-flex gap-2 flex-wrap mb-4">
        <v-chip size="small" variant="tonal" color="primary">
          <v-icon start>mdi-account-group</v-icon>
          {{ scaledServings }} servings
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
        class="mb-4"
        :to="{ name: 'recipe-cooking', params: { id: recipe.id }, query: { portion: portionSize } }"
      >
        Start cooking
      </v-btn>

      <!-- ── Portion selector ──────────────────────────────────────────── -->
      <v-card class="mb-5 pa-3" variant="outlined">
        <div class="d-flex align-center gap-3">
          <span class="text-body-2 font-weight-medium">Portion size</span>
          <v-btn-toggle v-model="portionSize" mandatory density="compact" color="primary">
            <v-btn value="HALF"    size="small">½</v-btn>
            <v-btn value="REGULAR" size="small">Regular</v-btn>
            <v-btn value="DOUBLE"  size="small">Double</v-btn>
          </v-btn-toggle>
        </div>
      </v-card>

      <!-- ── Ingredients ───────────────────────────────────────────────── -->
      <h2 class="text-subtitle-1 font-weight-bold mb-2">Ingredients</h2>
      <v-card class="mb-5">
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
                <!-- Absent for an ingredient whose source stated no quantity (Phase 9.1 §3.4) -->
                <span v-if="formatAmount(ing.amount, ing.unit)" class="font-weight-medium">
                  {{ formatAmount(ing.amount, ing.unit) }}
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
      <div v-for="step in recipe.steps" :key="step.id" class="mb-3">
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
            class="px-4 pb-3 d-flex flex-wrap gap-2"
          >
            <v-chip
              v-for="ing in stepIngredients(step)"
              :key="ing.id"
              size="x-small"
              color="secondary"
              variant="tonal"
              @click="highlightIngredient(ing.id)"
            >
              {{ ing.ingredientDisplayName }}
            </v-chip>
          </div>
        </v-card>
      </div>

      <!-- ── Mark as cooked ────────────────────────────────────────────── -->
      <v-btn
        block
        color="success"
        size="large"
        prepend-icon="mdi-chef-hat"
        class="mt-4"
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
import { formatMeasurement, formatSourceMeasurement } from '@/constants/units'
import { useUiStore } from '@/stores/ui'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'

const props = defineProps({ id: String })
const route  = useRoute()
const router = useRouter()
const store  = useRecipeStore()
const ui     = useUiStore()

const portionSize            = ref('REGULAR')
const highlightedIngredientId = ref(null)
const markingCooked          = ref(false)

const recipe = computed(() => store.currentRecipe)

const portionMultiplier = computed(() =>
  ({ HALF: 0.5, REGULAR: 1, DOUBLE: 2 })[portionSize.value] ?? 1
)

const scaledServings = computed(() =>
  Math.round(recipe.value?.servings * portionMultiplier.value) || 0
)

function reload() {
  store.fetchRecipe(props.id ?? route.params.id)
}

onMounted(reload)

/**
 * Scaling has to short-circuit on a null amount, not delegate it: `null * 2` is `0` in
 * JavaScript, which would turn an ingredient with no stated quantity into a rendered `0`
 * (Phase 9.1 §3.4). Half of nothing is still nothing.
 */
function formatAmount(baseAmount, unit) {
  if (baseAmount == null) return null
  return formatMeasurement(baseAmount * portionMultiplier.value, unit)
}

/**
 * The measurement as the source recipe stated it, shown alongside the canonical amount
 * so the recipe reads as written while the shopping list still sums grams. Null when the
 * row was hand-entered or nothing was converted.
 */
function formatSource(ing) {
  return formatSourceMeasurement(
    ing.sourceAmount, ing.sourceUnit, ing.unit, portionMultiplier.value)
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
