<template>
  <div>
    <!-- Search controls -->
    <div class="d-flex gap-2 mb-3">
      <v-text-field
        v-model="searchQuery"
        label="Search recipes"
        prepend-inner-icon="mdi-magnify"
        variant="outlined"
        density="compact"
        clearable
        hide-details
        class="flex-grow-1"
        @update:model-value="onSearchChange"
      />
    </div>

    <div class="d-flex align-center gap-3 mb-3">
      <v-switch
        v-model="hideRecentlyCooked"
        label="Hide recently cooked"
        density="compact"
        hide-details
        color="primary"
        @update:model-value="onSearchChange"
      />
    </div>

    <!-- Recipe list -->
    <div v-if="recipesStore.loading" class="d-flex justify-center py-4">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <div v-else-if="recipesStore.recipes.length === 0" class="text-center py-6 text-medium-emphasis">
      No recipes found
    </div>

    <v-list v-else lines="two">
      <v-list-item
        v-for="recipe in recipesStore.recipes"
        :key="recipe.id"
        :ripple="true"
        class="rounded mb-1"
        @click="handleRecipeClick(recipe)"
      >
        <template #prepend>
          <v-avatar size="48" rounded="lg" color="grey-lighten-3" class="mr-3">
            <v-img
              v-if="recipe.imageUrl"
              :src="recipe.imageUrl"
              cover
              :class="{ grayscale: recipesStore.isRecentlyCooked(recipe) }"
            />
            <v-icon v-else color="grey">mdi-food</v-icon>
          </v-avatar>
        </template>

        <v-list-item-title class="font-weight-medium">{{ recipe.name }}</v-list-item-title>
        <v-list-item-subtitle v-if="recipesStore.isRecentlyCooked(recipe)" class="text-warning">
          Cooked recently
        </v-list-item-subtitle>

        <template #append>
          <v-btn icon="mdi-plus" variant="tonal" size="small" color="primary" @click.stop="handleRecipeClick(recipe)" />
        </template>
      </v-list-item>
    </v-list>

    <!-- Recently-cooked confirmation dialog -->
    <RecentlyCookedDialog
      v-model="recentlyCoookedDialogOpen"
      :recipe-name="pendingRecipe?.name ?? ''"
      @confirm="confirmAdd"
      @cancel="cancelAdd"
    />
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRecipeStore } from '@/stores/recipes'
import RecentlyCookedDialog from '@/components/RecentlyCookedDialog.vue'

const emit = defineEmits(['add'])

const recipesStore = useRecipeStore()

const searchQuery           = ref('')
const hideRecentlyCooked    = ref(false)
const recentlyCoookedDialogOpen = ref(false)
const pendingRecipe         = ref(null)

onMounted(() => loadRecipes())

function onSearchChange() {
  loadRecipes()
}

async function loadRecipes() {
  const params = {}
  if (searchQuery.value) params.search = searchQuery.value
  if (hideRecentlyCooked.value) params.excludeRecentDays = 7
  await recipesStore.fetchRecipes(params)
}

function handleRecipeClick(recipe) {
  if (recipesStore.isRecentlyCooked(recipe)) {
    pendingRecipe.value = recipe
    recentlyCoookedDialogOpen.value = true
  } else {
    emit('add', recipe)
  }
}

function confirmAdd() {
  recentlyCoookedDialogOpen.value = false
  emit('add', pendingRecipe.value)
  pendingRecipe.value = null
}

function cancelAdd() {
  recentlyCoookedDialogOpen.value = false
  pendingRecipe.value = null
}
</script>
