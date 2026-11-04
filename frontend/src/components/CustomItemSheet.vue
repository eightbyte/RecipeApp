<template>
  <v-bottom-sheet v-model="model" max-width="600" :persistent="saving">
    <v-card>
      <v-card-title class="pt-6 px-6">{{ isEditing ? 'Edit item' : 'Add item' }}</v-card-title>

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
        <v-btn variant="text" :disabled="saving" @click="cancel">Cancel</v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :disabled="!form.name.trim()"
          :loading="saving"
          @click="confirm"
        >
          {{ isEditing ? 'Save' : 'Add' }}
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

const props = defineProps({
  item:   { type: Object, default: null },
  saving: { type: Boolean, default: false },
})

const isEditing = computed(() => props.item !== null)

const ingredientStore = useIngredientStore()

const categoryItems = INGREDIENT_CATEGORIES.map(({ title, value }) => ({ title, value }))

const defaultForm = () => ({ name: '', amount: null, unit: '', category: DEFAULT_CATEGORY })
const itemForm = item => ({ name: item.displayName, amount: item.amount, unit: item.unit ?? '', category: item.category })
const form = reactive(defaultForm())

watch(model, open => {
  if (!open) return
  Object.assign(form, isEditing.value ? itemForm(props.item) : defaultForm())
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

// Picking a suggestion yields its row and typing yields plain text, and only a pick while adding sets the Category so a rename never moves an item.
function updateName(value) {
  if (value && typeof value === 'object') {
    form.name = value.title
    if (!isEditing.value) form.category = value.category
  } else {
    form.name = value ?? ''
  }
}

const emit = defineEmits(['confirm', 'save'])

// An edit waits for the parent to close the sheet once the server accepts it, so a failed save keeps the edits.
function confirm() {
  if (!form.name.trim()) return
  const payload = {
    name:     form.name.trim(),
    amount:   form.amount || null,
    unit:     form.unit.trim() || null,
    category: form.category,
  }
  if (isEditing.value) {
    emit('save', payload)
    return
  }
  emit('confirm', payload)
  model.value = false
}

function cancel() {
  model.value = false
}
</script>
