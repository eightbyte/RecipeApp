<template>
  <v-container class="pa-4" max-width="600">
    <h1 class="text-h6 font-weight-bold mb-4">Past Meal Plans</h1>

    <!-- ── Loading skeleton ─────────────────────────────────────────────── -->
    <template v-if="mealPlanStore.loading">
      <v-skeleton-loader v-for="n in 3" :key="n" type="list-item-two-line" class="mb-3" />
    </template>

    <!-- ── Fetch error ──────────────────────────────────────────────────── -->
    <ErrorState
      v-else-if="mealPlanStore.error"
      :message="mealPlanStore.error"
      @retry="mealPlanStore.fetchPlans()"
    />

    <!-- ── Empty ────────────────────────────────────────────────────────── -->
    <EmptyState
      v-else-if="pastPlans.length === 0"
      icon="mdi-calendar-blank-outline"
      title="No past meal plans yet"
      text="Closed meal plans will appear here."
    />

    <!-- ── List ─────────────────────────────────────────────────────────── -->
    <template v-else>
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
    </template>

  </v-container>
</template>

<script setup>
import { computed, onMounted } from 'vue'
import { useMealPlanStore } from '@/stores/mealPlans'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'

const mealPlanStore = useMealPlanStore()

// Past plans = all non-active plans, already ordered newest first by the API
const pastPlans = computed(() => mealPlanStore.plans.filter(p => !p.isActive))

onMounted(() => mealPlanStore.fetchPlans())

const formatDate = (d) =>
  d ? new Date(d).toLocaleDateString('en-AU', { year: 'numeric', month: 'short', day: 'numeric' }) : ''
</script>
