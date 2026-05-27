<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Active plan header ───────────────────────────────────────────── -->
    <div v-if="activePlan">
      <div class="d-flex align-center justify-space-between mb-4">
        <div>
          <h2 class="text-h6 font-weight-bold">{{ activePlan.name }}</h2>
          <div class="text-caption text-medium-emphasis">Active plan</div>
        </div>
        <v-btn
          color="primary"
          variant="tonal"
          prepend-icon="mdi-cart-outline"
          size="small"
          :to="{ name: 'shopping' }"
        >
          Shopping list
        </v-btn>
      </div>

      <!-- Meal list sorted by date -->
      <v-card
        v-for="meal in sortedMeals"
        :key="meal.id"
        class="mb-3"
      >
        <div class="d-flex align-center pa-3 gap-3">
          <div class="date-chip text-center" style="min-width: 48px">
            <div v-if="meal.scheduledDate" class="text-caption font-weight-bold">
              {{ formatDay(meal.scheduledDate) }}
            </div>
            <div v-if="meal.scheduledDate" class="text-caption text-medium-emphasis">
              {{ formatMonth(meal.scheduledDate) }}
            </div>
            <v-icon v-else color="grey" size="20">mdi-calendar-question</v-icon>
          </div>

          <v-divider vertical class="mx-1" />

          <div class="flex-grow-1">
            <div class="text-body-1 font-weight-medium">{{ meal.recipe.name }}</div>
            <v-chip size="x-small" :color="portionColor(meal.portionSize)" class="mt-1">
              {{ portionLabel(meal.portionSize) }}
            </v-chip>
          </div>

          <v-btn icon="mdi-dots-vertical" variant="text" size="small" />
        </div>
      </v-card>

      <!-- Add recipe button -->
      <v-btn
        block
        variant="outlined"
        color="primary"
        prepend-icon="mdi-plus"
        class="mt-2"
      >
        Add recipe
      </v-btn>
    </div>

    <!-- ── No active plan ──────────────────────────────────────────────── -->
    <div v-else class="text-center py-12">
      <v-icon size="64" color="grey-lighten-2">mdi-calendar-week-outline</v-icon>
      <div class="text-h6 mt-3 mb-1">No active meal plan</div>
      <div class="text-body-2 text-medium-emphasis mb-4">
        Create a meal plan to organise your week
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus">
        New meal plan
      </v-btn>
    </div>

    <!-- ── Past plans link ─────────────────────────────────────────────── -->
    <div class="mt-8 text-center">
      <v-btn variant="text" color="medium-emphasis" size="small">
        View past meal plans
      </v-btn>
    </div>

  </v-container>
</template>

<script setup>
import { ref, computed } from 'vue'

const activePlan = ref(null)  // filled from API in Phase 4

const sortedMeals = computed(() => {
  if (!activePlan.value) return []
  return [...activePlan.value.recipes].sort((a, b) => {
    if (!a.scheduledDate) return 1
    if (!b.scheduledDate) return -1
    return new Date(a.scheduledDate) - new Date(b.scheduledDate)
  })
})

const portionLabel = (size) =>
  ({ HALF: '½ portion', REGULAR: 'Regular', DOUBLE: 'Double' })[size] ?? size

const portionColor = (size) =>
  ({ HALF: 'info', REGULAR: 'success', DOUBLE: 'warning' })[size] ?? 'default'

const formatDay   = (d) => new Date(d).toLocaleDateString('en-AU', { day: 'numeric' })
const formatMonth = (d) => new Date(d).toLocaleDateString('en-AU', { month: 'short' })
</script>
