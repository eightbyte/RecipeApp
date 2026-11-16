import { defineStore } from 'pinia'
import api from '@/services/api'

// Stateless because the LLM costs report owns its range, page and response, so leaving it resets them.
export const useLlmUsageStore = defineStore('llmUsage', () => {
  async function fetchUsage({ from, page, pageSize, signal }) {
    const { data } = await api.get('/llm/usage', { params: { from, page, pageSize }, signal })
    return data
  }

  return { fetchUsage }
})
