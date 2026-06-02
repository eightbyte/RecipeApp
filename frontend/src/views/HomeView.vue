<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Active meal plan ─────────────────────────────────────────────── -->
    <section v-if="mealPlanStore.activePlan" class="mb-6">
      <div class="d-flex align-center justify-space-between mb-3">
        <h2 class="text-h6 font-weight-bold">This Week's Plan</h2>
        <v-btn
          variant="text"
          color="primary"
          size="small"
          :to="{ name: 'meal-plan' }"
        >
          View all
        </v-btn>
      </div>

      <v-card
        v-for="meal in mealPlanStore.activePlan.recipes"
        :key="meal.id"
        class="mb-3"
      >
        <div class="d-flex align-center pa-3 gap-3">
          <v-avatar size="56" rounded="lg" color="grey-lighten-3">
            <v-img
              v-if="meal.recipeImageUrl"
              :src="meal.recipeImageUrl"
              cover
              :class="{ grayscale: isRecentlyCookedMeal(meal) }"
            />
            <v-icon v-else color="grey">mdi-food</v-icon>
          </v-avatar>

          <div class="flex-grow-1">
            <div class="text-body-1 font-weight-medium">{{ meal.recipeName }}</div>
            <div class="text-caption text-medium-emphasis">
              {{ meal.scheduledDate ? formatDate(meal.scheduledDate) : 'Unscheduled' }}
              &nbsp;·&nbsp;
              {{ portionLabel(meal.portionSize) }}
            </div>
          </div>

          <v-btn
            icon="mdi-chef-hat"
            variant="tonal"
            color="primary"
            size="small"
            @click="cookRecipe(meal.recipeId)"
          />
        </div>
      </v-card>
    </section>

    <!-- ── No active plan ──────────────────────────────────────────────── -->
    <v-card v-else class="text-center pa-8" variant="outlined">
      <v-icon size="64" color="grey-lighten-2" class="mb-3">
        mdi-calendar-plus-outline
      </v-icon>
      <div class="text-h6 mb-1">No active meal plan</div>
      <div class="text-body-2 text-medium-emphasis mb-4">
        Create a meal plan to get started
      </div>
      <v-btn color="primary" :to="{ name: 'meal-plan-create' }">
        New meal plan
      </v-btn>
    </v-card>

    <!-- ── Food waste stat ──────────────────────────────────────────────── -->
    <v-card class="mt-4 pa-4 d-flex align-center gap-3" color="success" variant="tonal">
      <v-icon color="success" size="32">mdi-leaf</v-icon>
      <div>
        <div class="text-body-2 font-weight-medium">Food waste saved</div>
        <div class="text-caption text-medium-emphasis">
          Coming soon — tracked after your first meal plan
        </div>
      </div>
    </v-card>

  </v-container>
</template>

<script setup>
import { onMounted } from 'vue'
import { useMealPlanStore } from '@/stores/mealPlans'
import { useRecipeStore } from '@/stores/recipes'

const mealPlanStore = useMealPlanStore()
const recipesStore  = useRecipeStore()

onMounted(() => mealPlanStore.fetchActivePlan())

function isRecentlyCookedMeal(meal) {
  return recipesStore.isRecentlyCooked({ lastCookedAt: meal.recipeLastCookedAt })
}

async function cookRecipe(recipeId) {
  await recipesStore.markCooked(recipeId)
  await mealPlanStore.fetchActivePlan()
}

function formatDate(dateStr) {
  return new Date(dateStr).toLocaleDateString('en-AU', {
    weekday: 'short', month: 'short', day: 'numeric',
  })
}

function portionLabel(size) {
  return { HALF: '½ portion', REGULAR: 'Regular', DOUBLE: 'Double' }[size] ?? size
}
</script>
