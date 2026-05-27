<template>
  <v-app-bar
    color="primary"
    elevation="2"
    density="comfortable"
  >
    <!-- Back button — shown on nested routes -->
    <v-btn
      v-if="canGoBack"
      icon="mdi-arrow-left"
      @click="router.back()"
    />

    <v-app-bar-title class="font-weight-bold text-white">
      {{ pageTitle }}
    </v-app-bar-title>

    <template #append>
      <slot name="actions" />
    </template>
  </v-app-bar>
</template>

<script setup>
import { computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'

const router = useRouter()
const route  = useRoute()

const pageTitle = computed(() => route.meta?.title ?? 'RecipeApp')

// Show back button only on routes deeper than the top-level tabs
const topLevelRoutes = ['home', 'recipes', 'meal-plan', 'shopping']
const canGoBack = computed(() => !topLevelRoutes.includes(route.name))
</script>
