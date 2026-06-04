<template>
  <v-container class="pa-4" max-width="600">

    <!-- ── Loading skeleton ───────────────────────────────────────────────── -->
    <template v-if="store.loading && !store.list">
      <v-skeleton-loader type="list-item-two-line" class="mb-2" />
      <v-skeleton-loader type="list-item-two-line" class="mb-2" />
      <v-skeleton-loader type="list-item-two-line" class="mb-2" />
    </template>

    <template v-else>
      <!-- ── Stale banner ────────────────────────────────────────────────── -->
      <v-alert
        v-if="store.list?.isStale"
        type="warning"
        variant="tonal"
        class="mb-4"
        closable
      >
        <div class="d-flex align-center justify-space-between flex-wrap gap-2">
          <span>Your meal plan has changed — tap to update your shopping list.</span>
          <v-btn
            size="small"
            variant="flat"
            color="warning"
            :loading="store.loading"
            @click="store.regenerate()"
          >
            Regenerate
          </v-btn>
        </div>
      </v-alert>

      <!-- ── Toolbar ─────────────────────────────────────────────────────── -->
      <div v-if="store.list" class="d-flex align-center justify-space-between mb-4">
        <div class="text-body-2 text-medium-emphasis">
          {{ uncheckedCount }} item{{ uncheckedCount !== 1 ? 's' : '' }} remaining
        </div>
        <v-btn
          variant="text"
          size="small"
          :prepend-icon="showChecked ? 'mdi-eye-off-outline' : 'mdi-eye-outline'"
          @click="showChecked = !showChecked"
        >
          {{ showChecked ? 'Hide' : 'Show' }} purchased ({{ checkedCount }})
        </v-btn>
      </div>

      <!-- ── Grouped list ────────────────────────────────────────────────── -->
      <div v-if="visibleGroups.length">
        <div v-for="group in visibleGroups" :key="group.category" class="mb-4">
          <div class="text-overline text-medium-emphasis mb-2 px-1">
            {{ categoryLabel(group.category) }}
          </div>

          <v-card>
            <v-list density="compact">
              <template v-for="(item, idx) in group.items" :key="item.id">
                <v-list-item
                  :class="{ 'opacity-50': item.isChecked }"
                  @click="store.toggleItem(item.id, !item.isChecked)"
                >
                  <template #prepend>
                    <v-checkbox-btn
                      :model-value="item.isChecked"
                      color="primary"
                      @click.stop="store.toggleItem(item.id, !item.isChecked)"
                    />
                  </template>

                  <v-list-item-title
                    :class="{ 'text-decoration-line-through': item.isChecked }"
                  >
                    {{ item.displayName }}
                  </v-list-item-title>

                  <v-list-item-subtitle v-if="item.amount">
                    {{ formatAmount(item.amount, item.unit) }}
                  </v-list-item-subtitle>

                  <template #append>
                    <div class="d-flex align-center gap-1">
                      <!-- Review flag -->
                      <v-tooltip
                        v-if="item.needsReview"
                        text="Units couldn't be combined — check this item"
                      >
                        <template #activator="{ props }">
                          <v-icon
                            v-bind="props"
                            size="16"
                            color="warning"
                          >
                            mdi-alert-outline
                          </v-icon>
                        </template>
                      </v-tooltip>

                      <!-- Custom item badge -->
                      <v-icon
                        v-if="item.isCustom"
                        size="14"
                        color="secondary"
                        title="Custom item"
                      >
                        mdi-account-outline
                      </v-icon>

                      <!-- Delete custom item -->
                      <v-btn
                        v-if="item.isCustom"
                        icon="mdi-close"
                        size="x-small"
                        variant="text"
                        color="error"
                        @click.stop="store.deleteItem(item.id)"
                      />
                    </div>
                  </template>
                </v-list-item>
                <v-divider v-if="idx < group.items.length - 1" />
              </template>
            </v-list>
          </v-card>
        </div>
      </div>

      <!-- ── Empty state — has list but no items ────────────────────────── -->
      <div v-else-if="store.list" class="text-center py-12">
        <v-icon size="64" color="grey-lighten-2">mdi-cart-outline</v-icon>
        <div class="text-h6 mt-3 mb-1">Shopping list is empty</div>
        <div class="text-body-2 text-medium-emphasis">
          Add recipes to your meal plan to populate the list.
        </div>
      </div>

      <!-- ── Empty state — no active plan ──────────────────────────────── -->
      <div v-else class="text-center py-12">
        <v-icon size="64" color="grey-lighten-2">mdi-cart-outline</v-icon>
        <div class="text-h6 mt-3 mb-1">Shopping list is empty</div>
        <div class="text-body-2 text-medium-emphasis mb-4">
          Create a meal plan to generate your list
        </div>
        <v-btn color="primary" :to="{ name: 'meal-plan' }">
          Go to meal plan
        </v-btn>
      </div>
    </template>

    <!-- ── FAB — add custom item ────────────────────────────────────────── -->
    <v-btn
      v-if="store.list"
      color="primary"
      icon="mdi-plus"
      size="large"
      style="position: fixed; bottom: 80px; right: 16px"
      elevation="4"
      @click="addItemDialog = true"
    />

    <!-- ── Add custom item dialog ────────────────────────────────────────── -->
    <AddCustomItemDialog
      v-model="addItemDialog"
      @confirm="onAddCustomItem"
    />

  </v-container>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useShoppingListStore } from '@/stores/shoppingList'
import AddCustomItemDialog from '@/components/AddCustomItemDialog.vue'

const store         = useShoppingListStore()
const showChecked   = ref(false)
const addItemDialog = ref(false)

onMounted(() => store.fetchActive())

const items = computed(() => store.list?.items ?? [])

const checkedCount   = computed(() => items.value.filter(i => i.isChecked).length)
const uncheckedCount = computed(() => items.value.filter(i => !i.isChecked).length)

const visibleGroups = computed(() => {
  const filtered = showChecked.value
    ? items.value
    : items.value.filter(i => !i.isChecked)

  const grouped = {}
  for (const item of filtered) {
    if (!grouped[item.category]) grouped[item.category] = []
    grouped[item.category].push(item)
  }
  return Object.entries(grouped).map(([category, groupItems]) => ({
    category,
    items: groupItems,
  }))
})

async function onAddCustomItem(payload) {
  await store.addCustomItem(payload)
}

function formatAmount(amount, unit) {
  return `${amount} ${unit}`
}

const categoryLabels = {
  PRODUCE:      '🥦 Produce',
  MEAT_SEAFOOD: '🥩 Meat & Seafood',
  DAIRY:        '🥛 Dairy',
  CANNED:       '🥫 Canned Goods',
  FROZEN:       '🧊 Frozen',
  DRY_GOODS:    '🌾 Dry Goods & Pasta',
  BAKERY:       '🍞 Bakery',
  CONDIMENTS:   '🫙 Condiments & Sauces',
  BEVERAGES:    '🧃 Beverages',
  OTHER:        '📦 Other',
}

function categoryLabel(cat) {
  return categoryLabels[cat] ?? cat
}
</script>
