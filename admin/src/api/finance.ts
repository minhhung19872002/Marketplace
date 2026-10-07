// Platform finance: fee schedule, withdrawals, ledger, reconciliation with the gateway and the carrier.
import { ApiError, apiCommand, apiRequest, refreshSession } from './http'
import { useAuthStore } from '../stores/auth'
import type { PagedResult } from './admin'

export type FeeType = 'Fixed' | 'Payment' | 'FreeshipXtra' | 'VoucherXtra'

export interface FeeRule {
  id: string
  categoryId: string | null
  categoryName: string | null
  feeType: FeeType
  rateBp: number
  validFrom: string
  validTo: string | null
  note: string | null
  inForce: boolean
}

export interface Withdrawal {
  id: string
  ownerType: 'Shop' | 'Buyer'
  ownerId: string
  ownerName: string | null
  amount: number
  status: 'Pending' | 'Processing' | 'Done' | 'Rejected'
  statusLabel: string
  bankCode: string
  accountLast4: string
  accountName: string
  rejectReason: string | null
  bankRef: string | null
  createdAt: string
}

export interface LedgerAccount {
  ownerType: 'Platform' | 'Shop' | 'Buyer'
  type: string
  label: string
  balance: number
}

export interface LedgerOverview {
  platform: LedgerAccount[]
  totals: LedgerAccount[]
  check: { accounts: number; mismatches: unknown[]; totalDebits: number; totalCredits: number; unbalancedTransactions: number }
}

export interface LedgerEntry {
  id: string
  transactionId: string
  kind: string
  ownerType: string
  accountType: string
  direction: 'Debit' | 'Credit'
  amount: number
  description: string
  postedAt: string
}

export interface ReconcileResult {
  statementLines: number
  matched: number
  issues: { reference: string; issue: string; providerAmount: number | null; systemAmount: number | null; note: string }[]
  statementTotal: number
  systemTotal: number
  // Fees the gateway charged, as written in its file
  statementFees: number
}

export interface ReconcileSource {
  code: string
  name: string
  // Simulated providers can generate their own statement file
  simulated: boolean
}

export const FEE_TYPE_LABEL: Record<FeeType, string> = {
  Fixed: 'Phí cố định',
  Payment: 'Phí thanh toán',
  FreeshipXtra: 'Phí dịch vụ Freeship Xtra',
  VoucherXtra: 'Phí dịch vụ Voucher Xtra',
}

export const ISSUE_LABEL: Record<string, string> = {
  MissingInSystem: 'ShopHub không có',
  MissingInStatement: 'Tệp không có',
  AmountMismatch: 'Lệch số tiền',
  RefundMismatch: 'Lệch tiền hoàn',
  FeeMismatch: 'Lệch phí vận chuyển',
  BadLine: 'Dòng lỗi',
  DuplicateInStatement: 'Lặp trong tệp',
}

const range = (from: string, to: string) => `from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`

async function raw(path: string, init: RequestInit = {}, retried = false): Promise<Response> {
  const token = useAuthStore.getState().accessToken
  const res = await fetch(`/api${path}`, { ...init, credentials: 'include', headers: token ? { Authorization: `Bearer ${token}` } : {} })
  if (res.status === 401 && !retried && (await refreshSession())) return raw(path, init, true)
  return res
}

export const financeApi = {
  feeRules: () => apiRequest<FeeRule[]>('/admin/finance/fee-rules'),
  createFeeRule: (body: { categoryId: string | null; feeType: FeeType; rateBp: number; validFrom: string; note: string | null }) =>
    apiCommand<string>('/admin/finance/fee-rules', { method: 'POST', body }),
  withdrawals: (status: string, page: number) =>
    apiRequest<PagedResult<Withdrawal>>(`/admin/finance/withdrawals?status=${status}&page=${page}&pageSize=20`),
  approve: (id: string) => apiCommand(`/admin/finance/withdrawals/${id}/approve`, { method: 'POST' }),
  reject: (id: string, reason: string) => apiCommand(`/admin/finance/withdrawals/${id}/reject`, { method: 'POST', body: { reason } }),
  ledger: () => apiRequest<LedgerOverview>('/admin/finance/ledger'),
  entries: (page: number) => apiRequest<PagedResult<LedgerEntry>>(`/admin/finance/ledger/entries?page=${page}&pageSize=50`),
  sources: () => apiRequest<{ gateways: ReconcileSource[]; carriers: ReconcileSource[] }>('/admin/finance/reconcile/sources'),
  statement: async (provider: 'gateway' | 'carrier', source: string, from: string, to: string) => {
    const res = await raw(`/admin/finance/statements/${provider}?${range(from, to)}&carrier=${encodeURIComponent(source)}`)
    if (!res.ok) throw new ApiError(res.status, 'Không tải được sao kê.')
    return res.blob()
  },
  reconcile: async (provider: 'gateway' | 'carrier', source: string, from: string, to: string, file: File) => {
    const form = new FormData()
    form.append('from', from)
    form.append('to', to)
    form.append('source', source)
    form.append('file', file)
    const res = await raw(`/admin/finance/reconcile/${provider}`, { method: 'POST', body: form })
    const body = (await res.json().catch(() => null)) as { success: boolean; data: ReconcileResult; message: string } | null
    if (!res.ok || !body?.success) throw new ApiError(res.status, body?.message ?? 'Đối soát không thành công.')
    return body.data
  },
}
