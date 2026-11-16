// The order the Admin landing list shows its sections in; an Admin Tool names one as `meta.section`.
export const ADMIN_SECTIONS = ['Data', 'Appearance', 'Reports']

// Kept beside the section order so adding an Admin Tool touches only this file and its view.
export const adminToolRoutes = [
  {
    path: 'ingredients',
    name: 'admin-ingredients',
    component: () => import('@/views/admin/IngredientsView.vue'),
    meta: {
      title: 'Ingredients',
      icon: 'mdi-food-apple-outline',
      description: 'Edit, add, remove and restore Ingredients',
      section: 'Data',
    },
  },
  // A route with no `section` is a tool's own sub-page, which the landing list leaves out.
  {
    path: 'ingredients/new',
    name: 'admin-ingredient-new',
    component: () => import('@/views/admin/IngredientFormView.vue'),
    meta: { title: 'Add ingredient' },
  },
  {
    path: 'ingredients/:id',
    name: 'admin-ingredient-edit',
    component: () => import('@/views/admin/IngredientFormView.vue'),
    props: true,
    meta: { title: 'Edit ingredient' },
  },
  {
    path: 'llm-costs',
    name: 'admin-llm-costs',
    component: () => import('@/views/admin/LlmCostsView.vue'),
    meta: {
      title: 'LLM costs',
      icon: 'mdi-chart-bar',
      description: 'Estimated cost and tokens of every LLM call',
      section: 'Reports',
    },
  },
]
