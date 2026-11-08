/** The key App.vue mounts a view under: a route flagged `keepOnQueryChange` stays mounted while only its query changes. */
export function viewKey(route) {
  return route.meta.keepOnQueryChange ? route.path : route.fullPath
}

/** The router's scroll behaviour: a view that stays mounted stays where the reader left it, and every other navigation starts at the top. */
export function scrollPosition(to, from) {
  return viewKey(to) === viewKey(from) ? false : { top: 0 }
}
