<template>
  <v-container class="pa-4" max-width="600">
    <div v-if="loading" class="d-flex justify-center py-12">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <EmptyState v-else-if="notFound" icon="mdi-food-apple-outline" title="Ingredient not found" />

    <ErrorState v-else-if="loadError" :message="loadError" @retry="load" />

    <v-form v-else ref="formRef" @submit.prevent="save">
      <v-alert v-if="isRemoved" type="warning" variant="tonal" density="compact" class="mb-4">
        This ingredient is removed. Recipes and shopping lists that use it still show it, but nothing
        new will match it.
      </v-alert>

      <v-text-field
        v-model="form.displayName"
        data-test="display-name"
        label="Name"
        :rules="[required, maxLength]"
        :counter="INGREDIENT_NAME_MAX_LENGTH"
        class="mb-2"
      />

      <v-select
        v-model="form.category"
        data-test="category"
        :items="INGREDIENT_CATEGORIES"
        label="Category"
        :hint="isEdit ? 'Recipes show a new Category at once. Shopping lists keep the old one until regenerated.' : ''"
        :persistent-hint="isEdit"
        class="mb-4"
      />

      <v-select
        v-model="form.defaultUnit"
        data-test="default-unit"
        :items="MEASUREMENT_UNITS"
        label="Default unit"
        clearable
        class="mb-2"
      />

      <v-combobox
        :model-value="form.aliases"
        data-test="aliases"
        label="Aliases"
        hint="Other names a Scrape or Import should match to this ingredient."
        persistent-hint
        multiple
        chips
        closable-chips
        :rules="[aliasesFit]"
        class="mb-4"
        @update:model-value="setAliases"
      />

      <v-text-field
        v-model="form.density"
        data-test="density"
        label="Density (g/ml)"
        type="number"
        inputmode="decimal"
        :min="MIN_DENSITY"
        :max="MAX_DENSITY"
        step="0.01"
        :rules="[densityInRange]"
        hint="Leave empty when there's no reliable density: cups then stay as cups. Recipes already saved keep their amounts; a change applies to future Scrapes and Imports."
        persistent-hint
        class="mb-4"
      />

      <v-switch
        v-model="form.isStaple"
        data-test="staple"
        label="Staple (always on hand)"
        color="primary"
        hide-details
        class="mb-4"
      />

      <v-alert v-if="submitError" type="error" variant="tonal" density="compact" class="mb-4">
        {{ submitError }}
      </v-alert>

      <div class="d-flex ga-2">
        <template v-if="isEdit">
          <v-btn v-if="isRemoved" variant="tonal" :loading="restoring" @click="restore">Restore</v-btn>
          <v-btn v-else color="error" variant="text" :loading="checkingUsage" @click="confirmRemove">Remove</v-btn>
        </template>
        <v-spacer />
        <v-btn color="primary" type="submit" :loading="saving">Save</v-btn>
      </div>
    </v-form>

    <v-dialog v-model="removeDialog" max-width="400">
      <v-card :title="`Remove ${original?.displayName}?`">
        <v-card-text>
          <p class="mb-2">{{ usageText }}</p>
          <p class="text-medium-emphasis">
            Scrapes and Imports will stop matching it, and its name and aliases become free for
            another ingredient. You can restore it later.
          </p>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="removeDialog = false">Cancel</v-btn>
          <v-btn color="error" variant="text" :loading="removing" @click="remove">Remove</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<script setup>
import { ref, reactive, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useIngredientStore } from '@/stores/ingredients'
import { useUiStore } from '@/stores/ui'
import { INGREDIENT_CATEGORIES, DEFAULT_CATEGORY } from '@/constants/categories'
import { MEASUREMENT_UNITS } from '@/constants/units'
import { INGREDIENT_NAME_MAX_LENGTH, MIN_DENSITY, MAX_DENSITY } from '@/constants/ingredientLimits'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'

const props = defineProps({
  id: { type: String, default: null },
})

const router          = useRouter()
const ingredientStore = useIngredientStore()
const ui              = useUiStore()

const isEdit = computed(() => !!props.id)

const formRef       = ref(null)
const original      = ref(null)
const loading       = ref(false)
const loadError     = ref(null)
const notFound      = ref(false)
const saving        = ref(false)
const submitError   = ref(null)
const restoring     = ref(false)
const checkingUsage = ref(false)
const removing      = ref(false)
const removeDialog  = ref(false)
const recipeCount   = ref(0)

const form = reactive({
  displayName: '',
  category:    DEFAULT_CATEGORY,
  defaultUnit: null,
  aliases:     [],
  density:     '',
  isStaple:    false,
})

const isRemoved = computed(() => !!original.value?.removedAt)

/** The form the API stores a name or alias in, so the form compares names as matching does. */
const normalise = (name) => (name ?? '').trim().toLowerCase()

// ── Rules ─────────────────────────────────────────────────────────────────────

const required = (value) => !!normalise(value) || 'Name is required'
const maxLength = (value) =>
  (value ?? '').trim().length <= INGREDIENT_NAME_MAX_LENGTH || `At most ${INGREDIENT_NAME_MAX_LENGTH} characters`
const aliasesFit = (aliases) =>
  aliases.every(alias => alias.length <= INGREDIENT_NAME_MAX_LENGTH) || `Each alias is at most ${INGREDIENT_NAME_MAX_LENGTH} characters`

function densityOf(value) {
  return value === '' || value === null || value === undefined ? null : Number(value)
}
const densityInRange = (value) => {
  const density = densityOf(value)
  return density === null || (density >= MIN_DENSITY && density <= MAX_DENSITY)
    || `Between ${MIN_DENSITY} and ${MAX_DENSITY} g/ml, or empty`
}

// ── Aliases, and the old name offered on rename ───────────────────────────────

// Whether the old name is in the aliases because a rename put it there, and whether the user took it out again.
const offeredOldName  = ref(false)
const declinedOldName = ref(false)

function setAliases(aliases) {
  form.aliases = [...new Set(aliases.map(normalise).filter(Boolean))]
  if (offeredOldName.value && !form.aliases.includes(original.value.name)) {
    offeredOldName.value  = false
    declinedOldName.value = true
  }
}

watch(() => form.displayName, (displayName) => {
  if (!original.value) return
  const oldName = original.value.name
  const renamed = normalise(displayName) !== '' && normalise(displayName) !== oldName

  if (renamed && !offeredOldName.value && !declinedOldName.value && !form.aliases.includes(oldName)) {
    form.aliases = [...form.aliases, oldName]
    offeredOldName.value = true
  } else if (!renamed && offeredOldName.value) {
    form.aliases = form.aliases.filter(alias => alias !== oldName)
    offeredOldName.value = false
  }
})

// ── Load ──────────────────────────────────────────────────────────────────────

function fill(ingredient) {
  original.value   = ingredient
  form.displayName = ingredient.displayName
  form.category    = ingredient.category
  form.defaultUnit = ingredient.defaultUnit
  form.aliases     = [...ingredient.aliases]
  form.density     = ingredient.gramsPerMillilitre ?? ''
  form.isStaple    = ingredient.isStaple
}

async function load() {
  if (!isEdit.value) return
  loading.value   = true
  loadError.value = null
  try {
    fill(await ingredientStore.fetchIngredient(props.id))
  } catch (e) {
    if (e.status === 404) notFound.value = true
    else loadError.value = e.message
  }
  loading.value = false
}

onMounted(load)

// ── Save, remove, restore ─────────────────────────────────────────────────────

function payload() {
  return {
    displayName:        form.displayName.trim(),
    category:           form.category,
    defaultUnit:        form.defaultUnit,
    aliases:            form.aliases,
    gramsPerMillilitre: densityOf(form.density),
    isStaple:           form.isStaple,
  }
}

/** Back when the list is the page before, so history holds one copy of it, and otherwise in place of the form. */
function returnToList() {
  const back = router.options.history.state.back
  if (back && router.resolve(back).name === 'admin-ingredients') router.back()
  else router.replace({ name: 'admin-ingredients' })
}

async function save() {
  const { valid } = await formRef.value.validate()
  if (!valid) return

  saving.value      = true
  submitError.value = null
  try {
    const saved = isEdit.value
      ? await ingredientStore.updateIngredient(props.id, payload())
      : await ingredientStore.createIngredient(payload())
    ui.notify({ message: `${saved.displayName} saved` })
    returnToList()
  } catch (e) {
    submitError.value = e.message
  }
  saving.value = false
}

const usageText = computed(() => {
  if (recipeCount.value === 0) return 'Not used in any recipes.'
  if (recipeCount.value === 1) return "Used in 1 recipe — it'll keep showing it."
  return `Used in ${recipeCount.value} recipes — they'll keep showing it.`
})

async function confirmRemove() {
  checkingUsage.value = true
  submitError.value   = null
  try {
    recipeCount.value  = await ingredientStore.fetchUsage(props.id)
    removeDialog.value = true
  } catch (e) {
    submitError.value = e.message
  }
  checkingUsage.value = false
}

async function remove() {
  removing.value = true
  try {
    await ingredientStore.removeIngredient(props.id)
    removeDialog.value = false
    ui.notify({ message: `${original.value.displayName} removed` })
    returnToList()
  } catch (e) {
    removeDialog.value = false
    submitError.value  = e.message
  }
  removing.value = false
}

async function restore() {
  restoring.value   = true
  submitError.value = null
  try {
    original.value = await ingredientStore.restoreIngredient(props.id)
    ui.notify({ message: `${original.value.displayName} restored` })
  } catch (e) {
    submitError.value = e.message
  }
  restoring.value = false
}
</script>
