<template>
  <v-container class="pa-4" max-width="600">

    <h1 class="d-sr-only">Shopping list</h1>

    <!-- ── Loading skeleton ───────────────────────────────────────────────── -->
    <template v-if="store.loading && !store.list">
      <v-skeleton-loader type="list-item-two-line" class="mb-2" />
      <v-skeleton-loader type="list-item-two-line" class="mb-2" />
      <v-skeleton-loader type="list-item-two-line" class="mb-2" />
    </template>

    <!-- ── Fetch error ──────────────────────────────────────────────────── -->
    <ErrorState
      v-else-if="store.error && !store.list"
      :message="store.error"
      @retry="store.fetchActive()"
    />

    <template v-else>
      <!-- ── Stale banner ────────────────────────────────────────────────── -->
      <v-alert
        v-if="store.list?.isStale"
        type="warning"
        variant="tonal"
        class="mb-6"
        closable
      >
        <div class="d-flex align-center justify-space-between flex-wrap ga-2">
          <span>Your meal plan has changed — tap to update your shopping list.</span>
          <v-btn
            size="small"
            variant="flat"
            color="warning"
            :loading="store.loading"
            @click="onRegenerate"
          >
            Regenerate
          </v-btn>
        </div>
      </v-alert>

      <!-- ── Toolbar ─────────────────────────────────────────────────────── -->
      <div v-if="store.list" class="d-flex align-center justify-space-between mb-6">
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
        <div v-for="group in visibleGroups" :key="group.category" class="mb-6">
          <div class="text-overline text-medium-emphasis mb-2 px-1">
            {{ formatCategory(group.category) }}
          </div>

          <v-card>
            <v-list density="compact">
              <template v-for="(item, idx) in group.items" :key="item.id">
                <v-list-item
                  class="shopping-row"
                  :class="{ 'opacity-50': item.isChecked }"
                  v-on="item.isCustom ? customRowListeners(item) : {}"
                >
                  <template #prepend>
                    <v-checkbox-btn
                      class="purchased-checkbox"
                      :model-value="item.isChecked"
                      color="primary"
                      :aria-label="`Mark ${item.displayName} purchased`"
                      @pointerdown.stop
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
                    <div class="d-flex align-center ga-1">
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
                        :aria-label="`Remove ${item.displayName}`"
                        @pointerdown.stop
                        @click.stop="onDeleteItem(item)"
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
      <EmptyState
        v-else-if="store.list"
        icon="mdi-cart-outline"
        title="Shopping list is empty"
        text="Add recipes to your meal plan to populate the list."
      />

      <!-- ── Empty state — no active plan ──────────────────────────────── -->
      <EmptyState
        v-else
        icon="mdi-cart-outline"
        title="Shopping list is empty"
        text="Create a meal plan to generate your list"
        action-label="Go to meal plan"
        :action-to="{ name: 'meal-plan' }"
      />
    </template>

    <!-- ── FAB — add custom item ────────────────────────────────────────── -->
    <v-btn
      v-if="store.list"
      class="fab-above-nav"
      color="primary"
      icon="mdi-plus"
      size="large"
      aria-label="Add custom item"
      style="position: fixed; right: 16px"
      elevation="4"
      @click="openSheet(null)"
    />

    <!-- ── Add or edit custom item sheet ─────────────────────────────────── -->
    <CustomItemSheet
      v-model="sheetOpen"
      :item="editingItem"
      :saving="saving"
      @confirm="onAddCustomItem"
      @save="onSaveCustomItem"
    />

  </v-container>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useShoppingListStore } from '@/stores/shoppingList'
import { useUiStore } from '@/stores/ui'
import { formatCategory, INGREDIENT_CATEGORY_VALUES } from '@/constants/categories'
import { useHold } from '@/composables/useHold'
import CustomItemSheet from '@/components/CustomItemSheet.vue'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'

const store       = useShoppingListStore()
const ui          = useUiStore()
const showChecked = ref(false)
const sheetOpen   = ref(false)
const editingItem = ref(null)
const saving      = ref(false)

const hold = useHold(openSheet)

function openSheet(item) {
  editingItem.value = item
  sheetOpen.value   = true
}

function customRowListeners(item) {
  return { ...hold.listeners(item), contextmenu: event => onCustomRowContextMenu(event, item) }
}

// Android Chrome fires a touch contextmenu at about 500 ms, so only a mouse right-click opens the editor at once.
function onCustomRowContextMenu(event, item) {
  event.preventDefault()
  if (event.pointerType === 'mouse') openSheet(item)
}

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
  // Groups follow Aisle Order rather than item order, because the server numbers a lone Custom Item 1.
  return Object.entries(grouped)
    .map(([category, groupItems]) => ({ category, items: groupItems }))
    .sort((a, b) => aisleIndex(a.category) - aisleIndex(b.category))
})

function aisleIndex(category) {
  const index = INGREDIENT_CATEGORY_VALUES.indexOf(category)
  return index === -1 ? INGREDIENT_CATEGORY_VALUES.length : index
}

async function onAddCustomItem(payload) {
  try {
    await store.addCustomItem(payload)
    ui.notify({ message: `Added “${payload.displayName ?? payload.name ?? 'item'}”`, color: 'success' })
  } catch (e) {
    ui.notify({ message: e?.message ?? 'Could not add item.', color: 'error' })
  }
}

async function onSaveCustomItem(payload) {
  saving.value = true
  try {
    await store.updateCustomItem(editingItem.value.id, payload)
    sheetOpen.value = false
    ui.notify({ message: `Saved “${payload.name}”`, color: 'success' })
  } catch (e) {
    const message = e?.timedOut
      ? `Saving “${editingItem.value.displayName}” timed out. Try again.`
      : e?.message ?? 'Could not save item.'
    ui.notify({ message, color: 'error' })
  } finally {
    saving.value = false
  }
}

async function onDeleteItem(item) {
  try {
    await store.deleteItem(item.id)
    ui.notify({ message: `Removed “${item.displayName}”`, color: 'success' })
  } catch (e) {
    ui.notify({ message: e?.message ?? 'Could not remove item.', color: 'error' })
  }
}

async function onRegenerate() {
  try {
    await store.regenerate()
    ui.notify({ message: 'Shopping list updated', color: 'success' })
  } catch (e) {
    ui.notify({ message: e?.message ?? 'Could not regenerate list.', color: 'error' })
  }
}

function formatAmount(amount, unit) {
  return `${amount} ${unit}`
}
</script>

<style scoped>
/* The checkbox is the only control for Purchased, so it gets the 48px minimum touch target. */
.purchased-checkbox {
  --v-selection-control-size: 48px;
}

/* A held row would otherwise select its text or raise the browser's long-press menu. */
.shopping-row {
  user-select: none;
  -webkit-user-select: none;
  -webkit-touch-callout: none;
}
</style>
