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
      <v-card
        v-for="meal in mealPlanStore.activePlan.plannedMeals"
        :key="meal.id"
        class="mb-4"
      >
        <div class="d-flex align-center pa-3 ga-4">
          <div class="text-center" style="min-width: 48px">
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
              :alt="meal.recipeName"
              cover
              :class="{ grayscale: isRecentlyCookedMeal(meal) }"
            />
            <v-icon v-else color="grey" size="18">mdi-food</v-icon>
          </v-avatar>

          <div class="flex-grow-1 ml-2">
            <div class="text-body-1 font-weight-medium">{{ meal.recipeName }}</div>
            <v-chip size="x-small" :color="batchSizeColor(meal.batchSize)" class="mt-1">
              {{ formatBatchSize(meal.batchSize) }}
            </v-chip>
          </div>

          <!-- Per-meal overflow menu -->
          <v-menu>
            <template #activator="{ props: menuProps }">
              <v-btn
                icon="mdi-dots-vertical"
                variant="text"
                size="small"
                aria-label="Meal options"
                v-bind="menuProps"
              />
            </template>
            <v-list>
              <v-list-item prepend-icon="mdi-play-circle-outline" title="Start cooking" @click="startCooking(meal)" />
              <v-list-item prepend-icon="mdi-chef-hat" title="Mark as cooked" @click="markMealCooked(meal)" />
              <v-divider />
              <v-list-item prepend-icon="mdi-calendar" title="Change date" @click="openDatePicker(meal)" />
              <v-list-item prepend-icon="mdi-silverware" title="Change batch" @click="openBatchSizePicker(meal)" />
              <v-list-item prepend-icon="mdi-delete" title="Remove" @click="removePlannedMeal(meal.id)" />
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

    <!-- ── Change batch dialog ─────────────────────────────────────────── -->
    <v-dialog v-model="batchSizePicker.open" max-width="360">
      <v-card class="pa-4">
        <v-card-title>Batch</v-card-title>
        <v-card-text>
          <v-btn-toggle v-model="batchSizePicker.batchSize" mandatory color="primary" class="d-flex">
            <v-btn
              v-for="batchSize in BATCH_SIZES"
              :key="batchSize.value"
              :value="batchSize.value"
              class="flex-grow-1"
            >
              {{ batchSize.title }}
            </v-btn>
          </v-btn-toggle>
        </v-card-text>
        <v-card-actions>
          <v-btn variant="text" @click="batchSizePicker.open = false">Cancel</v-btn>
          <v-btn color="primary" @click="saveBatchSizeChange">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

  </v-container>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useMealPlanStore } from '@/stores/mealPlans'
import { useRecipeStore } from '@/stores/recipes'
import { useUiStore } from '@/stores/ui'
import { BATCH_SIZES, DEFAULT_BATCH_SIZE, batchSizeColor, formatBatchSize } from '@/constants/batchSizes'
import RecipeBrowser from '@/components/RecipeBrowser.vue'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'

const router        = useRouter()
const mealPlanStore = useMealPlanStore()
const recipesStore  = useRecipeStore()
const ui            = useUiStore()

const browserOpen     = ref(false)
const datePicker      = ref({ open: false, plannedMealId: null, date: null })
const batchSizePicker = ref({ open: false, plannedMealId: null, batchSize: DEFAULT_BATCH_SIZE })

onMounted(() => mealPlanStore.fetchActivePlan())

function isRecentlyCookedMeal(meal) {
  return recipesStore.isRecentlyCooked({ lastCookedAt: meal.recipeLastCookedAt })
}

function startNewPlan() {
  // The builder creates a new active plan, so the backend closes the current one and the shopping list regenerates on next visit.
  router.push({ name: 'meal-plan-create' })
}

function startCooking(meal) {
  router.push({
    name: 'recipe-cooking',
    params: { id: meal.recipeId },
    query: { batch: meal.batchSize },
  })
}

async function markMealCooked(meal) {
  try {
    await recipesStore.markCooked(meal.recipeId)
    await mealPlanStore.fetchActivePlan()   // refresh greyscale / recently-cooked
    ui.notify({ message: `Marked “${meal.recipeName}” as cooked`, color: 'success' })
  } catch (e) {
    ui.notify({ message: e?.message ?? 'Could not mark as cooked.', color: 'error' })
  }
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

function openDatePicker(meal) {
  datePicker.value = { open: true, plannedMealId: meal.id, date: meal.scheduledDate ?? null }
}

async function saveDateChange() {
  const { plannedMealId, date } = datePicker.value
  datePicker.value.open = false
  const planId = mealPlanStore.activePlan?.id
  if (!planId) return
  await mealPlanStore.updatePlannedMeal(planId, plannedMealId, {
    scheduledDate: date || null,
    batchSize: mealPlanStore.activePlan.plannedMeals.find(candidate => candidate.id === plannedMealId)?.batchSize ?? DEFAULT_BATCH_SIZE,
  })
}

function openBatchSizePicker(meal) {
  batchSizePicker.value = { open: true, plannedMealId: meal.id, batchSize: meal.batchSize }
}

async function saveBatchSizeChange() {
  const { plannedMealId, batchSize } = batchSizePicker.value
  batchSizePicker.value.open = false
  const planId = mealPlanStore.activePlan?.id
  if (!planId) return
  const meal = mealPlanStore.activePlan.plannedMeals.find(candidate => candidate.id === plannedMealId)
  await mealPlanStore.updatePlannedMeal(planId, plannedMealId, {
    scheduledDate: meal?.scheduledDate ?? null,
    batchSize,
  })
}

async function removePlannedMeal(plannedMealId) {
  const planId = mealPlanStore.activePlan?.id
  if (!planId) return
  await mealPlanStore.removePlannedMeal(planId, plannedMealId)
}

const formatDay   = (d) => new Date(d).toLocaleDateString('en-AU', { day: 'numeric' })
const formatMonth = (d) => new Date(d).toLocaleDateString('en-AU', { month: 'short' })
</script>
