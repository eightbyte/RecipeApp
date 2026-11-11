<template>
  <v-select
    :model-value="hasAmount(amount) ? unit : null"
    :items="UNIT_PICKER_ITEMS"
    label="Unit"
    density="compact"
    hide-details="auto"
    :error="error"
    :placeholder="countPlaceholder"
    :persistent-placeholder="!!countPlaceholder"
    :disabled="!hasAmount(amount)"
    style="max-width: 100px"
    @update:model-value="unit = $event"
  />
</template>

<script setup>
import { computed } from 'vue'
import { UNIT_PICKER_ITEMS, COUNT_UNIT, hasAmount } from '@/constants/units'

// Kept while the amount is empty and only shown blank, so the picked unit returns with an amount.
const unit = defineModel({ type: [String, null], default: null })

const props = defineProps({
  amount: { type: [Number, String, null], default: null },
  // Set when an empty unit cannot stand for a count, such as a scraped "can" whose size pcs would lose.
  requiresUnit: { type: Boolean, default: false },
  // Marks the picker without a message, which is too narrow to hold one; the caller shows it.
  error: { type: Boolean, default: false },
})

// An amount with no unit saves as a count, so the empty field says so, unless a unit has to be picked.
const countPlaceholder = computed(() =>
  hasAmount(props.amount) && !props.requiresUnit ? COUNT_UNIT : undefined)
</script>
