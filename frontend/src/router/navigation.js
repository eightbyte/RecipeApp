/** The key App.vue mounts a view under: a route flagged `keepOnQueryChange` stays mounted while only its query changes. */
export function viewKey(route) {
  return route.meta.keepOnQueryChange ? route.path : route.fullPath
}

/** The router's scroll behaviour: a view kept mounted through a query change stays where the reader left it, and every other navigation starts at the top. */
export function scrollPosition(to, from) {
  if (to.meta.keepOnQueryChange && to.path === from.path) return false
  return { top: 0 }
}
