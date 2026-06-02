<template>
  <v-container class="pa-4" max-width="600">
    <h2 class="text-h6 font-weight-bold mb-4">Past Meal Plans</h2>

    <div v-if="mealPlanStore.loading" class="d-flex justify-center py-8">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <div v-else-if="pastPlans.length === 0" class="text-center py-8 text-medium-emphasis">
      No past meal plans yet
    </div>

    <v-card
      v-for="plan in pastPlans"
      :key="plan.id"
      class="mb-3"
      :to="{ name: 'meal-plan-detail', params: { id: plan.id } }"
      :ripple="true"
    >
      <v-card-text>
        <div class="d-flex align-center justify-space-between">
          <div>
            <div class="text-body-1 font-weight-medium">{{ plan.name }}</div>
            <div class="text-caption text-medium-emphasis mt-1">
              <span v-if="plan.firstScheduledDate">
                {{ formatDate(plan.firstScheduledDate) }}
                <span v-if="plan.lastScheduledDate && plan.lastScheduledDate !== plan.firstScheduledDate">
                  – {{ formatDate(plan.lastScheduledDate) }}
                </span>
              </span>
              <span v-else>No dates scheduled</span>
            </div>
          </div>
          <div class="text-right">
            <v-chip size="x-small" class="mb-1">{{ plan.recipeCount }} recipes</v-chip>
            <div class="text-caption text-medium-emphasis">
              Closed {{ formatDate(plan.closedAt) }}
            </div>
          </div>
        </div>
      </v-card-text>
    </v-card>

  </v-container>
</template>

<script setup>
import { computed, onMounted } from 'vue'
import { useMealPlanStore } from '@/stores/mealPlans'

const mealPlanStore = useMealPlanStore()

// Past plans = all non-active plans, already ordered newest first by the API
const pastPlans = computed(() => mealPlanStore.plans.filter(p => !p.isActive))

onMounted(() => mealPlanStore.fetchPlans())

const formatDate = (d) =>
  d ? new Date(d).toLocaleDateString('en-AU', { year: 'numeric', month: 'short', day: 'numeric' }) : ''
</script>
