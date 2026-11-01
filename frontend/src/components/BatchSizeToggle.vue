<template>
  <v-btn-toggle
    v-model="batchSize"
    aria-label="Batch"
    mandatory
    color="primary"
    :class="{ 'd-flex': stretch }"
  >
    <v-btn
      v-for="option in BATCH_SIZES"
      :key="option.value"
      :value="option.value"
      :size="buttonSize"
      :class="{ 'flex-grow-1': stretch }"
    >
      {{ option.title }}
    </v-btn>
  </v-btn-toggle>
</template>

<script setup>
import { BATCH_SIZES } from '@/constants/batchSizes'

const batchSize = defineModel({ type: String, required: true })

defineProps({
  buttonSize: { type: String, default: 'default' },
  stretch:    { type: Boolean, default: false },
})
</script>
