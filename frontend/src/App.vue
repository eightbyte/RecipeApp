<template>
  <!--
    v-app is required by Vuetify — it sets up the layout context,
    theming, and portal targets for dialogs/snackbars.
  -->
  <v-app>

    <!-- Top navigation bar — hidden on full-screen routes (e.g. Cooking Mode) -->
    <AppTopBar v-if="!route.meta.fullscreen" />

    <!-- Main content area — padded to sit between top bar and bottom nav -->
    <v-main>
      <router-view v-slot="{ Component, route: r }">
        <transition name="fade" mode="out-in">
          <component :is="Component" :key="r.fullPath" />
        </transition>
      </router-view>
    </v-main>

    <!-- Bottom navigation tabs — hidden on full-screen routes -->
    <AppBottomNav v-if="!route.meta.fullscreen" />

    <!-- Global feedback snackbar — available to every view -->
    <AppSnackbar />

  </v-app>
</template>

<script setup>
import { useRoute } from 'vue-router'
import AppTopBar    from '@/components/layout/AppTopBar.vue'
import AppBottomNav from '@/components/layout/AppBottomNav.vue'
import AppSnackbar  from '@/components/AppSnackbar.vue'

const route = useRoute()
</script>

<style>
/* Page transition */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.15s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

/* Reduced motion: disable the page transition for users who request it. */
@media (prefers-reduced-motion: reduce) {
  .fade-enter-active,
  .fade-leave-active {
    transition: none;
  }
}
</style>
