<template>
  <div class="d-flex align-center ga-2">
    <span class="text-body-2 font-weight-medium">Servings</span>
    <v-btn
      icon="mdi-minus"
      size="small"
      variant="tonal"
      aria-label="Fewer servings"
      :disabled="servings <= MIN_SERVINGS"
      @click="stepDown"
    />
    <span
      class="servings-stepper-value text-body-1 font-weight-medium text-center"
      style="min-width: 3ch"
      aria-live="polite"
    >{{ formatMeasurement(servings) }}</span>
    <v-btn
      icon="mdi-plus"
      size="small"
      variant="tonal"
      aria-label="More servings"
      :disabled="servings >= MAX_SERVINGS"
      @click="stepUp"
    />
  </div>
</template>

<script setup>
import { formatMeasurement } from '@/constants/units'
import { MIN_SERVINGS, MAX_SERVINGS } from '@/constants/servings'

// Can arrive fractional from a Batch Size shortcut, but every step lands on a whole number.
const servings = defineModel({ type: Number, required: true })

function stepDown() {
  servings.value = Math.ceil(servings.value) - 1
}

function stepUp() {
  servings.value = Math.floor(servings.value) + 1
}
</script>
