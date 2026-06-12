<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Loading skeleton (initial active-plan fetch) ─────────────────── -->
    <template v-if="mealPlanStore.loading && !mealPlanStore.activePlan">
      <v-skeleton-loader type="heading" class="mb-3" />
      <v-skeleton-loader v-for="n in 2" :key="n" type="list-item-avatar-two-line" class="mb-3" />
    </template>

    <!-- ── Active meal plan ─────────────────────────────────────────────── -->
    <section v-else-if="mealPlanStore.activePlan" class="mb-6">
      <div class="d-flex align-center justify-space-between mb-3">
        <h1 class="text-h6 font-weight-bold">This Week's Plan</h1>
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
              :alt="meal.recipeName"
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
            :aria-label="`Mark ${meal.recipeName} as cooked`"
            @click="cookRecipe(meal)"
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
import { useUiStore } from '@/stores/ui'

const mealPlanStore = useMealPlanStore()
const recipesStore  = useRecipeStore()
const ui            = useUiStore()

onMounted(() => mealPlanStore.fetchActivePlan())

function isRecentlyCookedMeal(meal) {
  return recipesStore.isRecentlyCooked({ lastCookedAt: meal.recipeLastCookedAt })
}

async function cookRecipe(meal) {
  try {
    await recipesStore.markCooked(meal.recipeId)
    await mealPlanStore.fetchActivePlan()
    ui.notify({ message: `Marked “${meal.recipeName}” as cooked`, color: 'success' })
  } catch (e) {
    ui.notify({ message: e?.message ?? 'Could not mark as cooked.', color: 'error' })
  }
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
