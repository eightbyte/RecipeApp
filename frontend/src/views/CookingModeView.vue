<template>
  <div class="cooking-mode">

    <!-- ── Loading ──────────────────────────────────────────────────────── -->
    <div v-if="store.loading" class="cooking-state">
      <v-btn
        class="cooking-close"
        icon="mdi-close"
        variant="text"
        aria-label="Exit cooking mode"
        @click="exit"
      />
      <div class="pa-6" style="width: 100%; max-width: 600px">
        <v-skeleton-loader type="article, list-item-three-line@3" />
      </div>
    </div>

    <!-- ── Fetch error ──────────────────────────────────────────────────── -->
    <div v-else-if="store.error" class="cooking-state">
      <v-btn
        class="cooking-close"
        icon="mdi-close"
        variant="text"
        aria-label="Exit cooking mode"
        @click="exit"
      />
      <ErrorState :message="store.error" @retry="load" />
    </div>

    <!-- ── Recipe not found ─────────────────────────────────────────────── -->
    <div v-else-if="!recipe" class="cooking-state">
      <v-btn
        class="cooking-close"
        icon="mdi-close"
        variant="text"
        aria-label="Exit cooking mode"
        @click="exit"
      />
      <EmptyState
        icon="mdi-help-circle-outline"
        title="Recipe not found"
        text="This recipe could not be loaded."
        action-label="Back"
        @action="exit"
      />
    </div>

    <!-- ── Cooking ──────────────────────────────────────────────────────── -->
    <template v-else>
      <!-- Sticky header -->
      <header ref="headerEl" tabindex="-1" class="cooking-header safe-area-top">
        <div class="d-flex align-center gap-2 px-2 py-2">
          <v-btn
            icon="mdi-close"
            variant="text"
            aria-label="Exit cooking mode"
            @click="exit"
          />
          <div class="flex-grow-1 min-width-0">
            <h1 class="text-subtitle-1 font-weight-bold text-truncate mb-0">{{ recipe.name }}</h1>
            <div class="text-caption text-medium-emphasis" role="status" aria-live="polite">
              Step {{ displayIndex }} / {{ totalSteps }}
            </div>
          </div>
        </div>
        <v-progress-linear
          :model-value="completionPercent"
          color="primary"
          height="4"
          aria-hidden="true"
        />
      </header>

      <!-- Scrollable step list -->
      <div class="cooking-steps px-4 py-4">
        <div
          v-for="(step, index) in recipe.steps"
          :id="`cooking-step-${step.id}`"
          :key="step.id"
          class="cooking-step pa-4 mb-4"
          :class="{
            'cooking-step--current': isCurrent(index),
            'cooking-step--done': isDone(step),
          }"
          @click="toggleStep(step)"
        >
          <div class="d-flex align-start gap-3">
            <v-checkbox-btn
              :model-value="isDone(step)"
              color="primary"
              :aria-label="`Mark step ${step.stepNumber} as done`"
              @click.stop="toggleStep(step)"
            />
            <div class="flex-grow-1 min-width-0">
              <div class="text-overline text-medium-emphasis">Step {{ step.stepNumber }}</div>
              <div
                class="font-weight-medium cooking-instruction"
                :class="isCurrent(index) ? 'text-h5' : 'text-h6'"
              >
                {{ step.instruction }}
              </div>

              <!-- Per-step ingredient chips; the current step's are highlighted -->
              <div v-if="stepIngredients(step).length" class="d-flex flex-wrap gap-2 mt-3">
                <v-chip
                  v-for="ing in stepIngredients(step)"
                  :key="ing.id"
                  size="small"
                  :color="isCurrent(index) ? 'primary' : undefined"
                  :variant="isCurrent(index) ? 'flat' : 'tonal'"
                >
                  <span class="font-weight-medium mr-1">{{ formatAmount(ing.amount, ing.unit) }}</span>
                  <span v-if="formatSource(ing)" class="text-caption mr-1">{{ formatSource(ing) }}</span>
                  {{ ing.ingredientDisplayName }}
                </v-chip>
              </div>
            </div>
          </div>
        </div>

        <!-- Spacer so the floating Next button never overlaps the last step -->
        <div style="height: 72px"></div>
      </div>

      <!-- Floating "Next step" — marks current done and advances -->
      <v-btn
        v-if="currentIndex !== -1"
        class="cooking-next"
        color="primary"
        variant="elevated"
        prepend-icon="mdi-arrow-down"
        aria-label="Mark current step done and go to next step"
        @click="nextStep"
      >
        Next step
      </v-btn>

      <!-- Persistent finish footer -->
      <footer class="cooking-footer safe-area-bottom">
        <v-btn
          block
          size="large"
          color="success"
          prepend-icon="mdi-chef-hat"
          :loading="finishing"
          @click="finish"
        >
          Mark as cooked &amp; finish
        </v-btn>
      </footer>
    </template>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useRecipeStore } from '@/stores/recipes'
import { formatMeasurement, formatSourceMeasurement } from '@/constants/units'
import { useUiStore } from '@/stores/ui'
import { useWakeLock } from '@/composables/useWakeLock'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'

const props  = defineProps({ id: String })
const route  = useRoute()
const router = useRouter()
const store  = useRecipeStore()
const ui     = useUiStore()

// Keep the screen awake while cooking (no-op where unsupported).
useWakeLock()

const recipeId = props.id ?? route.params.id

const PORTION_MULTIPLIERS = { HALF: 0.5, REGULAR: 1, DOUBLE: 2 }

// Portion carried from a meal plan via ?portion=…; defaults to REGULAR.
const portionSize = computed(() => {
  const q = route.query.portion
  return q in PORTION_MULTIPLIERS ? q : 'REGULAR'
})
const portionMultiplier = computed(() => PORTION_MULTIPLIERS[portionSize.value] ?? 1)

const recipe   = computed(() => store.currentRecipe)
const headerEl = ref(null)
const finishing = ref(false)

// Ephemeral, component-local "done" state — not persisted (spec §3.4).
const doneStepIds = ref(new Set())

const totalSteps = computed(() => recipe.value?.steps?.length ?? 0)

// The current step is the first one not yet marked done (-1 when all done).
const currentIndex = computed(() => {
  const steps = recipe.value?.steps ?? []
  return steps.findIndex(s => !doneStepIds.value.has(s.id))
})

const displayIndex = computed(() =>
  currentIndex.value === -1 ? totalSteps.value : currentIndex.value + 1
)

const completionPercent = computed(() => {
  if (!totalSteps.value) return 0
  const done = (recipe.value?.steps ?? []).filter(s => doneStepIds.value.has(s.id)).length
  return (done / totalSteps.value) * 100
})

const isCurrent = (index) => currentIndex.value === index
const isDone    = (step)  => doneStepIds.value.has(step.id)

function stepIngredients(step) {
  if (!recipe.value) return []
  return step.recipeIngredientIds
    .map(riId => recipe.value.ingredients.find(i => i.id === riId))
    .filter(Boolean)
}

function formatAmount(baseAmount, unit) {
  return formatMeasurement(baseAmount * portionMultiplier.value, unit)
}

/** The measurement as the source recipe stated it; null when there is nothing to add. */
function formatSource(ing) {
  return formatSourceMeasurement(
    ing.sourceAmount, ing.sourceUnit, ing.unit, portionMultiplier.value)
}

function toggleStep(step) {
  const next = new Set(doneStepIds.value)
  if (next.has(step.id)) next.delete(step.id)
  else next.add(step.id)
  doneStepIds.value = next
}

function nextStep() {
  const idx = currentIndex.value
  if (idx === -1) return
  toggleStep(recipe.value.steps[idx])
  nextTick(() => {
    const newIdx = currentIndex.value
    if (newIdx !== -1) scrollToStep(recipe.value.steps[newIdx].id)
  })
}

function scrollToStep(stepId) {
  const el = document.getElementById(`cooking-step-${stepId}`)
  if (!el?.scrollIntoView) return
  const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches ?? false
  try {
    el.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'center' })
  } catch { /* jsdom has no layout — ignore */ }
}

async function load() {
  if (store.currentRecipe?.id !== recipeId) {
    await store.fetchRecipe(recipeId)
  }
  await nextTick()
  headerEl.value?.focus()
}

function exit() {
  router.back()
}

async function finish() {
  finishing.value = true
  try {
    await store.markCooked(recipeId)
    ui.notify({ message: `Marked “${recipe.value?.name}” as cooked`, color: 'success' })
    router.replace({ name: 'recipe-detail', params: { id: recipeId } })
  } catch (e) {
    ui.notify({ message: e?.message ?? 'Could not mark as cooked.', color: 'error' })
  } finally {
    finishing.value = false
  }
}

onMounted(load)
</script>

<style scoped>
/* Full-viewport overlay — owns the whole screen, independent of v-main padding.
   100dvh keeps the sticky footer clear of mobile browser chrome (spec §8). */
.cooking-mode {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: flex;
  flex-direction: column;
  height: 100dvh;
  background: rgb(var(--v-theme-background));
}

.cooking-header {
  flex: 0 0 auto;
  background: rgb(var(--v-theme-surface));
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.08);
}

.cooking-steps {
  flex: 1 1 auto;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
}

.cooking-footer {
  flex: 0 0 auto;
  padding: 12px 16px;
  background: rgb(var(--v-theme-surface));
  box-shadow: 0 -1px 4px rgba(0, 0, 0, 0.08);
}

/* States (loading / error / not-found) fill the overlay and centre content. */
.cooking-state {
  flex: 1 1 auto;
  display: flex;
  align-items: center;
  justify-content: center;
}
.cooking-close {
  position: absolute;
  top: max(8px, env(safe-area-inset-top, 0px));
  left: 8px;
}

.cooking-step {
  border: 1px solid rgba(0, 0, 0, 0.08);
  border-left: 4px solid transparent;
  border-radius: 12px;
  cursor: pointer;
  transition: opacity 0.2s ease, border-color 0.2s ease, background-color 0.2s ease;
}
.cooking-step--current {
  border-left-color: rgb(var(--v-theme-primary));
  background: rgba(46, 125, 50, 0.04);
}
.cooking-step--done {
  opacity: 0.5;
}
.cooking-step--done .cooking-instruction {
  text-decoration: line-through;
}

.cooking-instruction {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.cooking-next {
  position: absolute;
  right: 16px;
  bottom: calc(84px + env(safe-area-inset-bottom, 0px));
  z-index: 3;
}

.min-width-0 { min-width: 0; }

/* Reduced motion: drop step state transitions for users who request it. */
@media (prefers-reduced-motion: reduce) {
  .cooking-step {
    transition: none;
  }
}
</style>
