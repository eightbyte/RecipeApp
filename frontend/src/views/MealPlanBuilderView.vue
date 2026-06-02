<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Phase 1: Name entry ──────────────────────────────────────────── -->
    <div v-if="!mealPlanStore.currentPlan">
      <h2 class="text-h6 font-weight-bold mb-4">New Meal Plan</h2>

      <v-text-field
        v-model="planName"
        label="Plan name"
        placeholder="e.g. Week of 2 June"
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

    <!-- ── Phase 2: Recipe builder ──────────────────────────────────────── -->
    <div v-else>
      <div class="d-flex align-center justify-space-between mb-4">
        <div>
          <h2 class="text-h6 font-weight-bold">{{ mealPlanStore.currentPlan.name }}</h2>
          <div class="text-caption text-medium-emphasis">Active plan — add recipes below</div>
        </div>
        <v-btn color="primary" variant="tonal" @click="done">Done</v-btn>
      </div>

      <!-- Added recipes -->
      <div v-if="mealPlanStore.currentPlan.recipes.length > 0" class="mb-4">
        <v-card
          v-for="meal in mealPlanStore.currentPlan.recipes"
          :key="meal.id"
          class="mb-2"
          variant="outlined"
        >
          <div class="pa-3">
            <div class="d-flex align-center gap-2 mb-2">
              <v-avatar size="36" rounded="lg" color="grey-lighten-3">
                <v-img v-if="meal.recipeImageUrl" :src="meal.recipeImageUrl" cover />
                <v-icon v-else color="grey" size="16">mdi-food</v-icon>
              </v-avatar>
              <div class="flex-grow-1 text-body-2 font-weight-medium">{{ meal.recipeName }}</div>
              <v-btn icon="mdi-delete" variant="text" size="x-small" color="error" @click="removeRecipe(meal.id)" />
            </div>

            <div class="d-flex gap-2 flex-wrap">
              <v-text-field
                :model-value="meal.scheduledDate ?? ''"
                type="date"
                label="Date"
                variant="outlined"
                density="compact"
                hide-details
                clearable
                style="min-width: 160px; flex: 1"
                @update:model-value="(v) => updateDate(meal, v || null)"
              />
              <v-btn-toggle
                :model-value="meal.portionSize"
                mandatory
                color="primary"
                density="compact"
                style="height: 40px"
                @update:model-value="(v) => updatePortion(meal, v)"
              >
                <v-btn value="HALF" size="small">½</v-btn>
                <v-btn value="REGULAR" size="small">Regular</v-btn>
                <v-btn value="DOUBLE" size="small">Double</v-btn>
              </v-btn-toggle>
            </div>
          </div>
        </v-card>
      </div>

      <!-- Suggestions -->
      <div v-if="mealPlanStore.suggestions.length > 0" class="mb-4">
        <SuggestionsPanel
          :suggestions="mealPlanStore.suggestions"
          @add="addSuggestion"
        />
      </div>

      <!-- Recipe browser -->
      <v-divider class="mb-3" />
      <h3 class="text-subtitle-1 font-weight-bold mb-3">Browse recipes</h3>
      <RecipeBrowser @add="addRecipe" />
    </div>

  </v-container>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useMealPlanStore } from '@/stores/mealPlans'
import RecipeBrowser from '@/components/RecipeBrowser.vue'
import SuggestionsPanel from '@/components/SuggestionsPanel.vue'

const router        = useRouter()
const mealPlanStore = useMealPlanStore()

const planName  = ref('')
const nameError = ref('')
const creating  = ref(false)

async function createPlan() {
  if (!planName.value.trim()) {
    nameError.value = 'Plan name is required'
    return
  }
  nameError.value = ''
  creating.value  = true
  try {
    const id = await mealPlanStore.createPlan(planName.value.trim())
    await mealPlanStore.fetchSuggestions(id)
  } finally {
    creating.value = false
  }
}

async function addRecipe(recipe) {
  const planId = mealPlanStore.currentPlan?.id
  if (!planId) return
  await mealPlanStore.addRecipe(planId, {
    recipeId: recipe.id,
    scheduledDate: null,
    portionSize: 'REGULAR',
  })
}

async function addSuggestion(suggestion) {
  const planId = mealPlanStore.currentPlan?.id
  if (!planId) return
  await mealPlanStore.addRecipe(planId, {
    recipeId: suggestion.recipeId,
    scheduledDate: null,
    portionSize: 'REGULAR',
  })
}

async function removeRecipe(mprId) {
  const planId = mealPlanStore.currentPlan?.id
  if (!planId) return
  await mealPlanStore.removePlanRecipe(planId, mprId)
}

async function updateDate(meal, newDate) {
  const planId = mealPlanStore.currentPlan?.id
  if (!planId) return
  await mealPlanStore.updatePlanRecipe(planId, meal.id, {
    scheduledDate: newDate,
    portionSize: meal.portionSize,
  })
}

async function updatePortion(meal, newPortion) {
  const planId = mealPlanStore.currentPlan?.id
  if (!planId) return
  await mealPlanStore.updatePlanRecipe(planId, meal.id, {
    scheduledDate: meal.scheduledDate ?? null,
    portionSize: newPortion,
  })
}

function done() {
  mealPlanStore.currentPlan = null
  router.push({ name: 'meal-plan' })
}
</script>
