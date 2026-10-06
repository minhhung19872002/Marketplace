// Ví ShopHub: balance = the sum of its ledger entries on the server; top-ups go through the payment gateway.
import { apiCommand, apiRequest } from './http';
import type { PagedResult } from '../types';

export interface WalletEntry {
  id: string;
  kind: string;
  direction: 'Debit' | 'Credit';
  amount: number;
  description: string;
  refType: string;
  refId: string;
  postedAt: string;
}

export interface BankAccount {
  id: string;
  bankCode: string;
  accountNoLast4: string;
  accountName: string;
  verified: boolean;
  isDefault: boolean;
}

export interface Withdrawal {
  id: string;
  amount: number;
  status: 'Pending' | 'Processing' | 'Done' | 'Rejected';
  statusLabel: string;
  bankCode: string;
  accountLast4: string;
  accountName: string;
  rejectReason: string | null;
  createdAt: string;
  processedAt: string | null;
}

export interface WalletInfo {
  balance: number;
  pendingWithdrawals: number;
  hasPin: boolean;
  locked: boolean;
  bankAccounts: BankAccount[];
  history: PagedResult<WalletEntry>;
  withdrawals: Withdrawal[];
}

export interface Topup {
  id: string;
  amount: number;
  status: 'Pending' | 'Succeeded' | 'Failed' | 'Expired';
}

export const BANKS = [
  { code: 'VCB', name: 'Vietcombank' },
  { code: 'TCB', name: 'Techcombank' },
  { code: 'BIDV', name: 'BIDV' },
  { code: 'VTB', name: 'VietinBank' },
  { code: 'ACB', name: 'ACB' },
  { code: 'MB', name: 'MB Bank' },
  { code: 'TPB', name: 'TPBank' },
  { code: 'VPB', name: 'VPBank' },
];

export const walletApi = {
  get: (page: number) => apiRequest<WalletInfo>(`/wallet?page=${page}&pageSize=20`),
  otp: () => apiCommand<{ expiresInSeconds: number; resendAfterSeconds: number }>('/wallet/otp', { method: 'POST' }),
  setPin: (otpCode: string, pin: string) => apiCommand('/wallet/pin', { method: 'POST', body: { otpCode, pin } }),
  topup: (amount: number) => apiRequest<{ topupId: string; paymentId: string; redirectUrl: string }>('/wallet/topups', { method: 'POST', body: { amount } }),
  getTopup: (id: string) => apiRequest<Topup>(`/wallet/topups/${id}`),
  addBank: (body: { bankCode: string; accountNo: string; accountName: string; otpCode: string }) =>
    apiCommand<string>('/wallet/bank-accounts', { method: 'POST', body }),
  removeBank: (id: string) => apiCommand(`/wallet/bank-accounts/${id}`, { method: 'DELETE' }),
  withdraw: (bankAccountId: string, amount: number, pin: string) =>
    apiCommand<Withdrawal>('/wallet/withdrawals', { method: 'POST', body: { bankAccountId, amount, pin } }),
};
