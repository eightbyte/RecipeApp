<template>
  <v-alert
    v-if="problems.length"
    type="warning"
    variant="tonal"
    density="compact"
    class="mb-6"
    data-test="recipe-problems"
  >
    <div class="font-weight-medium">{{ headline }}</div>
    <ul class="pl-4">
      <li v-for="(entry, i) in problems" :key="i" class="text-body-2">
        {{ entry.where }} — {{ entry.message }}
      </li>
    </ul>
  </v-alert>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  // Summary entries from `validateRecipe`, already filtered to those showing.
  problems: { type: Array, required: true },
})

const headline = computed(() => props.problems.length === 1
  ? '1 problem needs fixing before saving'
  : `${props.problems.length} problems need fixing before saving`)
</script>
