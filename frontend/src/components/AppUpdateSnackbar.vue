<template>
  <!-- At the top and with no timeout, so it sits clear of the feedback snackbar and waits until the user chooses. -->
  <v-snackbar
    :model-value="ui.updateReady"
    :timeout="-1"
    color="primary"
    location="top"
    @update:model-value="onToggle"
  >
    <span role="status" aria-live="polite">A new version of RecipeApp is ready.</span>

    <template #actions>
      <v-btn variant="text" :loading="reloading" @click="reload">Reload</v-btn>
      <v-btn
        variant="text"
        icon="mdi-close"
        aria-label="Not now"
        :disabled="reloading"
        @click="ui.dismissUpdate()"
      />
    </template>
  </v-snackbar>
</template>

<script setup>
import { ref } from 'vue'
import { useUiStore } from '@/stores/ui'

const ui = useUiStore()

// The page reloads once the new version takes control, which can take a moment.
const reloading = ref(false)

function reload() {
  reloading.value = true
  ui.applyUpdate()
}

function onToggle(value) {
  if (!value) ui.dismissUpdate()
}
</script>
