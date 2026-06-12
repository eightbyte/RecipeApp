<template>
  <v-container class="pa-4" max-width="600">
    <h1 class="text-h6 font-weight-bold mb-4">
      {{ isEdit ? 'Edit Recipe' : 'New Recipe' }}
    </h1>

    <v-form ref="formRef" @submit.prevent="submit">

      <!-- ── Basic info ──────────────────────────────────────────────── -->
      <v-card class="mb-4 pa-4">
        <div class="text-subtitle-2 text-medium-emphasis mb-3">Basic Info</div>

        <v-text-field
          v-model="form.name"
          label="Recipe name"
          :rules="[v => !!v || 'Name is required']"
          required
          class="mb-2"
        />

        <v-textarea
          v-model="form.description"
          label="Description (optional)"
          rows="2"
          auto-grow
          class="mb-2"
        />

        <div class="d-flex gap-3">
          <v-text-field
            v-model.number="form.servings"
            label="Servings"
            type="number"
            min="1"
            max="100"
            :rules="[v => v > 0 || 'Must be at least 1']"
            style="max-width: 120px"
          />
          <v-text-field
            v-model="form.sourceUrl"
            label="Source URL (optional)"
            class="flex-grow-1"
          />
        </div>
      </v-card>

      <!-- ── Image ────────────────────────────────────────────────────── -->
      <v-card class="mb-4 pa-4">
        <div class="text-subtitle-2 text-medium-emphasis mb-3">Photo</div>

        <v-img
          v-if="imagePreview"
          :src="imagePreview"
          height="160"
          cover
          rounded="lg"
          class="mb-3"
        />

        <v-file-input
          v-model="imageFile"
          label="Recipe photo"
          accept="image/jpeg,image/png,image/webp"
          prepend-icon="mdi-camera"
          variant="outlined"
          density="comfortable"
          hide-details
          @update:model-value="onImageChange"
        />
      </v-card>

      <!-- ── Ingredients ────────────────────────────────────────────── -->
      <v-card class="mb-4 pa-4">
        <div class="d-flex align-center justify-space-between mb-3">
          <div class="text-subtitle-2 text-medium-emphasis">Ingredients</div>
          <v-btn
            size="small"
            variant="tonal"
            color="primary"
            prepend-icon="mdi-plus"
            @click="addIngredient"
          >
            Add
          </v-btn>
        </div>

        <div
          v-for="(ing, idx) in form.ingredients"
          :key="idx"
          class="d-flex gap-2 align-start mb-3"
        >
          <!-- Drag handle future: just a number for now -->
          <div class="text-caption text-medium-emphasis mt-4" style="min-width: 20px">
            {{ idx + 1 }}.
          </div>

          <div class="flex-grow-1">
            <!-- Ingredient search -->
            <v-autocomplete
              v-model="ing.ingredientId"
              :items="ingredientStore.ingredients"
              item-title="displayName"
              item-value="id"
              label="Ingredient"
              density="compact"
              hide-details
              class="mb-1"
              :loading="ingredientStore.loading"
              @update:search="onIngredientSearch"
              @update:model-value="onIngredientSelected(idx, $event)"
            >
              <template #no-data>
                <div class="pa-2 text-caption text-medium-emphasis">
                  No match — type to search or
                  <span
                    class="text-primary cursor-pointer"
                    @click="openCreateIngredient(idx)"
                  >create new</span>
                </div>
              </template>
            </v-autocomplete>

            <div class="d-flex gap-2">
              <v-text-field
                v-model.number="ing.amount"
                label="Amount"
                type="number"
                min="0"
                step="0.5"
                density="compact"
                hide-details
                style="max-width: 100px"
              />
              <v-select
                v-model="ing.unit"
                :items="units"
                label="Unit"
                density="compact"
                hide-details
                style="max-width: 100px"
              />
              <v-text-field
                v-model="ing.notes"
                label="Notes"
                density="compact"
                hide-details
                placeholder="e.g. diced"
                class="flex-grow-1"
              />
            </div>
          </div>

          <v-btn
            icon="mdi-close"
            size="small"
            variant="text"
            color="error"
            class="mt-1"
            @click="removeIngredient(idx)"
          />
        </div>

        <div v-if="!form.ingredients.length" class="text-caption text-medium-emphasis text-center py-2">
          Add at least one ingredient
        </div>
      </v-card>

      <!-- ── Steps ─────────────────────────────────────────────────── -->
      <v-card class="mb-4 pa-4">
        <div class="d-flex align-center justify-space-between mb-3">
          <div class="text-subtitle-2 text-medium-emphasis">Steps</div>
          <v-btn
            size="small"
            variant="tonal"
            color="primary"
            prepend-icon="mdi-plus"
            @click="addStep"
          >
            Add step
          </v-btn>
        </div>

        <div
          v-for="(step, sIdx) in form.steps"
          :key="sIdx"
          class="mb-4"
        >
          <div class="d-flex align-start gap-2">
            <v-avatar color="primary" size="28" class="text-white text-caption font-weight-bold mt-3">
              {{ sIdx + 1 }}
            </v-avatar>

            <div class="flex-grow-1">
              <v-textarea
                v-model="step.instruction"
                :label="`Step ${sIdx + 1} instruction`"
                rows="2"
                auto-grow
                density="compact"
                hide-details
                class="mb-2"
              />

              <!-- Ingredient chips for this step -->
              <div class="text-caption text-medium-emphasis mb-1">
                Ingredients used in this step:
              </div>
              <div class="d-flex flex-wrap gap-1">
                <v-chip
                  v-for="(ing, iIdx) in form.ingredients"
                  :key="iIdx"
                  :variant="step.ingredientIndexes.includes(iIdx) ? 'flat' : 'outlined'"
                  :color="step.ingredientIndexes.includes(iIdx) ? 'secondary' : 'default'"
                  size="x-small"
                  :disabled="!ing.ingredientId"
                  @click="toggleStepIngredient(sIdx, iIdx)"
                >
                  {{ ingredientLabel(ing) || `Ingredient ${iIdx + 1}` }}
                </v-chip>
                <span v-if="!form.ingredients.length" class="text-caption text-medium-emphasis">
                  Add ingredients above first
                </span>
              </div>
            </div>

            <v-btn
              icon="mdi-close"
              size="small"
              variant="text"
              color="error"
              @click="removeStep(sIdx)"
            />
          </div>
          <v-divider v-if="sIdx < form.steps.length - 1" class="mt-3" />
        </div>

        <div v-if="!form.steps.length" class="text-caption text-medium-emphasis text-center py-2">
          No steps yet — add your first step above
        </div>
      </v-card>

      <!-- ── Submit ──────────────────────────────────────────────────── -->
      <v-alert v-if="submitError" type="error" class="mb-4" closable @click:close="submitError = null">
        {{ submitError }}
      </v-alert>

      <div class="d-flex gap-3">
        <v-btn
          variant="outlined"
          :to="isEdit ? { name: 'recipe-detail', params: { id: route.params.id } } : { name: 'recipes' }"
        >
          Cancel
        </v-btn>
        <v-btn
          type="submit"
          color="primary"
          :loading="saving"
          class="flex-grow-1"
        >
          {{ isEdit ? 'Save changes' : 'Create recipe' }}
        </v-btn>
      </div>

    </v-form>

    <!-- ── Create ingredient dialog ──────────────────────────────────── -->
    <v-dialog v-model="createIngDialog" max-width="400">
      <v-card>
        <v-card-title>New Ingredient</v-card-title>
        <v-card-text>
          <v-text-field
            v-model="newIng.displayName"
            label="Name"
            class="mb-2"
          />
          <v-select
            v-model="newIng.category"
            :items="ingredientStore.categories"
            label="Category"
            class="mb-2"
          />
          <v-select
            v-model="newIng.defaultUnit"
            :items="units"
            label="Default unit"
          />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="createIngDialog = false">Cancel</v-btn>
          <v-btn color="primary" :loading="creatingIng" @click="confirmCreateIngredient">
            Create
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

  </v-container>
</template>

<script setup>
import { ref, reactive, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useRecipeStore }     from '@/stores/recipes'
import { useIngredientStore } from '@/stores/ingredients'

const props = defineProps({ id: String })
const route  = useRoute()
const router = useRouter()

const recipeStore     = useRecipeStore()
const ingredientStore = useIngredientStore()

const isEdit = computed(() => !!(props.id || route.params.id))

const units = ['g', 'kg', 'ml', 'L', 'pcs', 'tsp', 'tbsp']

// ── Form state ─────────────────────────────────────────────────────────────
const formRef    = ref(null)
const saving     = ref(false)
const submitError = ref(null)
const imageFile  = ref(null)
const imagePreview = ref(null)

const form = reactive({
  name:        '',
  description: '',
  servings:    4,
  sourceUrl:   '',
  ingredients: [],  // { ingredientId, amount, unit, notes }
  steps:       [],  // { instruction, ingredientIndexes: number[] }
})

// ── Create ingredient dialog ────────────────────────────────────────────────
const createIngDialog   = ref(false)
const creatingIng       = ref(false)
const pendingIngIdx     = ref(null)
const newIng = reactive({ displayName: '', category: 'OTHER', defaultUnit: 'g' })

// ── Load data ───────────────────────────────────────────────────────────────
onMounted(async () => {
  await ingredientStore.fetchIngredients()
  await ingredientStore.fetchCategories()

  if (isEdit.value) {
    const id = props.id ?? route.params.id
    const recipe = await recipeStore.fetchRecipe(id)
    if (recipe) populateForm(recipe)
  }
})

function populateForm(recipe) {
  form.name        = recipe.name
  form.description = recipe.description ?? ''
  form.servings    = recipe.servings
  form.sourceUrl   = recipe.sourceUrl ?? ''
  imagePreview.value = recipe.imageUrl ?? null

  const ingredients = recipe.ingredients ?? []
  const steps       = recipe.steps ?? []

  form.ingredients = ingredients.map(i => ({
    ingredientId: i.ingredientId,
    amount:       i.amount,
    unit:         i.unit,
    notes:        i.notes ?? '',
  }))

  form.steps = steps.map(s => ({
    instruction:      s.instruction,
    ingredientIndexes: (s.recipeIngredientIds ?? [])
      .map(riId => ingredients.findIndex(i => i.id === riId))
      .filter(idx => idx !== -1),
  }))
}

// ── Ingredient management ───────────────────────────────────────────────────
function addIngredient() {
  form.ingredients.push({ ingredientId: null, amount: 1, unit: 'g', notes: '' })
}

function removeIngredient(idx) {
  form.ingredients.splice(idx, 1)
  // Fix up step indexes
  form.steps.forEach(step => {
    step.ingredientIndexes = step.ingredientIndexes
      .filter(i => i !== idx)
      .map(i => (i > idx ? i - 1 : i))
  })
}

let ingSearchTimer = null
function onIngredientSearch(val) {
  clearTimeout(ingSearchTimer)
  ingSearchTimer = setTimeout(() =>
    ingredientStore.fetchIngredients(val ?? ''), 250)
}

function onIngredientSelected(idx, ingredientId) {
  const found = ingredientStore.ingredients.find(i => i.id === ingredientId)
  if (found?.defaultUnit && !form.ingredients[idx].unit)
    form.ingredients[idx].unit = found.defaultUnit
}

function ingredientLabel(ing) {
  const found = ingredientStore.ingredients.find(i => i.id === ing.ingredientId)
  return found?.displayName ?? ''
}

function openCreateIngredient(idx) {
  pendingIngIdx.value = idx
  newIng.displayName  = ''
  newIng.category     = 'OTHER'
  newIng.defaultUnit  = 'g'
  createIngDialog.value = true
}

async function confirmCreateIngredient() {
  creatingIng.value = true
  const name = newIng.displayName.trim().toLowerCase()
  const created = await ingredientStore.createIngredient({
    name,
    displayName: newIng.displayName.trim(),
    category:    newIng.category,
    defaultUnit: newIng.defaultUnit,
  })
  if (pendingIngIdx.value !== null) {
    form.ingredients[pendingIngIdx.value].ingredientId = created.id
    form.ingredients[pendingIngIdx.value].unit = created.defaultUnit ?? 'g'
  }
  creatingIng.value     = false
  createIngDialog.value = false
}

// ── Step management ─────────────────────────────────────────────────────────
function addStep() {
  form.steps.push({ instruction: '', ingredientIndexes: [] })
}

function removeStep(idx) {
  form.steps.splice(idx, 1)
}

function toggleStepIngredient(stepIdx, ingIdx) {
  const step    = form.steps[stepIdx]
  const pos     = step.ingredientIndexes.indexOf(ingIdx)
  if (pos === -1) step.ingredientIndexes.push(ingIdx)
  else             step.ingredientIndexes.splice(pos, 1)
}

// ── Image handling ──────────────────────────────────────────────────────────
function onImageChange(files) {
  const file = Array.isArray(files) ? files[0] : files
  if (!file) { imagePreview.value = null; return }
  const reader = new FileReader()
  reader.onload = e => { imagePreview.value = e.target.result }
  reader.readAsDataURL(file)
}

// ── Submit ──────────────────────────────────────────────────────────────────
async function submit() {
  const { valid } = await formRef.value.validate()
  if (!valid) return

  if (!form.ingredients.length) {
    submitError.value = 'Add at least one ingredient.'
    return
  }

  saving.value = true
  submitError.value = null

  try {
    const payload = {
      name:        form.name.trim(),
      description: form.description.trim() || null,
      sourceUrl:   form.sourceUrl.trim() || null,
      servings:    form.servings,
      ingredients: form.ingredients.map((ing, i) => ({
        ingredientId: ing.ingredientId,
        amount:       Number(ing.amount),
        unit:         ing.unit,
        notes:        ing.notes?.trim() || null,
        displayOrder: i,
      })),
      steps: form.steps.map((s, i) => ({
        stepNumber:       i + 1,
        instruction:      s.instruction.trim(),
        ingredientIndexes: s.ingredientIndexes,
      })),
    }

    let saved
    if (isEdit.value) {
      const id = props.id ?? route.params.id
      saved = await recipeStore.updateRecipe(id, payload)
    } else {
      saved = await recipeStore.createRecipe(payload)
    }

    // Upload image if a new file was selected
    if (imageFile.value) {
      const file = Array.isArray(imageFile.value) ? imageFile.value[0] : imageFile.value
      if (file) await recipeStore.uploadImage(saved.id, file)
    }

    router.push({ name: 'recipe-detail', params: { id: saved.id } })
  } catch (e) {
    submitError.value = e.message ?? 'Something went wrong. Please try again.'
  } finally {
    saving.value = false
  }
}
</script>
