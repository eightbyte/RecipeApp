<template>
  <v-container class="pa-4" max-width="600">

    <div v-if="restoring" class="d-flex justify-center py-6">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <ErrorState
      v-else-if="restoreError"
      :message="restoreError"
      @retry="restorePlan(planInUrl)"
    />

    <!-- ── Step 1: Name entry ────────────────────────────────────────── -->
    <div v-else-if="!builtPlan">
      <h2 class="text-h6 font-weight-bold mb-6">New Meal Plan</h2>

      <v-alert
        v-if="mealPlanStore.activePlan && !creating"
        type="info"
        variant="tonal"
        density="compact"
        class="mb-6"
      >
        Creating a new plan closes your current active plan
        “{{ mealPlanStore.activePlan.name }}” and starts a fresh shopping list.
      </v-alert>

      <v-text-field
        v-model="planName"
        label="Plan name"
        variant="outlined"
        :error-messages="nameError"
        @keyup.enter="createPlan"
      />

      <v-btn
        block
        color="primary"
        size="large"
        class="mt-2"
        :loading="creating"
        :disabled="!planName.trim()"
        @click="createPlan"
      >
        Create plan
      </v-btn>
    </div>

    <!-- ── Step 2: Recipe builder ────────────────────────────────────── -->
    <div v-else>
      <div class="d-flex align-center justify-space-between mb-6">
        <div>
          <h2 class="text-h6 font-weight-bold mb-2">{{ builtPlan.name }}</h2>
          <div class="text-caption text-medium-emphasis">Active plan — add recipes below</div>
        </div>
        <v-btn color="primary" variant="tonal" @click="done">Done</v-btn>
      </div>

      <!-- Added recipes -->
      <div v-if="builtPlan.plannedMeals.length > 0" class="mb-6">
        <v-card
          v-for="meal in builtPlan.plannedMeals"
          :key="meal.id"
          class="mb-2"
          variant="outlined"
          @click="openRecipe(meal)"
        >
          <div class="pa-3">
            <div class="d-flex align-center ga-2 mb-2">
              <v-avatar size="36" rounded="lg" color="grey-lighten-3">
                <v-img v-if="meal.recipeImageUrl" :src="meal.recipeImageUrl" cover />
                <v-icon v-else color="grey" size="16">mdi-food</v-icon>
              </v-avatar>
              <div class="flex-grow-1 text-body-2 font-weight-medium">{{ meal.recipeName }}</div>
              <v-btn icon="mdi-delete" variant="text" size="x-small" color="error" @click.stop="removePlannedMeal(meal.id)" />
            </div>

            <div class="d-flex ga-2 flex-wrap" @click.stop>
              <v-text-field
                :model-value="meal.scheduledDate ?? ''"
                type="date"
                label="Date"
                variant="outlined"
                density="compact"
                hide-details
                clearable
                style="min-width: 160px; flex: 1"
                @update:model-value="(scheduledDate) => updatePlannedMeal(meal, { scheduledDate })"
              />
              <ServingsStepper
                :model-value="servingsOf(meal)"
                @update:model-value="(servings) => stepServings(meal, servings)"
              />
            </div>
          </div>
        </v-card>
      </div>

      <!-- Suggestions -->
      <div v-if="mealPlanStore.suggestions.length > 0" class="mb-6">
        <SuggestionsPanel
          :suggestions="mealPlanStore.suggestions"
          @add="addSuggestion"
        />
      </div>

      <!-- Recipe browser -->
      <v-divider class="mb-4" />
      <h3 class="text-subtitle-1 font-weight-bold mb-4">Browse recipes</h3>
      <RecipeBrowser @add="addRecipe" />
    </div>

  </v-container>
</template>

<script setup>
import { computed, ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMealPlanStore } from '@/stores/mealPlans'
import { recipeRoute } from '@/router/recipeRoutes'
import { DISPLAY_LOCALE } from '@/utils/dateOnly'
import ErrorState from '@/components/ErrorState.vue'
import RecipeBrowser from '@/components/RecipeBrowser.vue'
import ServingsStepper from '@/components/ServingsStepper.vue'
import SuggestionsPanel from '@/components/SuggestionsPanel.vue'

const PLAN_QUERY_KEY = 'plan'

const router        = useRouter()
const route         = useRoute()
const mealPlanStore = useMealPlanStore()

// App.vue keys each view by its full path, so the plan in this builder's URL never changes while it is mounted.
const planInUrl = route.query[PLAN_QUERY_KEY]
const builtPlan = computed(() =>
  planInUrl && mealPlanStore.currentPlan?.id === planInUrl ? mealPlanStore.currentPlan : null)

const planName     = ref(`Week of ${new Date().toLocaleDateString(DISPLAY_LOCALE, { day: 'numeric', month: 'long' })}`)
const nameError    = ref('')
const creating     = ref(false)
const restoring    = ref(false)
const restoreError = ref(null)

// Without a plan in the URL, start at the name step rather than at a plan left in the store from a previously viewed plan.
onMounted(() => {
  if (planInUrl) restorePlan(planInUrl)
  else mealPlanStore.currentPlan = null
})

async function restorePlan(planId) {
  restoring.value    = true
  restoreError.value = null
  try {
    await mealPlanStore.fetchPlan(planId)
    if (mealPlanStore.currentPlan) await mealPlanStore.fetchSuggestions(planId)
    else if (mealPlanStore.error) restoreError.value = mealPlanStore.error
    else router.replace({ name: 'meal-plan-create' })
  } finally {
    restoring.value = false
  }
}

async function createPlan() {
  if (!planName.value.trim()) {
    nameError.value = 'Plan name is required'
    return
  }
  nameError.value = ''
  creating.value  = true
  try {
    const id = await mealPlanStore.createPlan(planName.value.trim())
    // On success this builder stays busy on the name step until the one mounted at the new URL replaces it.
    await router.replace({ name: 'meal-plan-create', query: { [PLAN_QUERY_KEY]: id } })
  } catch (e) {
    creating.value = false
    throw e
  }
}

async function addRecipe(recipe) {
  const planId = builtPlan.value?.id
  if (!planId) return
  await mealPlanStore.addPlannedMeal(planId, { recipeId: recipe.id, scheduledDate: null })
}

async function addSuggestion(suggestion) {
  const planId = builtPlan.value?.id
  if (!planId) return
  await mealPlanStore.addPlannedMeal(planId, { recipeId: suggestion.recipeId, scheduledDate: null })
}

function openRecipe(meal) {
  router.push(recipeRoute(meal.recipeId, servingsOf(meal)))
}

async function removePlannedMeal(plannedMealId) {
  const planId = builtPlan.value?.id
  if (!planId) return
  await mealPlanStore.removePlannedMeal(planId, plannedMealId)
}

// Each step saves at once, so a step taken before the plan reloads must build on the one before it.
const steppedServings = ref({})
const servingsSaves   = {}

function servingsOf(meal) {
  return steppedServings.value[meal.id] ?? meal.servings
}

// One meal's saves run in turn, so the last step taken is the one stored.
function stepServings(meal, servings) {
  steppedServings.value[meal.id] = servings
  const save = (servingsSaves[meal.id] ?? Promise.resolve())
    .catch(() => {})
    .then(() => updatePlannedMeal(meal, { servings }))
  servingsSaves[meal.id] = save

  // Once the last save settles, the reloaded plan shows what was stored.
  const settle = () => {
    if (servingsSaves[meal.id] !== save) return
    delete servingsSaves[meal.id]
    delete steppedServings.value[meal.id]
  }
  save.then(settle, settle)
  return save
}

async function updatePlannedMeal(meal, changes) {
  const planId = builtPlan.value?.id
  if (!planId) return
  await mealPlanStore.updatePlannedMeal(planId, { ...meal, servings: servingsOf(meal) }, changes)
}

function done() {
  mealPlanStore.currentPlan = null
  router.push({ name: 'meal-plan' })
}
</script>
