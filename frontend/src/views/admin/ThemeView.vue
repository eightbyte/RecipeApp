<template>
  <v-container class="pa-4" max-width="600">
    <v-radio-group v-model="themeId" hide-details>
      <v-list>
        <v-list-item
          v-for="theme in BUILT_IN_THEMES"
          :key="theme.id"
          :title="theme.name"
          @click="themeId = theme.id"
        >
          <template #prepend>
            <v-radio :value="theme.id" :aria-label="`Use ${theme.name} on this device`" class="mr-2" />
          </template>

          <div class="swatch-strip mt-2">
            <span
              v-for="token in THEME_TOKENS"
              :key="token"
              data-test="swatch"
              class="swatch"
              :title="token"
              :style="{ backgroundColor: theme.colors[token] }"
            />
          </div>
        </v-list-item>
      </v-list>
    </v-radio-group>
  </v-container>
</template>

<script setup>
import { computed } from 'vue'
import { useTheme } from 'vuetify'
import { BUILT_IN_THEMES, THEME_TOKENS } from '@/constants/themes'
import { writeThemeChoice } from '@/utils/themeChoice'

const theme = useTheme()

const themeId = computed({
  get: () => theme.global.name.value,
  set: (id) => {
    theme.change(id)
    writeThemeChoice(id)
  },
})
</script>

<style scoped>
.swatch-strip {
  display: flex;
  height: 20px;
  border: 1px solid rgb(var(--v-theme-border));
  border-radius: 6px;
  overflow: hidden;
}

.swatch {
  flex: 1 1 0;
}
</style>
