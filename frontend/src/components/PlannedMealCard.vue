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
        <BatchSizeChip :batch-size="plannedMeal.batchSize" size="x-small" class="mt-1" />
      </div>

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
          <v-list-item prepend-icon="mdi-play-circle-outline" title="Start cooking" @click="startCooking" />
          <v-list-item prepend-icon="mdi-chef-hat" title="Mark as cooked" @click="markCooked" />
          <v-divider />
          <v-list-item prepend-icon="mdi-calendar" title="Change date" @click="openDatePicker" />
          <v-list-item prepend-icon="mdi-silverware" title="Change batch" @click="openBatchSizePicker" />
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

    <v-dialog v-model="batchSizePicker.open" max-width="360">
      <v-card class="pa-4">
        <v-card-title>Batch</v-card-title>
        <v-card-text>
          <BatchSizeToggle v-model="batchSizePicker.batchSize" stretch />
        </v-card-text>
        <v-card-actions>
          <v-btn variant="text" @click="batchSizePicker.open = false">Cancel</v-btn>
          <v-btn color="primary" @click="saveBatchSizeChange">Save</v-btn>
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
import { DEFAULT_BATCH_SIZE } from '@/constants/batchSizes'
import { cookingRoute, recipeRoute } from '@/router/recipeRoutes'
import { formatDateOnly } from '@/utils/dateOnly'
import BatchSizeChip from '@/components/BatchSizeChip.vue'
import BatchSizeToggle from '@/components/BatchSizeToggle.vue'

const props = defineProps({
  plannedMeal: { type: Object, required: true },
  planId:      { type: String, required: true },
})

const router        = useRouter()
const mealPlanStore = useMealPlanStore()
const recipesStore  = useRecipeStore()
const ui            = useUiStore()

const datePicker      = ref({ open: false, date: null })
const batchSizePicker = ref({ open: false, batchSize: DEFAULT_BATCH_SIZE })

const isRecentlyCooked = computed(() =>
  recipesStore.isRecentlyCooked({ lastCookedAt: props.plannedMeal.recipeLastCookedAt }))

function openRecipe() {
  router.push(recipeRoute(props.plannedMeal.recipeId, props.plannedMeal.batchSize))
}

function startCooking() {
  router.push(cookingRoute(props.plannedMeal.recipeId, props.plannedMeal.batchSize))
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

function openBatchSizePicker() {
  batchSizePicker.value = { open: true, batchSize: props.plannedMeal.batchSize }
}

async function saveBatchSizeChange() {
  batchSizePicker.value.open = false
  await mealPlanStore.updatePlannedMeal(props.planId, props.plannedMeal, { batchSize: batchSizePicker.value.batchSize })
}

async function removePlannedMeal() {
  await mealPlanStore.removePlannedMeal(props.planId, props.plannedMeal.id)
}
</script>
