<template>
  <v-infinite-scroll :margin="LOAD_AHEAD_PX" @load="onLoad">
    <slot />

    <template #loading>
      <v-progress-circular v-if="!isComplete" indeterminate size="24" width="3" color="primary" />
    </template>

    <template #error="{ props: retryProps }">
      <div class="d-flex align-center ga-3 text-body-2 text-medium-emphasis">
        Couldn’t load more recipes
        <v-btn variant="tonal" size="small" color="primary" @click="retryProps.onClick">Retry</v-btn>
      </div>
    </template>

    <template #empty>
      <div v-if="pageCount > 1" class="text-caption text-medium-emphasis">
        That’s all {{ totalCount.toLocaleString() }} recipes
      </div>
    </template>
  </v-infinite-scroll>
</template>

<script setup>
/** Starts loading the next page this far before the end of the list, so scrolling rarely has to wait. */
const LOAD_AHEAD_PX = 300

const props = defineProps({
  loadMore:   { type: Function, required: true },
  isComplete: { type: Boolean, required: true },
  pageCount:  { type: Number, required: true },
  totalCount: { type: Number, required: true },
})

async function onLoad({ done }) {
  done(await props.loadMore())
}
</script>
