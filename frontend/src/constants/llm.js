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

/** The LLM costs report's range chips, each a rolling window back from now; `days: null` reaches back to the first LLM Call. */
export const LLM_USAGE_RANGES = Object.freeze([
  Object.freeze({ key: '7d',  label: '7 days',   days: 7 }),
  Object.freeze({ key: '30d', label: '30 days',  days: 30 }),
  Object.freeze({ key: '90d', label: '90 days',  days: 90 }),
  Object.freeze({ key: 'all', label: 'All time', days: null }),
])

/** Mirrors `LlmUsagePaging.DefaultDays`, so the report opens on the range the API defaults to. */
export const LLM_USAGE_DEFAULT_RANGE = '30d'

/** Sent as `from` for All time, because an omitted `from` means the API's 30-day default. */
export const LLM_USAGE_ALL_TIME_FROM = '1970-01-01T00:00:00.000Z'

/** Mirrors `LlmUsagePaging.DefaultPageSize`. */
export const LLM_USAGE_PAGE_SIZE = 20

/** Labels for the wire values of `LlmProvider`, `LlmOperation`, `LlmStep` and `LlmCallOutcome`. */
export const LLM_PROVIDER_LABELS = Object.freeze({
  [LLM_PROVIDER_MODEL_SERVER]: 'Model Server',
  [LLM_PROVIDER_ANTHROPIC]:    'Claude (Anthropic)',
})

export const LLM_OPERATION_LABELS = Object.freeze({
  ScrapeUrl:      'Scrape (URL)',
  ScrapeText:     'Scrape (text)',
  SeedNormalise:  'Seed normalise',
  CatalogueBuild: 'Catalogue build',
})

export const LLM_STEP_LABELS = Object.freeze({
  Extraction:         'Extraction',
  IngredientMatching: 'Ingredient matching',
})

export const LLM_OUTCOME_SUCCEEDED = 'Succeeded'

export const LLM_OUTCOME_LABELS = Object.freeze({
  [LLM_OUTCOME_SUCCEEDED]: 'Succeeded',
  FailedExtraction:        'Failed extraction',
  Timeout:                 'Timeout',
  Unavailable:             'Unavailable',
  Rejected:                'Rejected',
})

/** Shown for an LLM Call or group whose model the provider never named. */
export const LLM_UNKNOWN_MODEL_LABEL = 'Unknown model'
