/** The LLM providers Scraping can use, mirroring `LlmProvider` on the backend. */
export const LLM_PROVIDER_MODEL_SERVER = 'modelServer'
export const LLM_PROVIDER_ANTHROPIC    = 'anthropic'

/** The provider states, mirroring `LlmState` on the backend. */
export const LLM_OFFLINE        = 'offline'
export const LLM_LOADING        = 'loading'
export const LLM_READY          = 'ready'
export const LLM_UNAUTHORIZED   = 'unauthorized'
export const LLM_BUSY           = 'busy'
export const LLM_NOT_CONFIGURED = 'notConfigured'

/** Why Scraping is unavailable in each state a provider reports other than ready, worded as the backend's `UnavailableReason` so the sheet and a 503 say the same thing. */
export const LLM_UNAVAILABLE_MESSAGES = Object.freeze({
  [LLM_PROVIDER_MODEL_SERVER]: Object.freeze({
    [LLM_OFFLINE]:      "Scraping is unavailable: the Model Server isn't running.",
    [LLM_LOADING]:      'Scraping is unavailable: the Model Server is still loading the model, try again in a moment.',
    [LLM_UNAUTHORIZED]: 'Scraping is unavailable: the Model Server rejected the API key.',
  }),
  [LLM_PROVIDER_ANTHROPIC]: Object.freeze({
    [LLM_OFFLINE]:        "Scraping is unavailable: Claude (Anthropic) can't be reached.",
    [LLM_UNAUTHORIZED]:   'Scraping is unavailable: Claude (Anthropic) rejected the API key.',
    [LLM_BUSY]:           'Scraping is unavailable: Claude is busy right now, try again in a moment.',
    [LLM_NOT_CONFIGURED]: "Scraping is unavailable: Claude (Anthropic) isn't configured.",
  }),
})

/** Shown for a cloud provider, because a Scrape's content leaves the network; a provider on this network has none. */
export const LLM_CLOUD_CAPTIONS = Object.freeze({
  [LLM_PROVIDER_ANTHROPIC]: 'Extracted with Claude (Anthropic)',
})

/** How often the Import Recipe sheet asks again while the model is loading. */
export const LLM_LOADING_RECHECK_MS = 3000
