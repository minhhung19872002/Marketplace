// All HTTP paths live under src/api — components never write '/api/...' strings
export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy' | 'Unreachable'

export async function fetchHealth(signal?: AbortSignal): Promise<HealthStatus> {
  try {
    const res = await fetch('/health', { signal })
    const text = (await res.text()).trim()
    if (text === 'Healthy' || text === 'Degraded' || text === 'Unhealthy') return text
    return res.ok ? 'Healthy' : 'Unhealthy'
  } catch {
    return 'Unreachable'
  }
}
