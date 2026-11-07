<template>
  <v-bottom-sheet v-model="model" max-width="600">
    <v-card>
      <v-card-title class="text-subtitle-1 font-weight-bold pt-6 px-6">
        Import Recipe
      </v-card-title>

      <v-card-text class="px-6 pb-2">
        <v-alert
          v-if="unavailableMessage"
          type="info"
          variant="tonal"
          density="compact"
          class="mb-4"
        >
          {{ unavailableMessage }}
        </v-alert>

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
          :disabled="!url || store.scrapeLoading || !!unavailableMessage"
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
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useRecipeStore } from '@/stores/recipes'
import {
  MODEL_SERVER_LOADING,
  MODEL_SERVER_LOADING_RECHECK_MS,
  MODEL_SERVER_UNAVAILABLE_MESSAGES,
} from '@/constants/modelServer'

const model = defineModel({ type: Boolean, default: false })

const router = useRouter()
const store  = useRecipeStore()
const url    = ref('')

// Null until the status answers, and when the status request itself fails, so the Scrape can report the real error.
const modelServerState   = ref(null)
const unavailableMessage = computed(() => MODEL_SERVER_UNAVAILABLE_MESSAGES[modelServerState.value] ?? null)

let recheckTimer = null
// Bumped on close and on every new check, so an answer that arrives late cannot overwrite a newer one.
let statusCheck = 0

async function checkModelServer() {
  const check = ++statusCheck
  let state
  try {
    ({ state } = await store.fetchModelServerStatus())
  } catch {
    state = null
  }
  if (check !== statusCheck) return

  modelServerState.value = state
  if (state === MODEL_SERVER_LOADING) recheckTimer = setTimeout(checkModelServer, MODEL_SERVER_LOADING_RECHECK_MS)
}

function stopCheckingModelServer() {
  clearTimeout(recheckTimer)
  recheckTimer = null
  statusCheck++
}

watch(model, (open) => {
  stopCheckingModelServer()
  if (open) {
    url.value = ''
    store.clearScrapePreview()
    modelServerState.value = null
    checkModelServer()
  }
}, { immediate: true })

onBeforeUnmount(stopCheckingModelServer)

async function handleImport() {
  if (!url.value || store.scrapeLoading || unavailableMessage.value) return

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
