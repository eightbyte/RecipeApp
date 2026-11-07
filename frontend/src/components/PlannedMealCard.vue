<template>
  <v-card @click="openRecipe">
    <div class="d-flex align-center pa-3 ga-4">
      <div class="text-center" style="min-width: 48px">
        <template v-if="plannedMeal.scheduledDate">
          <div class="text-caption font-weight-bold">{{ formatDateOnly(plannedMeal.scheduledDate, { day: 'numeric' }) }}</div>
          <div class="text-caption text-medium-emphasis">{{ formatDateOnly(plannedMeal.scheduledDate, { month: 'short' }) }}</div>
        </template>
        <v-icon v-else color="grey" size="20">mdi-calendar-question</v-icon>
      </div>

      <v-divider vertical class="mx-1" />

      <v-avatar size="40" rounded="lg" color="grey-lighten-3">
        <v-img
          v-if="plannedMeal.recipeImageUrl"
          :src="plannedMeal.recipeImageUrl"
          :alt="plannedMeal.recipeName"
          cover
          :class="{ grayscale: isRecentlyCooked }"
        />
        <v-icon v-else color="grey" size="18">mdi-food</v-icon>
      </v-avatar>

      <div class="flex-grow-1 ml-2">
        <div class="text-body-1 font-weight-medium">{{ plannedMeal.recipeName }}</div>
        <ServingsChip :servings="plannedMeal.servings" size="x-small" class="mt-1" />
      </div>

      <v-menu v-model="mealOptionsOpen">
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
          <v-list-item prepend-icon="mdi-play-circle-outline" title="Start cooking" @click="startCooking" />
          <v-list-item prepend-icon="mdi-chef-hat" title="Mark as cooked" @click="markCooked" />
          <v-divider />
          <v-list-item prepend-icon="mdi-calendar" title="Change date" @click="openDatePicker" />
          <v-list-item prepend-icon="mdi-silverware" title="Change servings" @click="openServingsPicker" />
          <v-list-item prepend-icon="mdi-delete" title="Remove" @click="removePlannedMeal" />
        </v-list>
      </v-menu>
    </div>

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

    <v-dialog v-model="servingsPicker.open" max-width="360">
      <v-card class="pa-4">
        <v-card-title>Change servings</v-card-title>
        <v-card-text class="d-flex justify-center">
          <ServingsStepper v-model="servingsPicker.servings" />
        </v-card-text>
        <v-card-actions>
          <v-btn variant="text" @click="servingsPicker.open = false">Cancel</v-btn>
          <v-btn color="primary" @click="saveServingsChange">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-card>
</template>

<script setup>
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useMealPlanStore } from '@/stores/mealPlans'
import { useRecipeStore } from '@/stores/recipes'
import { useUiStore } from '@/stores/ui'
import { cookingRoute, recipeRoute } from '@/router/recipeRoutes'
import { formatDateOnly } from '@/utils/dateOnly'
import ServingsChip from '@/components/ServingsChip.vue'
import ServingsStepper from '@/components/ServingsStepper.vue'

const props = defineProps({
  plannedMeal: { type: Object, required: true },
  planId:      { type: String, required: true },
})

const router        = useRouter()
const mealPlanStore = useMealPlanStore()
const recipesStore  = useRecipeStore()
const ui            = useUiStore()

const mealOptionsOpen = ref(false)
const datePicker      = ref({ open: false, date: null })
const servingsPicker  = ref({ open: false, servings: props.plannedMeal.servings })

const isRecentlyCooked = computed(() =>
  recipesStore.isRecentlyCooked({ lastCookedAt: props.plannedMeal.recipeLastCookedAt }))

function openRecipe() {
  // The menu closes after this click, so a tap that only dismisses it would otherwise also navigate.
  if (mealOptionsOpen.value) return
  router.push(recipeRoute(props.plannedMeal.recipeId, props.plannedMeal.servings))
}

function startCooking() {
  router.push(cookingRoute(props.plannedMeal.recipeId, props.plannedMeal.servings))
}

async function markCooked() {
  try {
    await recipesStore.markCooked(props.plannedMeal.recipeId)
    await mealPlanStore.fetchActivePlan()   // the plan carries the last-cooked date that greys the image
    ui.notify({ message: `Marked “${props.plannedMeal.recipeName}” as cooked`, color: 'success' })
  } catch (e) {
    ui.notify({ message: e?.message ?? 'Could not mark as cooked.', color: 'error' })
  }
}

function openDatePicker() {
  datePicker.value = { open: true, date: props.plannedMeal.scheduledDate ?? null }
}

async function saveDateChange() {
  datePicker.value.open = false
  await mealPlanStore.updatePlannedMeal(props.planId, props.plannedMeal, { scheduledDate: datePicker.value.date })
}

function openServingsPicker() {
  servingsPicker.value = { open: true, servings: props.plannedMeal.servings }
}

async function saveServingsChange() {
  servingsPicker.value.open = false
  await mealPlanStore.updatePlannedMeal(props.planId, props.plannedMeal, { servings: servingsPicker.value.servings })
}

async function removePlannedMeal() {
  await mealPlanStore.removePlannedMeal(props.planId, props.plannedMeal.id)
}
</script>
