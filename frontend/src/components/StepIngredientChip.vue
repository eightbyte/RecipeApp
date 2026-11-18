<template>
  <v-chip>
    <!-- Absent for an Unquantified Ingredient, or when the Source Measurement already states the same count -->
    <span v-if="measurement.amount" class="font-weight-medium mr-1">{{ measurement.amount }}</span>
    <span v-if="measurement.source" class="text-caption mr-1">{{ measurement.source }}</span>
    {{ recipeIngredient.ingredientDisplayName }}
  </v-chip>
</template>

<script setup>
defineProps({
  recipeIngredient: { type: Object, required: true },
  // The row's `{ amount, source }` display parts at the chosen servings and Unit System, from `useRecipeMeasurements`.
  measurement: { type: Object, required: true },
})
</script>
