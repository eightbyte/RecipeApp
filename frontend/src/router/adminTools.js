// The order the Admin landing list shows its sections in; an Admin Tool names one as `meta.section`.
export const ADMIN_SECTIONS = ['Data', 'Appearance', 'Reports']

// Kept beside the section order so adding an Admin Tool touches only this file and its view.
export const adminToolRoutes = [
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
