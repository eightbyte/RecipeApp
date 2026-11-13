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

        <v-btn-toggle
          v-model="source"
          mandatory
          divided
          density="compact"
          variant="outlined"
          color="primary"
          :disabled="store.scrapeLoading"
          class="mb-4"
        >
          <v-btn :value="SCRAPE_SOURCE_URL">URL</v-btn>
          <v-btn :value="SCRAPE_SOURCE_TEXT">Text</v-btn>
        </v-btn-toggle>

        <v-text-field
          v-if="source === SCRAPE_SOURCE_URL"
          v-model="url"
          label="Recipe URL"
          type="url"
          placeholder="https://example.com/recipe"
          :disabled="store.scrapeLoading"
          :error-messages="errorMessages"
          hide-details="auto"
          autofocus
          clearable
          class="mb-4"
          @keyup.enter="handleImport"
        />

        <v-textarea
          v-else
          v-model="text"
          label="Recipe text"
          placeholder="Paste a recipe: its name, ingredients and steps"
          :counter="SCRAPE_TEXT_MAX_LENGTH"
          persistent-counter
          rows="8"
          :disabled="store.scrapeLoading"
          :error-messages="errorMessages"
          hide-details="auto"
          autofocus
          class="mb-4"
        />

        <v-progress-linear
          v-if="store.scrapeLoading"
          indeterminate
          color="primary"
          class="mb-2"
        />
        <p v-if="store.scrapeLoading" class="text-caption text-medium-emphasis text-center mb-2">
          Extracting recipe… this may take up to a minute.
        </p>
      </v-card-text>

      <v-card-actions class="px-6 pb-6 ga-2">
        <v-btn variant="outlined" @click="handleDismiss">Cancel</v-btn>
        <v-btn
          color="primary"
          variant="flat"
          class="flex-grow-1"
          :disabled="!canImport"
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
import { SCRAPE_TEXT_MAX_LENGTH } from '@/constants/scrapeLimits'
import { SCRAPE_SOURCE_TEXT, SCRAPE_SOURCE_URL } from '@/constants/scrapeSources'

const model = defineModel({ type: Boolean, default: false })

const props = defineProps({
  /** The mode the sheet opens in each time, never the one it was last left in. */
  initialMode: {
    type: String,
    default: SCRAPE_SOURCE_URL,
    validator: (mode) => [SCRAPE_SOURCE_URL, SCRAPE_SOURCE_TEXT].includes(mode),
  },
})

const router = useRouter()
const store  = useRecipeStore()
const source = ref(props.initialMode)
const url    = ref('')
const text   = ref('')

const errorMessages = computed(() => (store.scrapeError ? [store.scrapeError] : []))

const hasInput = computed(() => (source.value === SCRAPE_SOURCE_URL
  ? !!url.value
  : !!text.value?.trim() && text.value.length <= SCRAPE_TEXT_MAX_LENGTH))

const canImport = computed(() => hasInput.value && !store.scrapeLoading && !unavailableMessage.value)

// The error belongs to the field it was shown under, so it goes when the other field takes its place.
watch(source, () => { store.scrapeError = null })

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
    source.value = props.initialMode
    url.value = ''
    text.value = ''
    store.clearScrapePreview()
    modelServerState.value = null
    checkModelServer()
  }
}, { immediate: true })

onBeforeUnmount(stopCheckingModelServer)

async function handleImport() {
  if (!canImport.value) return

  if (source.value === SCRAPE_SOURCE_URL) await store.scrapeRecipe(url.value.trim())
  else await store.scrapeRecipeText(text.value)

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
