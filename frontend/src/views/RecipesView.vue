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
      @update:model-value="onSearch"
    />

    <!-- ── Loading skeleton ─────────────────────────────────────────────── -->
    <div v-if="store.loading">
      <v-skeleton-loader
        v-for="n in 3"
        :key="n"
        type="card"
        class="mb-3"
      />
    </div>

    <!-- ── Recipe list ──────────────────────────────────────────────────── -->
    <div v-else-if="store.recipes.length">
      <v-card
        v-for="recipe in store.recipes"
        :key="recipe.id"
        class="mb-3"
        :to="{ name: 'recipe-detail', params: { id: recipe.id } }"
        :ripple="true"
      >
        <div class="d-flex">
          <v-img
            :src="recipe.imageUrl || ''"
            :class="{ grayscale: store.isRecentlyCooked(recipe) }"
            width="110"
            height="110"
            cover
            style="flex-shrink: 0; border-radius: 12px 0 0 12px"
          >
            <template #placeholder>
              <div class="d-flex align-center justify-center fill-height bg-grey-lighten-3">
                <v-icon color="grey-lighten-1">mdi-food</v-icon>
              </div>
            </template>
          </v-img>

          <div class="pa-3 flex-grow-1 d-flex flex-column justify-space-between">
            <div>
              <div class="text-body-1 font-weight-medium">{{ recipe.name }}</div>
              <div
                v-if="recipe.description"
                class="text-caption text-medium-emphasis mt-1"
                style="display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden"
              >
                {{ recipe.description }}
              </div>
            </div>

            <div class="d-flex align-center justify-space-between mt-2">
              <div class="d-flex gap-2">
                <v-chip size="x-small" variant="tonal">
                  <v-icon start size="12">mdi-account-group</v-icon>
                  {{ recipe.servings }} servings
                </v-chip>
                <v-chip size="x-small" variant="tonal">
                  <v-icon start size="12">mdi-format-list-bulleted</v-icon>
                  {{ recipe.ingredientCount }} ingredients
                </v-chip>
              </div>

              <v-chip
                v-if="store.isRecentlyCooked(recipe)"
                size="x-small"
                color="warning"
                variant="tonal"
              >
                Cooked recently
              </v-chip>
              <span
                v-else-if="recipe.lastCookedAt"
                class="text-caption text-medium-emphasis"
              >
                {{ formatRelative(recipe.lastCookedAt) }}
              </span>
            </div>
          </div>
        </div>
      </v-card>
    </div>

    <!-- ── Empty state ──────────────────────────────────────────────────── -->
    <div v-else class="text-center py-12">
      <v-icon size="64" color="grey-lighten-2">mdi-chef-hat</v-icon>
      <div class="text-h6 mt-3 mb-1">
        {{ search ? 'No recipes match your search' : 'No recipes yet' }}
      </div>
      <div class="text-body-2 text-medium-emphasis mb-4">
        {{ search ? 'Try a different search term' : 'Add your first recipe below' }}
      </div>
    </div>

    <!-- ── Error state ──────────────────────────────────────────────────── -->
    <v-alert
      v-if="store.error"
      type="error"
      class="mt-4"
      closable
      @click:close="store.error = null"
    >
      {{ store.error }}
    </v-alert>

    <!-- ── FAB ─────────────────────────────────────────────────────────── -->
    <v-speed-dial
      location="bottom end"
      transition="slide-y-reverse-transition"
    >
      <template #activator="{ props: p }">
        <v-fab v-bind="p" icon="mdi-plus" color="primary" size="large" />
      </template>

      <v-btn
        prepend-icon="mdi-link"
        text="Import from URL"
        color="secondary"
        variant="tonal"
        rounded="xl"
        @click="showImportSheet = true"
      />

      <v-btn
        prepend-icon="mdi-pencil-outline"
        text="Add manually"
        color="primary"
        variant="tonal"
        rounded="xl"
        :to="{ name: 'recipe-create' }"
      />
    </v-speed-dial>

    <!-- ── Import from URL sheet ───────────────────────────────────────── -->
    <RecipeUrlBottomSheet v-model="showImportSheet" />

  </v-container>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRecipeStore } from '@/stores/recipes'
import RecipeUrlBottomSheet from '@/components/RecipeUrlBottomSheet.vue'

const store  = useRecipeStore()
const search = ref('')
const showImportSheet = ref(false)
let searchTimer = null

onMounted(() => store.fetchRecipes())

function onSearch(val) {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    store.fetchRecipes(val ? { search: val } : {})
  }, 300)
}

function formatRelative(dateStr) {
  const diffDays = Math.floor(
    (Date.now() - new Date(dateStr).getTime()) / (1000 * 60 * 60 * 24)
  )
  if (diffDays === 0) return 'Cooked today'
  if (diffDays === 1) return 'Cooked yesterday'
  return `Cooked ${diffDays}d ago`
}
</script>

<style scoped>
.grayscale { filter: grayscale(100%); }
</style>
