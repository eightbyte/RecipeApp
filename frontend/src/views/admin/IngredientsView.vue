<template>
  <v-container class="pa-4" max-width="600">
    <div class="d-flex align-center ga-2 mb-2">
      <v-text-field
        v-model="search"
        data-test="ingredient-search"
        label="Search"
        prepend-inner-icon="mdi-magnify"
        density="compact"
        hide-details
        clearable
      />
      <v-btn color="primary" prepend-icon="mdi-plus" :to="{ name: 'admin-ingredient-new' }">Add Ingredient</v-btn>
    </div>

    <div class="d-flex align-center ga-4 mb-2">
      <v-select
        v-model="category"
        data-test="category-filter"
        :items="INGREDIENT_CATEGORIES"
        label="Category"
        placeholder="All categories"
        persistent-placeholder
        density="compact"
        hide-details
        clearable
      />
      <v-switch
        v-model="showRemoved"
        label="Show removed"
        color="primary"
        density="compact"
        hide-details
        class="flex-grow-0"
      />
    </div>

    <div v-if="loading" class="d-flex justify-center py-12">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <ErrorState v-else-if="error" :message="error" @retry="load" />

    <EmptyState v-else-if="matches.length === 0" icon="mdi-food-apple-outline" title="No ingredients match" />

    <template v-else>
      <v-list class="pa-0">
        <v-list-item
          v-for="ingredient in shown"
          :key="ingredient.id"
          data-test="ingredient-row"
          class="px-0"
          :to="{ name: 'admin-ingredient-edit', params: { id: ingredient.id } }"
          :title="ingredient.displayName"
          :subtitle="categoryTitle(ingredient.category)"
        >
          <template v-if="ingredient.removedAt" #append>
            <v-chip size="small" color="warning" variant="tonal">Removed</v-chip>
          </template>
        </v-list-item>
      </v-list>

      <div v-if="shown.length < matches.length" class="d-flex justify-center mt-2">
        <v-btn variant="text" @click="shownCount += PAGE_SIZE">Show more</v-btn>
      </div>
    </template>
  </v-container>
</template>

<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useIngredientStore } from '@/stores/ingredients'
import { INGREDIENT_CATEGORIES, categoryTitle } from '@/constants/categories'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'

// Rendering the whole catalogue at once is slow on a phone.
const PAGE_SIZE = 50

const ingredientStore = useIngredientStore()

const search      = ref('')
const category    = ref(null)
const showRemoved = ref(false)
const shownCount  = ref(PAGE_SIZE)
const loading     = ref(false)
const error       = ref(null)

const matches = computed(() => {
  const text = (search.value ?? '').trim().toLowerCase()
  return ingredientStore.allIngredients
    .filter(i => showRemoved.value || !i.removedAt)
    .filter(i => !category.value || i.category === category.value)
    .filter(i => !text
      || i.displayName.toLowerCase().includes(text)
      || i.aliases.some(alias => alias.includes(text)))
    .sort((a, b) => a.displayName.localeCompare(b.displayName))
})

const shown = computed(() => matches.value.slice(0, shownCount.value))

watch([search, category, showRemoved], () => { shownCount.value = PAGE_SIZE })

async function load() {
  loading.value = true
  error.value   = null
  try {
    await ingredientStore.fetchAllIngredients()
  } catch (e) {
    error.value = e.message
  }
  loading.value = false
}

onMounted(load)
</script>
