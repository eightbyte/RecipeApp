<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Loading skeleton ─────────────────────────────────────────────── -->
    <template v-if="mealPlanStore.loading">
      <v-skeleton-loader type="heading" class="mb-4" />
      <v-skeleton-loader v-for="n in 3" :key="n" type="list-item-avatar-two-line" class="mb-2" />
    </template>

    <!-- ── Fetch error (network/5xx) — distinct from "not found" below ───── -->
    <ErrorState
      v-else-if="mealPlanStore.error"
      :message="mealPlanStore.error"
      @retry="reload"
    />

    <div v-else-if="mealPlanStore.currentPlan">
      <!-- Header -->
      <div class="mb-4">
        <div class="d-flex align-center gap-2 mb-1">
          <h1 class="text-h6 font-weight-bold">{{ mealPlanStore.currentPlan.name }}</h1>
          <v-chip size="x-small" :color="mealPlanStore.currentPlan.isActive ? 'success' : 'default'">
            {{ mealPlanStore.currentPlan.isActive ? 'Active' : 'Closed' }}
          </v-chip>
        </div>
        <div class="text-caption text-medium-emphasis">
          Created {{ formatDate(mealPlanStore.currentPlan.createdAt) }}
          <span v-if="mealPlanStore.currentPlan.closedAt">
            · Closed {{ formatDate(mealPlanStore.currentPlan.closedAt) }}
          </span>
        </div>
      </div>

      <!-- Recipe list (pre-sorted: dated ascending first, then undated) -->
      <v-card
        v-for="meal in mealPlanStore.currentPlan.recipes"
        :key="meal.id"
        class="mb-3"
        variant="outlined"
      >
        <div class="d-flex align-center pa-3 gap-3">
          <div class="text-center" style="min-width: 44px">
            <template v-if="meal.scheduledDate">
              <div class="text-caption font-weight-bold">{{ formatDay(meal.scheduledDate) }}</div>
              <div class="text-caption text-medium-emphasis">{{ formatMonth(meal.scheduledDate) }}</div>
            </template>
            <v-icon v-else color="grey" size="18">mdi-calendar-question</v-icon>
          </div>

          <v-divider vertical class="mx-1" />

          <v-avatar size="40" rounded="lg" color="grey-lighten-3">
            <v-img v-if="meal.recipeImageUrl" :src="meal.recipeImageUrl" :alt="meal.recipeName" cover />
            <v-icon v-else color="grey" size="16">mdi-food</v-icon>
          </v-avatar>

          <div class="flex-grow-1 ml-1">
            <div class="text-body-2 font-weight-medium">{{ meal.recipeName }}</div>
            <v-chip size="x-small" :color="portionColor(meal.portionSize)" class="mt-1">
              {{ portionLabel(meal.portionSize) }}
            </v-chip>
          </div>
        </div>
      </v-card>

      <div v-if="mealPlanStore.currentPlan.recipes.length === 0" class="text-center py-6 text-medium-emphasis">
        No recipes in this plan
      </div>
    </div>

    <!-- ── Not found (404) ──────────────────────────────────────────────── -->
    <EmptyState
      v-else
      icon="mdi-calendar-remove-outline"
      title="Plan not found"
      action-label="Back to meal plans"
      :action-to="{ name: 'meal-plan' }"
    />

  </v-container>
</template>

<script setup>
import { onMounted } from 'vue'
import { useMealPlanStore } from '@/stores/mealPlans'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'

const props = defineProps({ id: { type: String, required: true } })

const mealPlanStore = useMealPlanStore()

function reload() {
  mealPlanStore.fetchPlan(props.id)
}

onMounted(reload)

const portionLabel = (size) =>
  ({ HALF: '½ portion', REGULAR: 'Regular', DOUBLE: 'Double' })[size] ?? size

const portionColor = (size) =>
  ({ HALF: 'info', REGULAR: 'success', DOUBLE: 'warning' })[size] ?? 'default'

const formatDate  = (d) => new Date(d).toLocaleDateString('en-AU', { year: 'numeric', month: 'short', day: 'numeric' })
const formatDay   = (d) => new Date(d).toLocaleDateString('en-AU', { day: 'numeric' })
const formatMonth = (d) => new Date(d).toLocaleDateString('en-AU', { month: 'short' })
</script>
