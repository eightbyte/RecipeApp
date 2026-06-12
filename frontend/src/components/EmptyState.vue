<template>
  <div class="text-center py-12">
    <v-icon :icon="icon" size="64" color="grey-lighten-2" />
    <div v-if="title" class="text-h6 mt-3 mb-1">{{ title }}</div>
    <div v-if="text" class="text-body-2 text-medium-emphasis mb-4">{{ text }}</div>

    <!-- Optional CTA: routes if actionTo given, otherwise emits `action` -->
    <v-btn
      v-if="actionLabel"
      color="primary"
      :to="actionTo ?? undefined"
      @click="onAction"
    >
      {{ actionLabel }}
    </v-btn>
  </div>
</template>

<script setup>
const props = defineProps({
  icon:        { type: String, default: 'mdi-information-outline' },
  title:       { type: String, default: '' },
  text:        { type: String, default: '' },
  actionLabel: { type: String, default: '' },
  // Router target (e.g. { name: 'recipes' }); when absent, the CTA emits `action`.
  actionTo:    { type: [Object, String], default: null },
})

const emit = defineEmits(['action'])

function onAction() {
  if (!props.actionTo) emit('action')
}
</script>
