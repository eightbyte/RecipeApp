<template>
  <v-chip :size="size">{{ formatServings(servings) }}</v-chip>
</template>

<script setup>
import { formatServings } from '@/constants/servings'

defineProps({
  servings: { type: Number, required: true },
  size:     { type: String, default: 'default' },
})
</script>
