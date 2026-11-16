<template>
  <v-container class="pa-4 position-relative" max-width="600">
    <!-- Absolute so a range or page change keeps the shown report in place rather than shifting it. -->
    <v-progress-linear
      :active="loading && usage !== null"
      indeterminate
      color="primary"
      absolute
      location="top"
    />

    <v-chip-group
      :model-value="rangeKey"
      mandatory
      selected-class="text-primary"
      class="mb-2"
      @update:model-value="changeRange"
    >
      <v-chip
        v-for="range in LLM_USAGE_RANGES"
        :key="range.key"
        :value="range.key"
        variant="outlined"
        filter
      >{{ range.label }}</v-chip>
    </v-chip-group>

    <div v-if="loading && usage === null" class="d-flex justify-center py-12">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <ErrorState v-else-if="error" :message="error" @retry="load" />

    <EmptyState
      v-else-if="usage?.totalCount === 0"
      icon="mdi-chart-bar"
      title="No LLM calls in this range"
    />

    <template v-else-if="usage">
      <div data-test="grand-total" class="mb-1">
        <div class="text-h5 font-weight-bold">{{ formatTotalCost(grandTotalUsd) }}</div>
        <div class="text-body-2 text-medium-emphasis">{{ callCount(usage.totalCount) }}</div>
        <div v-if="unpricedCalls > 0" class="text-body-2 text-warning">
          Excludes {{ callCount(unpricedCalls, 'unpriced') }}
        </div>
      </div>
      <p class="text-caption text-medium-emphasis mb-4">
        Estimated in USD from the price list when each call was recorded. Per-call and group costs
        show 4 decimals because a single call costs fractions of a cent.
      </p>

      <v-list class="pa-0 mb-4">
        <v-list-item
          v-for="group in usage.totals"
          :key="`${group.provider}|${group.model}|${group.operation}`"
          data-test="usage-group"
          class="px-0"
        >
          <div class="d-flex align-center ga-2">
            <span class="text-body-2 text-truncate">
              {{ LLM_PROVIDER_LABELS[group.provider] }} ·
              {{ group.model ?? LLM_UNKNOWN_MODEL_LABEL }} ·
              {{ LLM_OPERATION_LABELS[group.operation] }}
            </span>
            <v-spacer />
            <v-icon
              v-if="group.unpricedCalls > 0"
              data-test="unpriced-flag"
              icon="mdi-alert-outline"
              color="warning"
              size="small"
              :aria-label="callCount(group.unpricedCalls, 'unpriced')"
            />
            <span class="text-body-2 font-weight-medium text-no-wrap">
              {{ formatCost(group.estimatedCostUsd) }}
            </span>
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ callCount(group.calls) }} · {{ formatTokens(groupTokensIn(group), group.outputTokens) }}
          </div>
        </v-list-item>
      </v-list>

      <v-divider />

      <v-list lines="three" class="pa-0">
        <v-list-item v-for="call in usage.items" :key="call.id" data-test="llm-call" class="px-0">
          <div class="d-flex align-center ga-2">
            <span class="text-body-2 text-truncate">
              {{ formatWhen(call.createdAt) }} · {{ operationLabel(call) }}
            </span>
            <v-spacer />
            <span v-if="call.estimatedCostUsd === null" class="text-body-2 text-warning text-no-wrap">
              Unpriced
            </span>
            <span v-else class="text-body-2 font-weight-medium text-no-wrap">
              {{ formatCost(call.estimatedCostUsd) }}
            </span>
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ call.model ?? LLM_UNKNOWN_MODEL_LABEL }} ·
            <span :class="{ 'text-error': call.outcome !== LLM_OUTCOME_SUCCEEDED }">
              {{ LLM_OUTCOME_LABELS[call.outcome] }}</span> ·
            {{ formatTokens(callTokensIn(call), call.outputTokens) }} ·
            {{ formatDuration(call.durationMs) }}
          </div>
          <a
            v-if="call.sourceUrl"
            :href="call.sourceUrl"
            target="_blank"
            rel="noopener"
            class="d-block text-caption text-primary text-truncate"
          >{{ shortUrl(call.sourceUrl) }}</a>
        </v-list-item>
      </v-list>

      <v-pagination
        :model-value="page"
        :length="pageCount"
        density="comfortable"
        class="mt-2"
        @update:model-value="changePage"
      />
    </template>
  </v-container>
</template>

<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useLlmUsageStore } from '@/stores/llmUsage'
import { DISPLAY_LOCALE } from '@/utils/dateOnly'
import EmptyState from '@/components/EmptyState.vue'
import ErrorState from '@/components/ErrorState.vue'
import {
  LLM_USAGE_RANGES, LLM_USAGE_DEFAULT_RANGE, LLM_USAGE_ALL_TIME_FROM, LLM_USAGE_PAGE_SIZE,
  LLM_PROVIDER_LABELS, LLM_OPERATION_LABELS, LLM_STEP_LABELS, LLM_OUTCOME_LABELS,
  LLM_OUTCOME_SUCCEEDED, LLM_UNKNOWN_MODEL_LABEL,
} from '@/constants/llm'

const DAY_MS = 24 * 60 * 60 * 1000

// `narrowSymbol` keeps a plain "$" in locales that would print "USD" or "US$".
const USD       = { style: 'currency', currency: 'USD', currencyDisplay: 'narrowSymbol' }
const wholeCost = new Intl.NumberFormat(DISPLAY_LOCALE, USD)
// A single call costs fractions of a cent, so per-call and group costs keep 4 decimals.
const fineCost  = new Intl.NumberFormat(DISPLAY_LOCALE, { ...USD, minimumFractionDigits: 4, maximumFractionDigits: 4 })
const CENT_USD  = 0.01

const tokenCount  = new Intl.NumberFormat(DISPLAY_LOCALE)
const WHEN_FORMAT = { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }

const store = useLlmUsageStore()

const rangeKey = ref(LLM_USAGE_DEFAULT_RANGE)
const page     = ref(1)
const usage    = ref(null)
const loading  = ref(false)
const error    = ref(null)

// Aborted by the next load, which cancels a superseded request and marks its response stale.
let controller = new AbortController()

function fromFor(key) {
  const { days } = LLM_USAGE_RANGES.find((range) => range.key === key)
  return days === null ? LLM_USAGE_ALL_TIME_FROM : new Date(Date.now() - days * DAY_MS).toISOString()
}

async function load() {
  controller.abort()
  controller = new AbortController()
  const { signal } = controller
  loading.value = true
  error.value   = null
  try {
    const data = await store.fetchUsage({
      from: fromFor(rangeKey.value), page: page.value, pageSize: LLM_USAGE_PAGE_SIZE, signal,
    })
    if (signal.aborted) return
    usage.value = data
  } catch (e) {
    if (signal.aborted) return
    error.value = e.message
  }
  loading.value = false
}

const sumOf = (groups = [], field) => groups.reduce((sum, group) => sum + group[field], 0)

const pageCount     = computed(() => Math.ceil((usage.value?.totalCount ?? 0) / LLM_USAGE_PAGE_SIZE))
const grandTotalUsd = computed(() => sumOf(usage.value?.totals, 'estimatedCostUsd'))
const unpricedCalls = computed(() => sumOf(usage.value?.totals, 'unpricedCalls'))

function changeRange(key) {
  rangeKey.value = key
  page.value     = 1
  load()
}

function changePage(newPage) {
  page.value = newPage
  load()
}

function operationLabel(call) {
  const operation = LLM_OPERATION_LABELS[call.operation]
  return call.step ? `${operation} / ${LLM_STEP_LABELS[call.step]}` : operation
}

// The cache split is left out, so cached input still counts as input.
function callTokensIn(call) {
  return (call.inputTokens ?? 0) + (call.cacheReadTokens ?? 0) + (call.cacheWriteTokens ?? 0)
}

function groupTokensIn(group) {
  return group.inputTokens + group.cacheReadTokens + group.cacheWriteTokens
}

function formatTokens(tokensIn, tokensOut) {
  return `in ${tokenCount.format(tokensIn)} · out ${tokenCount.format(tokensOut ?? 0)}`
}

// The Model Server's calls cost nothing, which reads better as $0.00 than $0.0000.
const formatCost      = (usd) => (usd === 0 ? wholeCost : fineCost).format(usd)
const formatTotalCost = (usd) => (usd > 0 && usd < CENT_USD ? fineCost : wholeCost).format(usd)
const formatWhen      = (createdAt) => new Date(createdAt).toLocaleString(DISPLAY_LOCALE, WHEN_FORMAT)
const formatDuration  = (ms) => `${(ms / 1000).toFixed(1)} s`
const callCount       = (n, kind = '') => `${n} ${kind ? `${kind} ` : ''}${n === 1 ? 'call' : 'calls'}`

function shortUrl(url) {
  try {
    const { hostname, pathname } = new URL(url)
    return hostname + (pathname === '/' ? '' : pathname)
  } catch {
    return url
  }
}

onMounted(load)
onBeforeUnmount(() => controller.abort())
</script>
