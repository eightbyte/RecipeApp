<template>
  <v-chip>
    <!-- Absent for an ingredient whose source stated no quantity -->
    <span v-if="measurement.amount" class="font-weight-medium mr-1">{{ measurement.amount }}</span>
    <span v-if="measurement.source" class="text-caption mr-1">{{ measurement.source }}</span>
    {{ ingredient.ingredientDisplayName }}
  </v-chip>
</template>

<script setup>
import { computed } from 'vue'
import { formatIngredientMeasurement } from '@/constants/units'

const props = defineProps({
  ingredient: { type: Object, required: true },
  multiplier: { type: Number, required: true },
})

const measurement = computed(() => formatIngredientMeasurement(props.ingredient, props.multiplier))
</script>
