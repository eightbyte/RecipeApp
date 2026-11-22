<template>
  <v-container class="pa-4" max-width="600">

    <h1 class="d-sr-only">Shopping list</h1>

    <!-- ── Offline banner ─────────────────────────────────────────────────── -->
    <!-- Only offline, which lasts: a banner for every quick online save would push the list down and back. -->
    <v-alert
      v-if="offlineBanner"
      data-test="pending-banner"
      type="warning"
      icon="mdi-cloud-off-outline"
      variant="tonal"
      density="compact"
      class="mb-4"
    >
      {{ offlineBanner }}
    </v-alert>

    <!-- The browser's own pull to refresh is off app-wide (main.css), so this one can't reload the page. -->
    <v-pull-to-refresh @load="onPull">

      <!-- ── Loading skeleton ───────────────────────────────────────────────── -->
      <template v-if="store.loading && !store.list">
        <v-skeleton-loader type="list-item-two-line" class="mb-2" />
        <v-skeleton-loader type="list-item-two-line" class="mb-2" />
        <v-skeleton-loader type="list-item-two-line" class="mb-2" />
      </template>

      <!-- ── Offline before this phone has ever saved the list ──────────────── -->
      <EmptyState
        v-else-if="store.offline && !store.list"
        icon="mdi-cloud-off-outline"
        title="Shopping list unavailable"
        text="You’re offline, and this list hasn’t been saved on this phone yet."
      />

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
              :disabled="!canRegenerate"
              @click="onRegenerate"
            >
              Regenerate
            </v-btn>
          </div>
        </v-alert>

        <!-- ── Toolbar ─────────────────────────────────────────────────────── -->
        <div v-if="store.list" class="d-flex align-center justify-space-between mb-6">
          <!-- The saving notice shares this line and never wraps, so the list below it stays put. -->
          <div class="text-body-2 text-medium-emphasis text-no-wrap">
            {{ uncheckedCount }} item{{ uncheckedCount !== 1 ? 's' : '' }} remaining<span
              v-if="isSaving"
              data-test="saving-status"
            > · Saving…</span>
          </div>
          <v-btn
            variant="text"
            size="small"
            :prepend-icon="showChecked ? 'mdi-eye-off-outline' : 'mdi-eye-outline'"
            @click="onToggleShowPurchased"
          >
            {{ showChecked ? 'Hide' : 'Show' }} purchased ({{ checkedCount }})
          </v-btn>
        </div>

        <!-- ── Grouped list ────────────────────────────────────────────────── -->
        <!-- Rows and groups a List Refresh removes collapse rather than jump, so the rows below slide up. -->
        <div v-if="visibleGroups.length">
          <v-expand-transition group>
            <!-- The spacing sits inside the collapsing element, because the transition animates height alone. -->
            <div v-for="group in visibleGroups" :key="group.category">
              <div class="pb-6">
                <div class="text-overline text-medium-emphasis mb-2 px-1">
                  {{ formatCategory(group.category) }}
                </div>

                <v-card>
                  <v-list density="compact">
                    <v-expand-transition group>
                      <div v-for="(item, idx) in group.items" :key="item.id">
                        <!-- Above its own row, so a collapsing last row takes its divider with it. -->
                        <v-divider v-if="idx > 0" />
                        <v-list-item
                          class="shopping-row"
                          :class="{ 'opacity-50': item.isChecked }"
                          :aria-expanded="hasSources(item) ? isExpanded(item) : undefined"
                          v-on="rowListeners(item)"
                        >
                          <template #prepend>
                            <v-checkbox-btn
                              class="purchased-checkbox"
                              :model-value="item.isChecked"
                              color="primary"
                              :aria-label="`Mark ${item.displayName} purchased`"
                              @pointerdown.stop
                              @click.stop="onTogglePurchased(item)"
                            />
                          </template>

                          <v-list-item-title
                            :class="{ 'text-decoration-line-through': item.isChecked }"
                          >
                            {{ item.displayName }}
                          </v-list-item-title>

                          <v-list-item-subtitle v-if="item.amount">
                            {{ formatMeasurement(item.amount, item.unit) }}
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

                              <v-icon v-if="hasSources(item)" size="20">
                                {{ isExpanded(item) ? 'mdi-chevron-up' : 'mdi-chevron-down' }}
                              </v-icon>
                            </div>
                          </template>
                        </v-list-item>

                        <!-- ── Item Sources ──────────────────────────────────────── -->
                        <v-expand-transition>
                          <div v-if="isExpanded(item)">
                            <v-list density="compact" class="py-0 pl-12" :aria-label="`Recipes needing ${item.displayName}`">
                              <v-list-item
                                v-for="(source, sourceIdx) in item.sources"
                                :key="sourceIdx"
                                class="item-source"
                                :to="source.recipeId ? recipeRoute(source.recipeId, source.servings) : undefined"
                              >
                                <v-list-item-title class="text-body-2">{{ formatSourceLine(source) }}</v-list-item-title>
                                <v-list-item-subtitle v-if="source.scheduledDate">
                                  {{ formatDateOnly(source.scheduledDate, SOURCE_DATE_FORMAT) }}
                                </v-list-item-subtitle>
                                <template v-if="source.servings" #append>
                                  <ServingsChip :servings="source.servings" size="x-small" />
                                </template>
                              </v-list-item>
                            </v-list>
                          </div>
                        </v-expand-transition>
                      </div>
                    </v-expand-transition>
                  </v-list>
                </v-card>
              </div>
            </div>
          </v-expand-transition>
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

    </v-pull-to-refresh>

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
      @confirm="onAddCustomItem"
      @save="onSaveCustomItem"
    />

  </v-container>
</template>

<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useShoppingListStore } from '@/stores/shoppingList'
import { useUiStore } from '@/stores/ui'
import { formatCategory, INGREDIENT_CATEGORY_VALUES } from '@/constants/categories'
import { formatMeasurement } from '@/constants/units'
import { useHold } from '@/composables/useHold'
import { useDelayedHide } from '@/composables/useDelayedHide'
import { useListRefresh } from '@/composables/useListRefresh'
import { recipeRoute } from '@/router/recipeRoutes'
import { formatDateOnly } from '@/utils/dateOnly'
import { VPullToRefresh } from 'vuetify/labs/VPullToRefresh'
import CustomItemSheet from '@/components/CustomItemSheet.vue'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'
import ServingsChip from '@/components/ServingsChip.vue'

const store       = useShoppingListStore()
const ui          = useUiStore()
const showChecked = ref(false)
const sheetOpen   = ref(false)
const editingItem = ref(null)

const SOURCE_DATE_FORMAT = { weekday: 'short', day: 'numeric', month: 'short' }

const hold = useHold(openSheet)

// A mis-tapped checkbox would otherwise make the item vanish before the user sees what they ticked.
const delayedHide = useDelayedHide()

const listRefresh = useListRefresh({
  fetch: store.fetchRefresh,
  apply: latest => store.applyRefresh(latest, { keep: delayedHide.isDelayed }),
  // The item being edited would otherwise change or vanish under the sheet.
  isHeld: () => sheetOpen.value,
})

// The server's list is only worth loading once it holds every change made here.
watch(() => store.pendingCount, (count, before) => {
  if (count === 0 && before > 0) listRefresh.request()
})

const offlineBanner = computed(() => {
  if (!store.offline) return null
  const count = store.pendingCount
  return count ? `Offline · ${count} change${count === 1 ? '' : 's'} waiting` : 'Offline'
})

const isSaving = computed(() => !store.offline && store.pendingCount > 0)

// Regenerating replaces every Generated Item, so it waits until the server has every change made here.
const canRegenerate = computed(() => !store.offline && store.pendingCount === 0)

// Several rows can be open at once, so each Generated Item's open state is kept by id.
const expandedIds = ref(new Set())

function openSheet(item) {
  editingItem.value = item
  sheetOpen.value   = true
}

function hasSources(item) {
  return item.sources?.length > 0
}

function isExpanded(item) {
  return expandedIds.value.has(item.id)
}

function toggleSources(item) {
  if (isExpanded(item)) expandedIds.value.delete(item.id)
  else expandedIds.value.add(item.id)
}

async function onTogglePurchased(item) {
  const isChecked = !item.isChecked
  if (!isChecked) delayedHide.cancel(item.id)
  else if (!showChecked.value) delayedHide.delay(item.id)
  await store.toggleItem(item.id, isChecked)
}

function onToggleShowPurchased() {
  delayedHide.cancelAll()
  showChecked.value = !showChecked.value
}

function rowListeners(item) {
  if (item.isCustom) return customRowListeners(item)
  return hasSources(item) ? { click: () => toggleSources(item) } : {}
}

/** A source such as "Pancakes · 2 tbsp (30 ml)", naming only the recipe when it is Unquantified. */
function formatSourceLine(source) {
  const amount = formatMeasurement(source.amount, source.unit)
  if (!amount) return source.recipeName
  const share = formatMeasurement(source.shareAmount, source.shareUnit)
  return share ? `${source.recipeName} · ${amount} (${share})` : `${source.recipeName} · ${amount}`
}

function customRowListeners(item) {
  return { ...hold.listeners(item), contextmenu: event => onCustomRowContextMenu(event, item) }
}

// Android Chrome fires a touch contextmenu at about 500 ms, so only a mouse right-click opens the editor at once.
function onCustomRowContextMenu(event, item) {
  event.preventDefault()
  if (event.pointerType === 'mouse') openSheet(item)
}

// A list already on screen, or saved on this phone, shows at once, and the server's copy follows as a List Refresh.
onMounted(async () => {
  if (await store.restore()) listRefresh.request()
  else await store.fetchActive()
})

const items = computed(() => store.list?.items ?? [])

const checkedCount   = computed(() => items.value.filter(i => i.isChecked).length)
const uncheckedCount = computed(() => items.value.filter(i => !i.isChecked).length)

const visibleGroups = computed(() => {
  const filtered = showChecked.value
    ? items.value
    : items.value.filter(i => !i.isChecked || delayedHide.isDelayed(i.id))

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

// Each write shows at once and is sent through the Pending Changes, which report any conflict themselves.
function onAddCustomItem(payload) {
  store.addCustomItem(payload)
  ui.notify({ message: `Added “${payload.name}”`, color: 'success' })
}

function onSaveCustomItem(payload) {
  store.updateCustomItem(editingItem.value.id, payload)
  ui.notify({ message: `Saved “${payload.name}”`, color: 'success' })
}

function onDeleteItem(item) {
  store.deleteItem(item.id)
  ui.notify({ message: `Removed “${item.displayName}”`, color: 'success' })
}

async function onPull({ done }) {
  store.sendPending()
  try {
    await listRefresh.pull()
  } catch (e) {
    ui.notify({ message: e?.message ?? 'Could not refresh the list.', color: 'error' })
  } finally {
    done()
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
