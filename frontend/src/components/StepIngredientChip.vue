<template>
  <v-chip>
    <!-- Absent for an Unquantified Ingredient, or when the Source Measurement already states the same count -->
    <span v-if="measurement.amount" class="font-weight-medium mr-1">{{ measurement.amount }}</span>
    <span v-if="measurement.source" class="text-caption mr-1">{{ measurement.source }}</span>
    {{ recipeIngredient.ingredientDisplayName }}
  </v-chip>
</template>

<script setup>
import { computed } from 'vue'
import { formatIngredientMeasurement } from '@/constants/units'

const props = defineProps({
  recipeIngredient: { type: Object, required: true },
  servingsMultiplier: { type: Number, required: true },
})

const measurement = computed(() => formatIngredientMeasurement(props.recipeIngredient, props.servingsMultiplier))
</script>
