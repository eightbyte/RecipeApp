<template>
  <v-btn-toggle
    v-model="unitSystem"
    aria-label="Unit System"
    mandatory
    color="primary"
  >
    <v-btn
      v-for="option in UNIT_SYSTEMS"
      :key="option.value"
      :value="option.value"
      :size="buttonSize"
    >
      {{ option.title }}
    </v-btn>
  </v-btn-toggle>
</template>

<script setup>
import { UNIT_SYSTEMS } from '@/constants/unitSystems'

const unitSystem = defineModel({ type: String, required: true })

defineProps({
  buttonSize: { type: String, default: 'default' },
})
</script>
