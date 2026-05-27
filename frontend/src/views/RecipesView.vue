<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Search bar ───────────────────────────────────────────────────── -->
    <v-text-field
      v-model="search"
      prepend-inner-icon="mdi-magnify"
      placeholder="Search recipes or ingredients…"
      clearable
      hide-details
      class="mb-4"
    />

    <!-- ── Recipe grid ──────────────────────────────────────────────────── -->
    <div v-if="filteredRecipes.length" class="recipe-grid">
      <v-card
        v-for="recipe in filteredRecipes"
        :key="recipe.id"
        class="mb-3"
        :ripple="true"
      >
        <v-img
          :src="recipe.imageUrl"
          :class="{ 'grayscale': isRecentlyCoooked(recipe) }"
          height="160"
          cover
        >
          <template #placeholder>
            <div class="d-flex align-center justify-center fill-height bg-grey-lighten-3">
              <v-icon size="48" color="grey">mdi-food</v-icon>
            </div>
          </template>
        </v-img>

        <v-card-item>
          <v-card-title>{{ recipe.name }}</v-card-title>
          <v-card-subtitle v-if="recipe.lastCookedAt">
            Last cooked {{ formatRelative(recipe.lastCookedAt) }}
          </v-card-subtitle>
        </v-card-item>
      </v-card>
    </div>

    <!-- ── Empty state ──────────────────────────────────────────────────── -->
    <div v-else class="text-center py-12">
      <v-icon size="64" color="grey-lighten-2">mdi-chef-hat</v-icon>
      <div class="text-h6 mt-3 mb-1">No recipes yet</div>
      <div class="text-body-2 text-medium-emphasis mb-4">
        Add your first recipe or import one from a URL
      </div>
    </div>

    <!-- ── FAB — add / import ───────────────────────────────────────────── -->
    <v-speed-dial
      location="bottom end"
      transition="slide-y-reverse-transition"
    >
      <template #activator="{ props: activatorProps }">
        <v-fab
          v-bind="activatorProps"
          icon="mdi-plus"
          color="primary"
          size="large"
        />
      </template>

      <v-btn
        key="manual"
        prepend-icon="mdi-pencil-outline"
        text="Add manually"
        color="primary"
        variant="tonal"
        rounded="xl"
      />
      <v-btn
        key="scrape"
        prepend-icon="mdi-link-variant"
        text="Import from URL"
        color="secondary"
        variant="tonal"
        rounded="xl"
      />
    </v-speed-dial>

  </v-container>
</template>

<script setup>
import { ref, computed } from 'vue'

const search  = ref('')
const recipes = ref([])   // filled from API in Phase 2

const filteredRecipes = computed(() => {
  if (!search.value) return recipes.value
  const q = search.value.toLowerCase()
  return recipes.value.filter(r =>
    r.name.toLowerCase().includes(q)
  )
})

function isRecentlyCoooked(recipe) {
  if (!recipe.lastCookedAt) return false
  const sevenDaysAgo = Date.now() - 7 * 24 * 60 * 60 * 1000
  return new Date(recipe.lastCookedAt).getTime() > sevenDaysAgo
}

function formatRelative(dateStr) {
  const diffDays = Math.floor(
    (Date.now() - new Date(dateStr).getTime()) / (1000 * 60 * 60 * 24)
  )
  if (diffDays === 0) return 'today'
  if (diffDays === 1) return 'yesterday'
  return `${diffDays} days ago`
}
</script>

<style scoped>
.grayscale {
  filter: grayscale(100%);
}
</style>
