<template>
  <div>
    <h3 class="text-subtitle-1 font-weight-bold mb-2">Uses up your leftovers</h3>

    <div v-if="suggestions.length === 0" class="text-body-2 text-medium-emphasis py-2">
      Add more recipes to see suggestions
    </div>

    <v-card
      v-for="suggestion in suggestions"
      :key="suggestion.recipeId"
      class="mb-2"
      :ripple="true"
      @click="handleAdd(suggestion)"
    >
      <div class="d-flex align-center pa-3 gap-3">
        <v-avatar size="48" rounded="lg" color="grey-lighten-3">
          <v-img
            v-if="suggestion.recipeImageUrl"
            :src="suggestion.recipeImageUrl"
            cover
            :class="{ grayscale: isRecentlyCooked(suggestion) }"
          />
          <v-icon v-else color="grey">mdi-food</v-icon>
        </v-avatar>

        <div class="flex-grow-1">
          <div class="text-body-2 font-weight-medium">{{ suggestion.recipeName }}</div>
          <div class="text-caption text-medium-emphasis">
            {{ ingredientLabel(suggestion) }}
          </div>
        </div>

        <v-btn icon="mdi-plus" variant="text" size="small" color="primary" @click.stop="handleAdd(suggestion)" />
      </div>
    </v-card>

    <RecentlyCookedDialog
      v-model="dialogOpen"
      :recipe-name="pendingSuggestion?.recipeName ?? ''"
      @confirm="confirmAdd"
      @cancel="dialogOpen = false"
    />
  </div>
</template>

<script setup>
import { ref } from 'vue'
import RecentlyCookedDialog from '@/components/RecentlyCookedDialog.vue'

const props = defineProps({
  suggestions: { type: Array, default: () => [] },
})

const emit = defineEmits(['add'])

const dialogOpen       = ref(false)
const pendingSuggestion = ref(null)

function isRecentlyCooked(suggestion) {
  if (!suggestion.recipeLastCookedAt) return false
  const cutoff = Date.now() - 7 * 24 * 60 * 60 * 1000
  return new Date(suggestion.recipeLastCookedAt).getTime() > cutoff
}

function ingredientLabel(suggestion) {
  if (!suggestion.overlappingIngredients?.length) return 'Uses ingredients already in your plan'
  return `Uses your leftover ${suggestion.overlappingIngredients.join(' & ')}`
}

function handleAdd(suggestion) {
  if (isRecentlyCooked(suggestion)) {
    pendingSuggestion.value = suggestion
    dialogOpen.value = true
  } else {
    emit('add', suggestion)
  }
}

function confirmAdd() {
  dialogOpen.value = false
  emit('add', pendingSuggestion.value)
  pendingSuggestion.value = null
}
</script>
