<template>
  <v-chip :size="size" :color="batchSizeColor(batchSize)" :aria-label="`Batch ${label}`">
    {{ label }}
  </v-chip>
</template>

<script setup>
import { computed } from 'vue'
import { batchSizeColor, formatBatchSize } from '@/constants/batchSizes'

const props = defineProps({
  batchSize: { type: String, required: true },
  size:      { type: String, default: 'default' },
})

const label = computed(() => formatBatchSize(props.batchSize))
</script>
