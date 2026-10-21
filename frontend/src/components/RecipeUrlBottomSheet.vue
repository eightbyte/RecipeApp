<template>
  <v-bottom-sheet v-model="model" max-width="600">
    <v-card>
      <v-card-title class="text-subtitle-1 font-weight-bold pt-6 px-6">
        Import Recipe
      </v-card-title>

      <v-card-text class="px-6 pb-2">
        <v-text-field
          v-model="url"
          label="Recipe URL"
          type="url"
          placeholder="https://example.com/recipe"
          :disabled="store.scrapeLoading"
          :error-messages="store.scrapeError ? [store.scrapeError] : []"
          hide-details="auto"
          autofocus
          clearable
          class="mb-4"
          @keyup.enter="handleImport"
        />

        <v-progress-linear
          v-if="store.scrapeLoading"
          indeterminate
          color="primary"
          class="mb-2"
        />
        <p v-if="store.scrapeLoading" class="text-caption text-medium-emphasis text-center mb-2">
          Extracting recipe… this may take up to 30 seconds.
        </p>
      </v-card-text>

      <v-card-actions class="px-6 pb-6 ga-2">
        <v-btn variant="outlined" @click="handleDismiss">Cancel</v-btn>
        <v-btn
          color="primary"
          variant="flat"
          class="flex-grow-1"
          :disabled="!url || store.scrapeLoading"
          :loading="store.scrapeLoading"
          @click="handleImport"
        >
          Import
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-bottom-sheet>
</template>

<script setup>
import { ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useRecipeStore } from '@/stores/recipes'

const model = defineModel({ default: false })

const router = useRouter()
const store  = useRecipeStore()
const url    = ref('')

watch(model, (open) => {
  if (open) {
    url.value = ''
    store.clearScrapePreview()
  }
})

async function handleImport() {
  if (!url.value || store.scrapeLoading) return

  await store.scrapeRecipe(url.value.trim())

  if (!store.scrapeError) {
    model.value = false
    router.push({ name: 'scrape-preview' })
  }
}

function handleDismiss() {
  store.clearScrapePreview()
  model.value = false
}
</script>
