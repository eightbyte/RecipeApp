<template>
  <v-btn-toggle
    v-model="batchSize"
    aria-label="Batch"
    :mandatory="mandatory"
    color="primary"
  >
    <v-btn
      v-for="option in BATCH_SIZES"
      :key="option.value"
      :value="option.value"
      :size="buttonSize"
    >
      {{ option.title }}
    </v-btn>
  </v-btn-toggle>
</template>

<script setup>
import { BATCH_SIZES } from '@/constants/batchSizes'

// Null when the toggle is not mandatory and no Batch Size matches what it stands in for.
const batchSize = defineModel({ type: [String, null], required: true })

defineProps({
  buttonSize: { type: String, default: 'default' },
  mandatory:  { type: Boolean, default: true },
})
</script>
