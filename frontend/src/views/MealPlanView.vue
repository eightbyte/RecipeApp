<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Loading skeleton (initial active-plan fetch) ─────────────────── -->
    <template v-if="mealPlanStore.loading && !mealPlanStore.activePlan">
      <v-skeleton-loader type="heading" class="mb-6" />
      <v-skeleton-loader v-for="n in 3" :key="n" type="list-item-avatar-two-line" class="mb-2" />
    </template>

    <!-- ── Fetch error ──────────────────────────────────────────────────── -->
    <ErrorState
      v-else-if="mealPlanStore.error"
      :message="mealPlanStore.error"
      @retry="mealPlanStore.fetchActivePlan()"
    />

    <!-- ── Active plan header ───────────────────────────────────────────── -->
    <div v-else-if="mealPlanStore.activePlan">
      <div class="d-flex align-center justify-space-between mb-6">
        <div>
          <h1 class="text-h6 font-weight-bold mb-2">{{ mealPlanStore.activePlan.name }}</h1>
          <div class="text-caption text-medium-emphasis">Active plan</div>
        </div>
        <div class="d-flex align-center ga-1">
          <v-btn
            color="primary"
            variant="tonal"
            prepend-icon="mdi-cart-outline"
            size="small"
            :to="{ name: 'shopping' }"
          >
            Shopping list
          </v-btn>

          <!-- Plan-level overflow menu -->
          <v-menu>
            <template #activator="{ props: menuProps }">
              <v-btn
                icon="mdi-dots-vertical"
                variant="text"
                size="small"
                aria-label="Plan options"
                v-bind="menuProps"
              />
            </template>
            <v-list>
              <v-list-item
                prepend-icon="mdi-calendar-plus"
                title="Start new plan"
                @click="startNewPlan"
              />
            </v-list>
          </v-menu>
        </div>
      </div>

      <!-- Meal list (pre-sorted by API: dated first ascending, then undated by order) -->
      <PlannedMealCard
        v-for="meal in mealPlanStore.activePlan.plannedMeals"
        :key="meal.id"
        :planned-meal="meal"
        :plan-id="mealPlanStore.activePlan.id"
        class="mb-4"
      />

      <!-- Add recipe button opens inline browser -->
      <v-btn
        block
        variant="outlined"
        color="primary"
        prepend-icon="mdi-plus"
        class="mt-2"
        @click="browserOpen = true"
      >
        Add recipe
      </v-btn>
    </div>

    <!-- ── No active plan ──────────────────────────────────────────────── -->
    <EmptyState
      v-else
      icon="mdi-calendar-week-outline"
      title="No active meal plan"
      text="Create a meal plan to organise your week"
      action-label="New meal plan"
      :action-to="{ name: 'meal-plan-create' }"
    />

    <!-- ── Past plans link ─────────────────────────────────────────────── -->
    <div class="mt-8 text-center">
      <v-btn variant="text" color="medium-emphasis" size="small" :to="{ name: 'meal-plan-past' }">
        View past meal plans
      </v-btn>
    </div>

    <!-- ── Inline recipe browser dialog ───────────────────────────────── -->
    <v-dialog v-model="browserOpen" max-width="560" scrollable>
      <v-card>
        <v-card-title class="d-flex align-center justify-space-between">
          <span>Add recipe</span>
          <v-btn icon="mdi-close" variant="text" @click="browserOpen = false" />
        </v-card-title>
        <v-card-text>
          <RecipeBrowser @add="onAddRecipe" />
        </v-card-text>
      </v-card>
    </v-dialog>

  </v-container>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useMealPlanStore } from '@/stores/mealPlans'
import { useUiStore } from '@/stores/ui'
import { DEFAULT_BATCH_SIZE } from '@/constants/batchSizes'
import PlannedMealCard from '@/components/PlannedMealCard.vue'
import RecipeBrowser from '@/components/RecipeBrowser.vue'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'

const router        = useRouter()
const mealPlanStore = useMealPlanStore()
const ui            = useUiStore()

const browserOpen = ref(false)

onMounted(() => mealPlanStore.fetchActivePlan())

function startNewPlan() {
  // The builder creates a new active plan, so the backend closes the current one and the shopping list regenerates on next visit.
  router.push({ name: 'meal-plan-create' })
}

async function onAddRecipe(recipe) {
  if (!mealPlanStore.activePlan) return
  browserOpen.value = false
  try {
    await mealPlanStore.addPlannedMeal(mealPlanStore.activePlan.id, {
      recipeId: recipe.id,
      scheduledDate: null,
      batchSize: DEFAULT_BATCH_SIZE,
    })
    ui.notify({ message: `Added “${recipe.name}” to your plan`, color: 'success' })
  } catch (e) {
    ui.notify({ message: e?.message ?? 'Could not add recipe.', color: 'error' })
  }
}
</script>
