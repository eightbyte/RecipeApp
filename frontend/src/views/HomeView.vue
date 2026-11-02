<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Loading skeleton (initial active-plan fetch) ─────────────────── -->
    <template v-if="mealPlanStore.loading && !mealPlanStore.activePlan">
      <v-skeleton-loader type="heading" class="mb-4" />
      <v-skeleton-loader v-for="n in 2" :key="n" type="list-item-avatar-two-line" class="mb-4" />
    </template>

    <!-- ── Active meal plan ─────────────────────────────────────────────── -->
    <section v-else-if="mealPlanStore.activePlan" class="mb-6">
      <div class="d-flex align-center justify-space-between mb-4">
        <h1 class="text-h6 font-weight-bold mb-2">This Week's Plan</h1>
        <v-btn
          variant="text"
          color="primary"
          size="small"
          :to="{ name: 'meal-plan' }"
        >
          View all
        </v-btn>
      </div>

      <PlannedMealCard
        v-for="meal in mealPlanStore.activePlan.plannedMeals"
        :key="meal.id"
        :planned-meal="meal"
        :plan-id="mealPlanStore.activePlan.id"
        class="mb-4"
      />
    </section>

    <!-- ── No active plan ──────────────────────────────────────────────── -->
    <v-card v-else class="text-center pa-8" variant="outlined">
      <v-icon size="64" color="grey-lighten-2" class="mb-4">
        mdi-calendar-plus-outline
      </v-icon>
      <div class="text-h6 mb-1">No active meal plan</div>
      <div class="text-body-2 text-medium-emphasis mb-6">
        Create a meal plan to get started
      </div>
      <v-btn color="primary" :to="{ name: 'meal-plan-create' }">
        New meal plan
      </v-btn>
    </v-card>

    <!-- ── Food waste stat ──────────────────────────────────────────────── -->
    <v-card class="mt-6 pa-4 d-flex align-center ga-4" color="success" variant="tonal">
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
import PlannedMealCard from '@/components/PlannedMealCard.vue'

const mealPlanStore = useMealPlanStore()

onMounted(() => mealPlanStore.fetchActivePlan())
</script>
