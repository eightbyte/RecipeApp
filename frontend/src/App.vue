<template>
  <!--
    v-app is required by Vuetify — it sets up the layout context,
    theming, and portal targets for dialogs/snackbars.
  -->
  <v-app>

    <!-- Top navigation bar -->
    <AppTopBar />

    <!-- Main content area — padded to sit between top bar and bottom nav -->
    <v-main>
      <router-view v-slot="{ Component, route }">
        <transition name="fade" mode="out-in">
          <component :is="Component" :key="route.fullPath" />
        </transition>
      </router-view>
    </v-main>

    <!-- Bottom navigation tabs -->
    <AppBottomNav />

  </v-app>
</template>

<script setup>
import AppTopBar    from '@/components/layout/AppTopBar.vue'
import AppBottomNav from '@/components/layout/AppBottomNav.vue'
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
</style>
