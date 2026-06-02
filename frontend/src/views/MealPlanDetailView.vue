<template>
  <v-container class="pa-4" max-width="600">

    <div v-if="mealPlanStore.loading" class="d-flex justify-center py-8">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <div v-else-if="mealPlanStore.currentPlan">
      <!-- Header -->
      <div class="mb-4">
        <div class="d-flex align-center gap-2 mb-1">
          <h2 class="text-h6 font-weight-bold">{{ mealPlanStore.currentPlan.name }}</h2>
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
            <v-img v-if="meal.recipeImageUrl" :src="meal.recipeImageUrl" cover />
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

    <div v-else class="text-center py-8 text-medium-emphasis">
      Plan not found
    </div>

  </v-container>
</template>

<script setup>
import { onMounted } from 'vue'
import { useMealPlanStore } from '@/stores/mealPlans'

const props = defineProps({ id: { type: String, required: true } })

const mealPlanStore = useMealPlanStore()

onMounted(() => mealPlanStore.fetchPlan(props.id))

const portionLabel = (size) =>
  ({ HALF: '½ portion', REGULAR: 'Regular', DOUBLE: 'Double' })[size] ?? size

const portionColor = (size) =>
  ({ HALF: 'info', REGULAR: 'success', DOUBLE: 'warning' })[size] ?? 'default'

const formatDate  = (d) => new Date(d).toLocaleDateString('en-AU', { year: 'numeric', month: 'short', day: 'numeric' })
const formatDay   = (d) => new Date(d).toLocaleDateString('en-AU', { day: 'numeric' })
const formatMonth = (d) => new Date(d).toLocaleDateString('en-AU', { month: 'short' })
</script>
