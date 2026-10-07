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
  // The JOB.*_CRON parameter holding the schedule
  parameterKey: string | null
}

export interface BackupFile { name: string; sizeBytes: number; writtenAt: string }
export interface BackupRun { id: string; fileName: string; sizeBytes: number | null; startedAt: string; finishedAt: string | null; status: 'Running' | 'Succeeded' | 'Failed'; error: string | null }
export interface Backups { directory: string; keepCount: number; files: BackupFile[]; runs: BackupRun[] }

export const jobsApi = {
  backups: () => apiRequest<Backups>('/admin/backups'),
  list: () => apiRequest<JobRow[]>('/admin/job-runs'),
  run: (id: string) => apiCommand(`/admin/job-runs/${encodeURIComponent(id)}`, { method: 'POST' }),
  // One-use link (60 s) that opens the Hangfire dashboard with a cookie
  ticket: () => apiRequest<{ url: string; expiresInSeconds: number }>('/admin/jobs/ticket', { method: 'POST' }),
}
