<template>
  <v-bottom-sheet v-model="isOpen" max-width="480">
    <v-card class="pa-4 pb-6">
      <v-card-title class="text-h6 mb-2">Recently cooked</v-card-title>
      <v-card-text class="text-body-1">
        You cooked <strong>{{ recipeName }}</strong> in the last week — are you sure you want to include it?
      </v-card-text>
      <v-card-actions class="gap-2 pt-4">
        <v-btn variant="outlined" @click="cancel">Cancel</v-btn>
        <v-btn color="primary" variant="tonal" @click="confirm">Add Anyway</v-btn>
      </v-card-actions>
    </v-card>
  </v-bottom-sheet>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  recipeName: { type: String, default: '' },
})

const emit = defineEmits(['update:modelValue', 'confirm', 'cancel'])

const isOpen = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v),
})

function confirm() {
  emit('confirm')
  isOpen.value = false
}

function cancel() {
  emit('cancel')
  isOpen.value = false
}
</script>
