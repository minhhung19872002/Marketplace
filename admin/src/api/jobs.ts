import { apiCommand, apiRequest } from './http'

export interface JobRow {
  id: string
  cron: string
  timeZone: string | null
  // Hangfire's own times (UTC instants)
  nextExecution: string | null
  lastExecution: string | null
  lastState: string | null
  lastError: string | null
  runnable: boolean
}

export const jobsApi = {
  list: () => apiRequest<JobRow[]>('/admin/job-runs'),
  run: (id: string) => apiCommand(`/admin/job-runs/${encodeURIComponent(id)}`, { method: 'POST' }),
  // One-use link (60 s) that opens the Hangfire dashboard with a cookie
  ticket: () => apiRequest<{ url: string; expiresInSeconds: number }>('/admin/jobs/ticket', { method: 'POST' }),
}
