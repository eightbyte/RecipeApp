<template>
  <v-bottom-sheet v-model="model" max-width="600">
    <v-card>
      <v-card-title class="pt-6 px-6">Add item</v-card-title>

      <v-card-text class="px-6 pb-0">
        <v-combobox
          :model-value="form.name"
          :items="suggestions"
          :item-props="row => ({ subtitle: row.subtitle })"
          no-filter
          hide-no-data
          label="Item name"
          :rules="[v => !!v || 'Item name is required']"
          required
          variant="outlined"
          density="compact"
          class="mb-2"
          autofocus
          @update:model-value="updateName"
        />

        <div class="d-flex ga-4 mb-2">
          <v-text-field
            v-model.number="form.amount"
            label="Amount"
            type="number"
            min="0"
            variant="outlined"
            density="compact"
            style="flex: 1"
          />
          <v-text-field
            v-model="form.unit"
            label="Unit"
            variant="outlined"
            density="compact"
            style="flex: 1"
            placeholder="g, ml, pcs…"
          />
        </div>

        <v-select
          v-model="form.category"
          :items="categoryItems"
          label="Category"
          variant="outlined"
          density="compact"
        />
      </v-card-text>

      <v-card-actions class="px-6 pb-6">
        <v-spacer />
        <v-btn variant="text" @click="cancel">Cancel</v-btn>
        <v-btn color="primary" variant="flat" :disabled="!form.name.trim()" @click="confirm">
          Add
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-bottom-sheet>
</template>

<script setup>
import { computed, reactive, watch } from 'vue'
import { INGREDIENT_CATEGORIES, DEFAULT_CATEGORY, categoryTitle } from '@/constants/categories'
import { useIngredientStore } from '@/stores/ingredients'

const MIN_SEARCH_LENGTH = 2

const model = defineModel({ type: Boolean, default: false })

const ingredientStore = useIngredientStore()

const categoryItems = INGREDIENT_CATEGORIES.map(({ title, value }) => ({ title, value }))

const defaultForm = () => ({ name: '', amount: null, unit: '', category: DEFAULT_CATEGORY })
const form = reactive(defaultForm())

watch(model, open => {
  if (!open) return
  Object.assign(form, defaultForm())
  ingredientStore.fetchCatalogue()
}, { immediate: true })

const MatchTier = { EXACT: 0, STARTS_WITH: 1, CONTAINS: 2 }

function matchTier(title, search) {
  const text = title.toLowerCase()
  if (text === search) return MatchTier.EXACT
  if (text.startsWith(search)) return MatchTier.STARTS_WITH
  return text.includes(search) ? MatchTier.CONTAINS : null
}

const suggestions = computed(() => {
  const search = form.name.trim().toLowerCase()
  if (search.length < MIN_SEARCH_LENGTH) return []

  const rows = []
  for (const ingredient of ingredientStore.catalogue) {
    const categoryLabel = categoryTitle(ingredient.category)
    const candidates = [
      { title: ingredient.displayName, subtitle: categoryLabel, isAlias: false },
      ...ingredient.aliases.map(alias =>
        ({ title: alias, subtitle: `${ingredient.displayName} · ${categoryLabel}`, isAlias: true })),
    ]
    for (const candidate of candidates) {
      const tier = matchTier(candidate.title, search)
      if (tier !== null) rows.push({ ...candidate, category: ingredient.category, tier })
    }
  }

  return rows.sort((a, b) =>
    a.tier - b.tier ||
    a.isAlias - b.isAlias ||
    a.title.localeCompare(b.title, undefined, { sensitivity: 'base' }))
})

// Picking a suggestion yields its row, while typing yields plain text that leaves the Category alone.
function updateName(value) {
  if (value && typeof value === 'object') {
    form.name     = value.title
    form.category = value.category
  } else {
    form.name = value ?? ''
  }
}

const emit = defineEmits(['confirm'])

function confirm() {
  if (!form.name.trim()) return
  emit('confirm', {
    name:     form.name.trim(),
    amount:   form.amount || null,
    unit:     form.unit.trim() || null,
    category: form.category,
  })
  model.value = false
}

function cancel() {
  model.value = false
}
</script>
