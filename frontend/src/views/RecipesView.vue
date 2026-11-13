<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Search bar and New recipe menu ───────────────────────────────── -->
    <div class="d-flex align-stretch ga-2" :class="hasNoRecipes ? 'mb-6' : 'mb-2'">
      <v-text-field
        v-model="search"
        prepend-inner-icon="mdi-magnify"
        placeholder="Search recipes or ingredients…"
        clearable
        hide-details
        @update:model-value="reloadAfterTyping"
      />

      <v-menu>
        <template #activator="{ props: menuProps }">
          <v-btn
            v-bind="menuProps"
            icon="mdi-plus"
            aria-label="New recipe"
            color="primary"
            variant="flat"
            rounded="lg"
            class="h-auto new-recipe-button"
          />
        </template>
        <v-list>
          <v-list-item
            prepend-icon="mdi-link"
            title="Import from URL"
            @click="openImportSheet(SCRAPE_SOURCE_URL)"
          />
          <v-list-item
            prepend-icon="mdi-text-box-outline"
            title="Import from text"
            @click="openImportSheet(SCRAPE_SOURCE_TEXT)"
          />
          <v-list-item
            prepend-icon="mdi-pencil-outline"
            title="Add manually"
            :to="{ name: 'recipe-create' }"
          />
        </v-list>
      </v-menu>
    </div>

    <!-- ── Sort ─────────────────────────────────────────────────────────── -->
    <div
      v-if="!hasNoRecipes"
      role="group"
      aria-label="Sort recipes"
      class="d-flex align-center justify-end ga-1 mb-4"
    >
      <v-select
        :model-value="listSort.sort"
        :items="RECIPE_SORTS"
        aria-label="Sort by"
        prefix="Sort:"
        variant="plain"
        density="compact"
        hide-details
        class="flex-grow-0 recipe-sort-field"
        @update:model-value="changeSortField"
      />
      <v-btn
        icon
        :aria-label="directionLabel"
        variant="text"
        size="small"
        @click="reverseDirection"
      >
        <v-icon :icon="directionIcon" />
        <v-tooltip activator="parent" location="bottom" :text="directionLabel" />
      </v-btn>
    </div>

    <!-- ── Loading skeleton (page 1) ────────────────────────────────────── -->
    <div v-if="loading">
      <v-skeleton-loader
        v-for="n in 3"
        :key="n"
        type="card"
        class="mb-4"
      />
    </div>

    <!-- ── Page 1 fetch error ───────────────────────────────────────────── -->
    <ErrorState
      v-else-if="error"
      :message="error"
      @retry="reload"
    />

    <!-- ── Recipe list ──────────────────────────────────────────────────── -->
    <RecipeInfiniteScroll
      v-else-if="items.length"
      :load-more="loadMore"
      :is-complete="isComplete"
      :page-count="pageCount"
      :total-count="totalCount"
    >
      <v-card
        v-for="recipe in items"
        :key="recipe.id"
        class="mb-4"
        :to="{ name: 'recipe-detail', params: { id: recipe.id } }"
        :ripple="true"
      >
        <div class="d-flex">
          <v-img
            :src="recipe.imageUrl || ''"
            :alt="recipe.name"
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
              <div class="d-flex ga-2">
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
    </RecipeInfiniteScroll>

    <!-- ── Empty state ──────────────────────────────────────────────────── -->
    <EmptyState
      v-else
      :icon="loadedSearch ? 'mdi-magnify' : 'mdi-chef-hat'"
      :title="loadedSearch ? 'No recipes match your search' : 'No recipes yet'"
      :text="loadedSearch ? 'Try a different search term' : 'Add your first recipe using the button above'"
    />

    <!-- ── Import Recipe sheet ──────────────────────────────────────────── -->
    <RecipeUrlBottomSheet v-model="showImportSheet" :initial-mode="importSource" />

  </v-container>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRecipeStore } from '@/stores/recipes'
import { useRecipePages } from '@/composables/useRecipePages'
import RecipeUrlBottomSheet from '@/components/RecipeUrlBottomSheet.vue'
import RecipeInfiniteScroll from '@/components/RecipeInfiniteScroll.vue'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'
import {
  RECIPE_SORTS,
  RECIPE_LIST_SORT_STORAGE_KEY,
  toRecipeListSort,
  reverseSortDirection,
  sortDirectionLabel,
  sortDirectionIcon,
} from '@/constants/recipeSorts'
import { readStoredJson, writeStoredJson } from '@/utils/storage'
import { SCRAPE_SOURCE_TEXT, SCRAPE_SOURCE_URL } from '@/constants/scrapeSources'

const store  = useRecipeStore()
const search = ref('')
const showImportSheet = ref(false)
const importSource    = ref(SCRAPE_SOURCE_URL)

function openImportSheet(source) {
  importSource.value    = source
  showImportSheet.value = true
}
const listSort = ref(toRecipeListSort(readStoredJson(RECIPE_LIST_SORT_STORAGE_KEY, null)))

const {
  items, totalCount, pageCount, isComplete, loading, error, loadedParams, reload, reloadAfterTyping, loadMore,
} = useRecipePages(() => ({
  ...(search.value ? { search: search.value } : {}),
  sort: listSort.value.sort,
  direction: listSort.value.direction,
}))

// The search the shown recipes were loaded for, null before the first load, so a reload or a pending search does not flip the empty state.
const loadedSearch = computed(() => loadedParams.value === null ? null : (loadedParams.value.search ?? ''))

const directionLabel = computed(() => sortDirectionLabel(listSort.value.sort, listSort.value.direction))
const directionIcon  = computed(() => sortDirectionIcon(listSort.value.direction))

// True exactly when "No recipes yet" shows, so a search with no results keeps the sort row.
const hasNoRecipes = computed(() =>
  loadedSearch.value === '' && !error.value && !items.value.length
)

onMounted(() => reload())

function applySort(recipeListSort) {
  listSort.value = recipeListSort
  writeStoredJson(RECIPE_LIST_SORT_STORAGE_KEY, recipeListSort)
  reload()
}

function changeSortField(sort) {
  applySort({ ...listSort.value, sort })
}

function reverseDirection() {
  applySort({ ...listSort.value, direction: reverseSortDirection(listSort.value.direction) })
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
.new-recipe-button { aspect-ratio: 1; }
.recipe-sort-field { min-width: 11rem; }
</style>
