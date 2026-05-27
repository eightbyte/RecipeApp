import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  {
    path: '/',
    name: 'home',
    component: () => import('@/views/HomeView.vue'),
    meta: { title: 'Home' },
  },
  {
    path: '/recipes',
    name: 'recipes',
    component: () => import('@/views/RecipesView.vue'),
    meta: { title: 'Recipes' },
  },
  {
    path: '/meal-plan',
    name: 'meal-plan',
    component: () => import('@/views/MealPlanView.vue'),
    meta: { title: 'Meal Plan' },
  },
  {
    path: '/shopping',
    name: 'shopping',
    component: () => import('@/views/ShoppingView.vue'),
    meta: { title: 'Shopping List' },
  },
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
})

// Update document title on navigation
router.afterEach((to) => {
  const appTitle = import.meta.env.VITE_APP_TITLE ?? 'RecipeApp'
  document.title = to.meta.title ? `${to.meta.title} — ${appTitle}` : appTitle
})

export default router
