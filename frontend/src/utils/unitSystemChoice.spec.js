import { readUnitSystemChoice, writeUnitSystemChoice } from './unitSystemChoice'
import { UNIT_SYSTEM_STORAGE_KEY } from '@/constants/unitSystems'

beforeEach(() => {
  window.localStorage.clear()
})

describe('readUnitSystemChoice', () => {
  it('is Metric when this device has not chosen a Unit System', () => {
    expect(readUnitSystemChoice()).toBe('metric')
  })

  it("is the device's stored Unit System", () => {
    window.localStorage.setItem(UNIT_SYSTEM_STORAGE_KEY, JSON.stringify('us'))
    expect(readUnitSystemChoice()).toBe('us')
  })

  it('falls back to Metric when the stored value is unknown or unreadable', () => {
    window.localStorage.setItem(UNIT_SYSTEM_STORAGE_KEY, JSON.stringify('imperial'))
    expect(readUnitSystemChoice()).toBe('metric')
    window.localStorage.setItem(UNIT_SYSTEM_STORAGE_KEY, '{not json')
    expect(readUnitSystemChoice()).toBe('metric')
  })
})

describe('writeUnitSystemChoice', () => {
  it('stores the choice, so the next read returns it', () => {
    writeUnitSystemChoice('us')
    expect(readUnitSystemChoice()).toBe('us')
  })
})
