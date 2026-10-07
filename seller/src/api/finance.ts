// Shop finance: "chờ giải ngân" / "đã giải ngân", balance, withdrawals, bank accounts, reports.
import { apiCommand, apiRequest } from './http'
import { download } from './orders'
import type { PagedResult } from './seller'

export interface BankAccount {
  id: string
  bankCode: string
  accountNoLast4: string
  accountName: string
  verified: boolean
  isDefault: boolean
}

export interface FinanceSummary {
  pending: number
  available: number
  withdrawing: number
  releasedTotal: number
  withdrawMin: number
  withdrawPerWeek: number
  autoApproveMax: number
  bankAccounts: BankAccount[]
}

export interface Earning {
  orderId: string
  orderCode: string
  completedAt: string | null
  releaseAfter: string | null
  hasOpenReturn: boolean
  goods: number
  shopDiscount: number
  refundsBorne: number
  fixedFee: number
  paymentFee: number
  serviceFee: number
  net: number
  releasedAt: string | null
}

export interface Withdrawal {
  id: string
  amount: number
  status: 'Pending' | 'Processing' | 'Done' | 'Rejected'
  statusLabel: string
  bankCode: string
  accountLast4: string
  accountName: string
  rejectReason: string | null
  bankRef: string | null
  createdAt: string
  processedAt: string | null
}

export interface LedgerLine {
  id: string
  kind: string
  direction: 'Debit' | 'Credit'
  amount: number
  description: string
  postedAt: string
}

export interface Bank {
  code: string
  name: string
}

const base = (shopId: string) => `/seller/shops/${shopId}/finance`

const range = (from: string, to: string) => `from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`

export const financeApi = {
  summary: (shopId: string) => apiRequest<FinanceSummary>(`${base(shopId)}/summary`),
  pending: (shopId: string, page: number) => apiRequest<PagedResult<Earning>>(`${base(shopId)}/pending?page=${page}&pageSize=20`),
  released: (shopId: string, page: number) => apiRequest<PagedResult<Earning>>(`${base(shopId)}/released?page=${page}&pageSize=20`),
  transactions: (shopId: string, page: number) =>
    apiRequest<PagedResult<LedgerLine>>(`${base(shopId)}/transactions?account=ShopAvailable&page=${page}&pageSize=20`),
  withdrawals: (shopId: string, page: number) => apiRequest<PagedResult<Withdrawal>>(`${base(shopId)}/withdrawals?page=${page}&pageSize=20`),
  withdraw: (shopId: string, bankAccountId: string, amount: number) =>
    apiCommand<Withdrawal>(`${base(shopId)}/withdrawals`, { method: 'POST', body: { bankAccountId, amount } }),
  otp: (shopId: string) => apiCommand<{ resendAfterSeconds: number }>(`${base(shopId)}/otp`, { method: 'POST' }),
  addBank: (shopId: string, body: { bankCode: string; accountNo: string; accountName: string; otpCode: string; makeDefault: boolean }) =>
    apiCommand<string>(`${base(shopId)}/bank-accounts`, { method: 'POST', body }),
  setDefaultBank: (shopId: string, id: string, otpCode: string) =>
    apiCommand(`${base(shopId)}/bank-accounts/${id}/default`, { method: 'POST', body: { otpCode } }),
  removeBank: (shopId: string, id: string) => apiCommand(`${base(shopId)}/bank-accounts/${id}`, { method: 'DELETE' }),
  // The server's bank catalogue (D6) — the only list of banks
  banks: () => apiRequest<Bank[]>('/site/banks', { auth: false }),
  report: (shopId: string, from: string, to: string, format: 'Xlsx' | 'Pdf') => download(`${base(shopId)}/report?${range(from, to)}&format=${format}`),
  feeInvoice: (shopId: string, from: string, to: string) => download(`${base(shopId)}/fee-invoice?${range(from, to)}`),
}
