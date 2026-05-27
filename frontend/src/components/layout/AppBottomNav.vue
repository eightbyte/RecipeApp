<template>
  <v-bottom-navigation
    v-model="activeTab"
    color="primary"
    grow
    elevation="8"
  >
    <v-btn
      v-for="item in navItems"
      :key="item.route"
      :value="item.route"
      :to="{ name: item.route }"
      stacked
    >
      <v-icon>{{ item.icon }}</v-icon>
      <span class="text-caption">{{ item.label }}</span>
    </v-btn>
  </v-bottom-navigation>
</template>

<script setup>
import { ref, watch } from 'vue'
import { useRoute } from 'vue-router'

const route     = useRoute()
const activeTab = ref(route.name)

// Keep the active tab in sync when the route changes programmatically
watch(() => route.name, (name) => {
  activeTab.value = name
})

const navItems = [
  { route: 'home',      icon: 'mdi-home-outline',          label: 'Home'     },
  { route: 'recipes',   icon: 'mdi-chef-hat',              label: 'Recipes'  },
  { route: 'meal-plan', icon: 'mdi-calendar-week-outline', label: 'Meal Plan'},
  { route: 'shopping',  icon: 'mdi-cart-outline',          label: 'Shopping' },
]
</script>
