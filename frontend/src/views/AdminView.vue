<template>
  <v-container class="pa-4" max-width="600">
    <EmptyState
      v-if="sections.length === 0"
      icon="mdi-cog-outline"
      title="No admin tools yet"
    />

    <v-list v-else>
      <template v-for="section in sections" :key="section.name">
        <v-list-subheader>{{ section.name }}</v-list-subheader>
        <v-list-item
          v-for="tool in section.tools"
          :key="tool.name"
          :to="{ name: tool.name }"
          :prepend-icon="tool.meta.icon"
          :title="tool.meta.title"
          :subtitle="tool.meta.description"
        />
      </template>
    </v-list>
  </v-container>
</template>

<script setup>
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { ADMIN_SECTIONS } from '@/router/adminTools'
import EmptyState from '@/components/EmptyState.vue'

const router = useRouter()

const sections = computed(() => {
  const tools = router.getRoutes().filter((r) => r.meta.section)
  return ADMIN_SECTIONS
    .map((name) => ({ name, tools: tools.filter((t) => t.meta.section === name) }))
    .filter((section) => section.tools.length > 0)
})
</script>
