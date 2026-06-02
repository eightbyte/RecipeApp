<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Active plan header ───────────────────────────────────────────── -->
    <div v-if="mealPlanStore.activePlan">
      <div class="d-flex align-center justify-space-between mb-4">
        <div>
          <h2 class="text-h6 font-weight-bold">{{ mealPlanStore.activePlan.name }}</h2>
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

      <!-- Meal list (pre-sorted by API: dated first ascending, then undated by order) -->
      <v-card
        v-for="meal in mealPlanStore.activePlan.recipes"
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

          <v-avatar size="40" rounded="lg" color="grey-lighten-3">
            <v-img
              v-if="meal.recipeImageUrl"
              :src="meal.recipeImageUrl"
              cover
              :class="{ grayscale: isRecentlyCookedMeal(meal) }"
            />
            <v-icon v-else color="grey" size="18">mdi-food</v-icon>
          </v-avatar>

          <div class="flex-grow-1 ml-2">
            <div class="text-body-1 font-weight-medium">{{ meal.recipeName }}</div>
            <v-chip size="x-small" :color="portionColor(meal.portionSize)" class="mt-1">
              {{ portionLabel(meal.portionSize) }}
            </v-chip>
          </div>

          <!-- Per-meal overflow menu -->
          <v-menu>
            <template #activator="{ props: menuProps }">
              <v-btn icon="mdi-dots-vertical" variant="text" size="small" v-bind="menuProps" />
            </template>
            <v-list>
              <v-list-item prepend-icon="mdi-calendar" title="Change date" @click="openDatePicker(meal)" />
              <v-list-item prepend-icon="mdi-silverware" title="Change portion" @click="openPortionPicker(meal)" />
              <v-list-item prepend-icon="mdi-delete" title="Remove" @click="removeRecipe(meal.id)" />
            </v-list>
          </v-menu>
        </div>
      </v-card>

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
    <div v-else class="text-center py-12">
      <v-icon size="64" color="grey-lighten-2">mdi-calendar-week-outline</v-icon>
      <div class="text-h6 mt-3 mb-1">No active meal plan</div>
      <div class="text-body-2 text-medium-emphasis mb-4">
        Create a meal plan to organise your week
      </div>
      <v-btn color="primary" prepend-icon="mdi-plus" :to="{ name: 'meal-plan-create' }">
        New meal plan
      </v-btn>
    </div>

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

    <!-- ── Change date dialog ──────────────────────────────────────────── -->
    <v-dialog v-model="datePicker.open" max-width="360">
      <v-card class="pa-4">
        <v-card-title>Change scheduled date</v-card-title>
        <v-card-text>
          <v-text-field
            v-model="datePicker.date"
            type="date"
            label="Scheduled date"
            variant="outlined"
            clearable
          />
        </v-card-text>
        <v-card-actions>
          <v-btn variant="text" @click="datePicker.open = false">Cancel</v-btn>
          <v-btn color="primary" @click="saveDateChange">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- ── Change portion dialog ───────────────────────────────────────── -->
    <v-dialog v-model="portionPicker.open" max-width="360">
      <v-card class="pa-4">
        <v-card-title>Change portion size</v-card-title>
        <v-card-text>
          <v-btn-toggle v-model="portionPicker.portion" mandatory color="primary" class="d-flex">
            <v-btn value="HALF" class="flex-grow-1">½ Portion</v-btn>
            <v-btn value="REGULAR" class="flex-grow-1">Regular</v-btn>
            <v-btn value="DOUBLE" class="flex-grow-1">Double</v-btn>
          </v-btn-toggle>
        </v-card-text>
        <v-card-actions>
          <v-btn variant="text" @click="portionPicker.open = false">Cancel</v-btn>
          <v-btn color="primary" @click="savePortionChange">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

  </v-container>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useMealPlanStore } from '@/stores/mealPlans'
import { useRecipeStore } from '@/stores/recipes'
import RecipeBrowser from '@/components/RecipeBrowser.vue'

const mealPlanStore = useMealPlanStore()
const recipesStore  = useRecipeStore()

const browserOpen = ref(false)
const datePicker  = ref({ open: false, mprId: null, date: null })
const portionPicker = ref({ open: false, mprId: null, portion: 'REGULAR' })

onMounted(() => mealPlanStore.fetchActivePlan())

function isRecentlyCookedMeal(meal) {
  return recipesStore.isRecentlyCooked({ lastCookedAt: meal.recipeLastCookedAt })
}

async function onAddRecipe(recipe) {
  if (!mealPlanStore.activePlan) return
  browserOpen.value = false
  await mealPlanStore.addRecipe(mealPlanStore.activePlan.id, {
    recipeId: recipe.id,
    scheduledDate: null,
    portionSize: 'REGULAR',
  })
}

function openDatePicker(meal) {
  datePicker.value = { open: true, mprId: meal.id, date: meal.scheduledDate ?? null }
}

async function saveDateChange() {
  const { mprId, date } = datePicker.value
  datePicker.value.open = false
  const planId = mealPlanStore.activePlan?.id
  if (!planId) return
  await mealPlanStore.updatePlanRecipe(planId, mprId, {
    scheduledDate: date || null,
    portionSize: mealPlanStore.activePlan.recipes.find(r => r.id === mprId)?.portionSize ?? 'REGULAR',
  })
}

function openPortionPicker(meal) {
  portionPicker.value = { open: true, mprId: meal.id, portion: meal.portionSize }
}

async function savePortionChange() {
  const { mprId, portion } = portionPicker.value
  portionPicker.value.open = false
  const planId = mealPlanStore.activePlan?.id
  if (!planId) return
  const meal = mealPlanStore.activePlan.recipes.find(r => r.id === mprId)
  await mealPlanStore.updatePlanRecipe(planId, mprId, {
    scheduledDate: meal?.scheduledDate ?? null,
    portionSize: portion,
  })
}

async function removeRecipe(mprId) {
  const planId = mealPlanStore.activePlan?.id
  if (!planId) return
  await mealPlanStore.removePlanRecipe(planId, mprId)
}

const portionLabel = (size) =>
  ({ HALF: '½ portion', REGULAR: 'Regular', DOUBLE: 'Double' })[size] ?? size

const portionColor = (size) =>
  ({ HALF: 'info', REGULAR: 'success', DOUBLE: 'warning' })[size] ?? 'default'

const formatDay   = (d) => new Date(d).toLocaleDateString('en-AU', { day: 'numeric' })
const formatMonth = (d) => new Date(d).toLocaleDateString('en-AU', { month: 'short' })
</script>
