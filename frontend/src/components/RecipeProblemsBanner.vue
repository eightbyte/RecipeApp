<template>
  <v-alert
    v-if="problems.length"
    type="warning"
    variant="tonal"
    density="compact"
    :class="['mb-6', PROBLEMS_BANNER_CLASS]"
    data-test="recipe-problems"
  >
    <div class="font-weight-medium">{{ headline }}</div>
    <v-list density="compact" bg-color="transparent" class="py-0">
      <v-list-item
        v-for="(entry, i) in problems"
        :key="i"
        class="px-0"
        min-height="0"
      >
        <v-list-item-title class="text-body-2 text-wrap">{{ entry.where }} — {{ entry.message }}</v-list-item-title>
      </v-list-item>
    </v-list>
  </v-alert>
</template>

<script setup>
import { computed } from 'vue'
import { PROBLEMS_BANNER_CLASS } from '@/composables/useRecipeValidation'

const props = defineProps({
  // Summary entries from `validateRecipe`, already filtered to those showing.
  problems: { type: Array, required: true },
})

const headline = computed(() => props.problems.length === 1
  ? '1 problem needs fixing before saving'
  : `${props.problems.length} problems need fixing before saving`)
</script>
