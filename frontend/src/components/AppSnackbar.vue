<template>
  <!-- Single global snackbar mounted once in App.vue so every view, including full-screen Cooking Mode, can surface feedback via useUiStore().notify(). -->
  <!-- Keyed per notification because Vuetify starts the timeout only on opening, and a replacement such as a second Undo deserves its own. -->
  <v-snackbar
    :key="ui.snackbar.id"
    :model-value="ui.snackbar.show"
    :color="ui.snackbar.color"
    :timeout="3500"
    location="bottom"
    @update:model-value="onToggle"
  >
    <span role="status" aria-live="polite">{{ ui.snackbar.message }}</span>

    <template #actions>
      <v-btn
        v-if="ui.snackbar.action"
        variant="text"
        @click="ui.runAction()"
      >
        {{ ui.snackbar.action.label }}
      </v-btn>
      <v-btn
        variant="text"
        icon="mdi-close"
        aria-label="Dismiss notification"
        @click="ui.dismiss()"
      />
    </template>
  </v-snackbar>
</template>

<script setup>
import { useUiStore } from '@/stores/ui'

const ui = useUiStore()

function onToggle(value) {
  if (!value) ui.dismiss()
}
</script>
