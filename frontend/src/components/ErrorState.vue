<template>
  <div class="text-center py-12">
    <v-icon icon="mdi-alert-circle-outline" size="64" color="grey-lighten-2" />
    <div class="text-body-1 mt-3 mb-4">{{ message }}</div>
    <v-btn
      color="primary"
      variant="tonal"
      prepend-icon="mdi-refresh"
      @click="emit('retry')"
    >
      Try again
    </v-btn>
  </div>
</template>

<script setup>
defineProps({
  message: { type: String, default: 'Something went wrong.' },
})

const emit = defineEmits(['retry'])
</script>
