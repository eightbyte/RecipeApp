<template>
  <v-select
    :model-value="hasAmount(amount) ? unit : null"
    :items="MEASUREMENT_UNITS"
    label="Unit"
    density="compact"
    hide-details
    :disabled="!hasAmount(amount)"
    style="max-width: 100px"
    @update:model-value="unit = $event"
  />
</template>

<script setup>
import { MEASUREMENT_UNITS, hasAmount } from '@/constants/units'

// Kept while the amount is empty and only shown blank, so the picked unit returns with an amount.
const unit = defineModel({ type: [String, null], default: null })

defineProps({
  amount: { type: [Number, String, null], default: null },
})
</script>
