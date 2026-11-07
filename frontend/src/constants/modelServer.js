/** The Model Server states, mirroring `ModelServerState` on the backend. */
export const MODEL_SERVER_OFFLINE      = 'offline'
export const MODEL_SERVER_LOADING      = 'loading'
export const MODEL_SERVER_READY        = 'ready'
export const MODEL_SERVER_UNAUTHORIZED = 'unauthorized'

/** Why Scraping is unavailable in each state that is not ready, worded as the backend's `UnavailableReason` so the sheet and a 503 say the same thing. */
export const MODEL_SERVER_UNAVAILABLE_MESSAGES = Object.freeze({
  [MODEL_SERVER_OFFLINE]:      "Scraping is unavailable: the Model Server isn't running.",
  [MODEL_SERVER_LOADING]:      'Scraping is unavailable: the Model Server is still loading the model, try again in a moment.',
  [MODEL_SERVER_UNAUTHORIZED]: 'Scraping is unavailable: the Model Server rejected the API key.',
})

/** How often the URL sheet asks again while the model is loading. */
export const MODEL_SERVER_LOADING_RECHECK_MS = 3000
