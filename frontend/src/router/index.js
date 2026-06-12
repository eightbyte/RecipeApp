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
    path: '/recipes/new',
    name: 'recipe-create',
    component: () => import('@/views/RecipeFormView.vue'),
    meta: { title: 'New Recipe' },
  },
  {
    path: '/recipes/:id',
    name: 'recipe-detail',
    component: () => import('@/views/RecipeDetailView.vue'),
    meta: { title: 'Recipe' },
    props: true,
  },
  {
    path: '/recipes/:id/edit',
    name: 'recipe-edit',
    component: () => import('@/views/RecipeFormView.vue'),
    meta: { title: 'Edit Recipe' },
    props: true,
  },
  {
    // `/cooking` (not `/cook`) avoids confusion with the POST .../cook action.
    path: '/recipes/:id/cooking',
    name: 'recipe-cooking',
    component: () => import('@/views/CookingModeView.vue'),
    meta: { title: 'Cooking', fullscreen: true },
    props: true,
  },
  {
    path: '/recipes/scrape/preview',
    name: 'scrape-preview',
    component: () => import('@/views/RecipeScrapePreviewView.vue'),
    meta: { title: 'Review Imported Recipe' },
  },
  {
    path: '/meal-plan',
    name: 'meal-plan',
    component: () => import('@/views/MealPlanView.vue'),
    meta: { title: 'Meal Plan' },
  },
  {
    path: '/meal-plan/new',
    name: 'meal-plan-create',
    component: () => import('@/views/MealPlanBuilderView.vue'),
    meta: { title: 'New Meal Plan' },
  },
  {
    path: '/meal-plan/past',
    name: 'meal-plan-past',
    component: () => import('@/views/PastPlansView.vue'),
    meta: { title: 'Past Plans' },
  },
  {
    path: '/meal-plan/:id',
    name: 'meal-plan-detail',
    component: () => import('@/views/MealPlanDetailView.vue'),
    meta: { title: 'Meal Plan' },
    props: true,
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
  scrollBehavior: () => ({ top: 0 }),
})

router.afterEach((to) => {
  const appTitle = import.meta.env.VITE_APP_TITLE ?? 'RecipeApp'
  document.title = to.meta.title ? `${to.meta.title} — ${appTitle}` : appTitle
})

export default router
