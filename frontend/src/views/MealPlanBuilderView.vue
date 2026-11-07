<template>
  <v-container class="pa-4" max-width="600">

    <div v-if="restoring" class="d-flex justify-center py-6">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <!-- ── Step 1: Name entry ────────────────────────────────────────── -->
    <div v-else-if="!mealPlanStore.currentPlan">
      <h2 class="text-h6 font-weight-bold mb-6">New Meal Plan</h2>

      <v-alert
        v-if="mealPlanStore.activePlan"
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
          <h2 class="text-h6 font-weight-bold mb-2">{{ mealPlanStore.currentPlan.name }}</h2>
          <div class="text-caption text-medium-emphasis">Active plan — add recipes below</div>
        </div>
        <v-btn color="primary" variant="tonal" @click="done">Done</v-btn>
      </div>

      <!-- Added recipes -->
      <div v-if="mealPlanStore.currentPlan.plannedMeals.length > 0" class="mb-6">
        <v-card
          v-for="meal in mealPlanStore.currentPlan.plannedMeals"
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
              <BatchSizeToggle
                :model-value="meal.batchSize"
                button-size="small"
                density="compact"
                style="height: 40px"
                @update:model-value="(batchSize) => updatePlannedMeal(meal, { batchSize })"
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
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMealPlanStore } from '@/stores/mealPlans'
import { DEFAULT_BATCH_SIZE } from '@/constants/batchSizes'
import { recipeRoute } from '@/router/recipeRoutes'
import { DISPLAY_LOCALE } from '@/utils/dateOnly'
import BatchSizeToggle from '@/components/BatchSizeToggle.vue'
import RecipeBrowser from '@/components/RecipeBrowser.vue'
import SuggestionsPanel from '@/components/SuggestionsPanel.vue'

const PLAN_QUERY_KEY = 'plan'

const router        = useRouter()
const route         = useRoute()
const mealPlanStore = useMealPlanStore()

const planName  = ref(`Week of ${new Date().toLocaleDateString(DISPLAY_LOCALE, { day: 'numeric', month: 'long' })}`)
const nameError = ref('')
const creating  = ref(false)
const restoring = ref(false)

// Without a plan in the URL, start at the name step rather than at a plan left in the store from a previously viewed plan.
onMounted(async () => {
  const planId = route.query[PLAN_QUERY_KEY]
  if (!planId) {
    mealPlanStore.currentPlan = null
    return
  }
  restoring.value = true
  try {
    await mealPlanStore.fetchPlan(planId)
    if (mealPlanStore.currentPlan) await mealPlanStore.fetchSuggestions(planId)
  } finally {
    restoring.value = false
  }
})

async function createPlan() {
  if (!planName.value.trim()) {
    nameError.value = 'Plan name is required'
    return
  }
  nameError.value = ''
  creating.value  = true
  try {
    const id = await mealPlanStore.createPlan(planName.value.trim())
    router.replace({ name: 'meal-plan-create', query: { [PLAN_QUERY_KEY]: id } })
    await mealPlanStore.fetchSuggestions(id)
  } finally {
    creating.value = false
  }
}

async function addRecipe(recipe) {
  const planId = mealPlanStore.currentPlan?.id
  if (!planId) return
  await mealPlanStore.addPlannedMeal(planId, {
    recipeId: recipe.id,
    scheduledDate: null,
    batchSize: DEFAULT_BATCH_SIZE,
  })
}

async function addSuggestion(suggestion) {
  const planId = mealPlanStore.currentPlan?.id
  if (!planId) return
  await mealPlanStore.addPlannedMeal(planId, {
    recipeId: suggestion.recipeId,
    scheduledDate: null,
    batchSize: DEFAULT_BATCH_SIZE,
  })
}

function openRecipe(meal) {
  router.push(recipeRoute(meal.recipeId, meal.batchSize))
}

async function removePlannedMeal(plannedMealId) {
  const planId = mealPlanStore.currentPlan?.id
  if (!planId) return
  await mealPlanStore.removePlannedMeal(planId, plannedMealId)
}

async function updatePlannedMeal(meal, changes) {
  const planId = mealPlanStore.currentPlan?.id
  if (!planId) return
  await mealPlanStore.updatePlannedMeal(planId, meal, changes)
}

function done() {
  mealPlanStore.currentPlan = null
  router.push({ name: 'meal-plan' })
}
</script>
