// Excel hàng loạt (III.3): import template per category, price / stock sheet, background tasks.
import { ApiError, apiRequest } from './http'
import { download } from './orders'
import { useAuthStore } from '../stores/auth'

export type BulkKind = 'ProductImport' | 'PriceStockUpdate'
export type BulkStatus = 'Queued' | 'Running' | 'Done' | 'Failed'

export interface BulkError {
  row: number
  column: string | null
  message: string
}

export interface BulkTask {
  id: string
  kind: BulkKind
  status: BulkStatus
  fileName: string
  total: number
  processed: number
  succeeded: number
  failed: number
  errors: BulkError[]
  message: string | null
  createdAt: string
  finishedAt: string | null
}

const base = (shopId: string) => `/seller/shops/${shopId}/bulk`

export const bulkApi = {
  template: (shopId: string, categoryId: string) => download(`${base(shopId)}/template?categoryId=${categoryId}`),
  priceStock: (shopId: string) => download(`${base(shopId)}/price-stock`),
  tasks: (shopId: string) => apiRequest<BulkTask[]>(`${base(shopId)}/tasks`),
  task: (shopId: string, taskId: string) => apiRequest<BulkTask>(`${base(shopId)}/tasks/${taskId}`),

  /** Multipart upload of a filled sheet; the server queues the work and returns the task to follow. */
  start: async (shopId: string, kind: BulkKind, file: File): Promise<BulkTask> => {
    const form = new FormData()
    form.append('file', file)
    const token = useAuthStore.getState().accessToken
    const res = await fetch(`/api${base(shopId)}/${kind}`, {
      method: 'POST',
      body: form,
      credentials: 'include',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    })
    const body = (await res.json().catch(() => null)) as { success: boolean; data: BulkTask; message: string; errors: { field: string; message: string }[] } | null
    if (!res.ok || !body?.success) throw new ApiError(res.status, body?.errors?.[0]?.message ?? body?.message ?? 'Không tải được tệp.', body?.errors ?? [])
    return body.data
  },
}
