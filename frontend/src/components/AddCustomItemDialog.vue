<template>
  <v-bottom-sheet v-model="model" max-width="600">
    <v-card>
      <v-card-title class="pt-4 px-4">Add item</v-card-title>

      <v-card-text class="px-4 pb-0">
        <v-text-field
          v-model="form.name"
          label="Item name"
          :rules="[v => !!v || 'Item name is required']"
          required
          variant="outlined"
          density="compact"
          class="mb-2"
          autofocus
        />

        <div class="d-flex gap-3 mb-2">
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

      <v-card-actions class="px-4 pb-4">
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
import { ref, reactive, watch } from 'vue'

const model = defineModel({ default: false })

const categoryItems = [
  { title: 'Produce',             value: 'PRODUCE'     },
  { title: 'Meat & Seafood',      value: 'MEAT_SEAFOOD' },
  { title: 'Dairy',               value: 'DAIRY'       },
  { title: 'Canned Goods',        value: 'CANNED'      },
  { title: 'Frozen',              value: 'FROZEN'      },
  { title: 'Dry Goods & Pasta',   value: 'DRY_GOODS'   },
  { title: 'Bakery',              value: 'BAKERY'      },
  { title: 'Condiments & Sauces', value: 'CONDIMENTS'  },
  { title: 'Beverages',           value: 'BEVERAGES'   },
  { title: 'Other',               value: 'OTHER'       },
]

const defaultForm = () => ({ name: '', amount: null, unit: '', category: 'OTHER' })
const form = reactive(defaultForm())

watch(model, open => {
  if (open) Object.assign(form, defaultForm())
})

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
