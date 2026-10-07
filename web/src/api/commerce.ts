// Cart, checkout, payment and orders. Money is integer VND; every total shown comes from the server.
import { apiCommand, apiRequest } from './http';
import type { PagedResult } from '../types';

// ---------- cart ----------

export interface CartLine {
  skuId: string;
  productId: string;
  name: string;
  imageUrl: string | null;
  variant: string | null;
  price: number;
  originalPrice: number;
  previousPrice: number | null;
  quantity: number;
  available: number;
  isSelected: boolean;
  canBuy: boolean;
  problem: string | null;
  // When it was put in the cart (header preview: newest across shops)
  addedAt: string;
  // "Flash Sale" / "Giảm giá" when a price programme gives the price
  priceLabel: string | null;
}

export interface CartShop {
  shopId: string;
  shopName: string;
  shopSlug: string;
  isMall: boolean;
  onVacation: boolean;
  lines: CartLine[];
}

export interface Cart {
  shops: CartShop[];
  lineCount: number;
  totalQuantity: number;
  selectedQuantity: number;
  selectedSubtotal: number;
}

export const cartApi = {
  get: () => apiRequest<Cart>('/cart'),
  add: (skuId: string, quantity: number) => apiCommand<Cart>('/cart/items', { method: 'POST', body: { skuId, quantity } }),
  update: (skuId: string, change: { quantity?: number; selected?: boolean; skuId?: string }) =>
    apiRequest<Cart>(`/cart/items/${skuId}`, { method: 'PUT', body: change }),
  remove: (skuIds: string[]) => apiCommand<Cart>('/cart/items/remove', { method: 'POST', body: { skuIds } }),
  select: (selected: boolean, shopId?: string) => apiRequest<Cart>('/cart/selection', { method: 'PUT', body: { shopId: shopId ?? null, selected } }),
};

// ---------- checkout ----------

export type PaymentMethod = 'Cod' | 'Simulated' | 'Wallet' | 'VnPay' | 'MoMo' | 'ZaloPay';
// The way of paying at the gateway; instalments / pay later are run by the gateway, never by ShopHub
export type PaymentOption = 'Default' | 'DomesticCard' | 'InternationalCard' | 'QrCode' | 'Installment' | 'PayLater';
export type VoucherType = 'Amount' | 'Percent' | 'FreeShipping' | 'CoinCashback';

export interface CheckoutShopChoice {
  shopId: string;
  carrierCode?: string | null;
  voucherCode?: string | null;
  note?: string | null;
}

export interface CheckoutRequest {
  addressId: string | null;
  shops: CheckoutShopChoice[];
  platformVoucherCode: string | null;
  freeshipVoucherCode: string | null;
  useCoins: boolean;
  paymentMethod: PaymentMethod;
  paymentOption?: PaymentOption;
}

export interface ShippingOption {
  code: string;
  name: string;
  description: string | null;
  fee: number;
  days: number;
  expectedDate: string;
  supportsCod: boolean;
}

export interface VoucherOption {
  id: string;
  code: string;
  name: string;
  type: VoucherType;
  discountValue: number;
  discountPercentBp: number;
  maxDiscount: number | null;
  minOrder: number;
  endAt: string;
  discount: number;
  usable: boolean;
  problem: string | null;
  selected: boolean;
}

export interface QuoteLine {
  skuId: string;
  productId: string;
  name: string;
  imageUrl: string | null;
  variant: string | null;
  unitPrice: number;
  originalPrice: number;
  quantity: number;
  lineTotal: number;
  shopDiscount: number;
  platformDiscount: number;
  coinDiscount: number;
  comboDiscount: number;
  priceLabel: string | null;
}

export interface QuoteGift {
  skuId: string;
  name: string;
  variant: string | null;
  quantity: number;
  value: number;
}

export interface QuoteShop {
  shopId: string;
  shopName: string;
  isMall: boolean;
  lines: QuoteLine[];
  shippingOptions: ShippingOption[];
  carrierCode: string | null;
  subtotal: number;
  shopDiscount: number;
  shippingFee: number;
  shippingDiscount: number;
  platformDiscount: number;
  coinUsed: number;
  total: number;
  shopVoucher: VoucherOption | null;
  shopVoucherOptions: VoucherOption[];
  note: string | null;
  comboDiscount: number;
  gifts: QuoteGift[] | null;
  // Đa kho: one parcel per ship-from warehouse, each with its own carrier fee
  parcels: QuoteParcel[] | null;
}

export interface QuoteParcel {
  no: number;
  warehouseName: string;
  provinceCode: string;
  shippingFee: number;
  productIds: string[];
}

export interface CheckoutQuote {
  address: { id: string; receiverName: string; phone: string; fullAddress: string; provinceCode: string } | null;
  shops: QuoteShop[];
  platformVouchers: VoucherOption[];
  freeshipVouchers: VoucherOption[];
  coins: { balance: number; max: number; used: number; applied: boolean };
  paymentMethods: {
    code: PaymentMethod;
    name: string;
    available: boolean;
    reason: string | null;
    options: { code: PaymentOption; name: string; available: boolean; reason: string | null }[] | null;
  }[];
  paymentMethod: PaymentMethod;
  paymentOption: PaymentOption;
  subtotal: number;
  shopDiscount: number;
  shippingFee: number;
  shippingDiscount: number;
  platformDiscount: number;
  coinUsed: number;
  grandTotal: number;
  coinCashback: number;
  problems: string[];
  canPlace: boolean;
  comboDiscount: number;
}

export type OrderStatus =
  | 'PendingPayment'
  | 'PendingConfirmation'
  | 'ReadyToShip'
  | 'Shipping'
  | 'Delivered'
  | 'Completed'
  | 'Cancelled'
  | 'DeliveryFailed'
  | 'Returning'
  | 'Returned';

export interface CheckoutResult {
  checkoutId: string;
  status: 'AwaitingPayment' | 'Placed' | 'Expired';
  paymentMethod: PaymentMethod;
  grandTotal: number;
  paymentExpiresAt: string | null;
  orders: { id: string; code: string; shopId: string; shopName: string; status: OrderStatus; grandTotal: number }[];
  payment: {
    paymentId: string;
    method: PaymentMethod;
    status: 'Initiated' | 'Succeeded' | 'Failed' | 'Expired' | 'Refunded';
    amount: number;
    redirectUrl: string | null;
    expiresAt: string;
  } | null;
}

export const checkoutApi = {
  quote: (request: CheckoutRequest) => apiRequest<CheckoutQuote>('/checkout/quote', { method: 'POST', body: request }),
  // walletPin: the 6-digit Ví ShopHub PIN when paying from the wallet (never stored by the client)
  place: (idempotencyKey: string, checkout: CheckoutRequest, expectedGrandTotal: number, walletPin?: string) =>
    apiRequest<CheckoutResult>('/checkout', {
      method: 'POST',
      body: { checkout, expectedGrandTotal, walletPin: walletPin ?? null },
      headers: { 'Idempotency-Key': idempotencyKey },
    }),
  get: (id: string) => apiRequest<CheckoutResult>(`/checkout/${id}`),
  retryPayment: (id: string) => apiRequest<CheckoutResult>(`/checkout/${id}/pay`, { method: 'POST' }),
};

// ---------- simulated gateway (the fake payment page) ----------

export interface SimulatedPayment {
  paymentId: string;
  amount: number;
  description: string;
  expiresAt: string;
  status: 'Initiated' | 'Succeeded' | 'Failed' | 'Expired' | 'Refunded';
  checkoutId: string;
  // Order result page, or the wallet for a top-up
  returnPath: string;
  // The way chosen at checkout (thẻ, QR, trả góp…); null = the gateway's own page
  way: string | null;
}

export const gatewayApi = {
  view: (paymentId: string) => apiRequest<SimulatedPayment>(`/payments/simulated/${paymentId}`, { auth: false }),
  complete: (paymentId: string, outcome: 'success' | 'fail') =>
    apiRequest<SimulatedPayment>(`/payments/simulated/${paymentId}/${outcome}`, { method: 'POST', auth: false }),
};

// ---------- orders ----------

export type OrderTab = 'All' | 'AwaitingPayment' | 'Processing' | 'Shipping' | 'Completed' | 'Cancelled' | 'Returns';

export interface OrderItem {
  id: string;
  skuId: string;
  productId: string;
  name: string;
  variant: string | null;
  imageUrl: string | null;
  unitPrice: number;
  originalPrice: number;
  quantity: number;
  lineTotal: number;
  shopDiscount: number;
  platformDiscount: number;
  coinDiscount: number;
  paidAmount: number;
}

export interface OrderSummary {
  id: string;
  code: string;
  shopId: string;
  shopName: string;
  shopSlug: string;
  status: OrderStatus;
  statusLabel: string;
  paymentStatus: 'Unpaid' | 'Paid' | 'Refunded';
  paymentMethod: PaymentMethod;
  grandTotal: number;
  itemCount: number;
  firstItem: OrderItem | null;
  createdAt: string;
  checkoutId: string;
}

export interface ShipmentEvent {
  status: string;
  label: string;
  location: string | null;
  description: string;
  occurredAt: string;
}

export interface ShipmentInfo {
  id: string;
  trackingNo: string;
  carrierCode: string;
  carrierName: string | null;
  status: string;
  statusLabel: string;
  pickupMethod: 'Pickup' | 'DropOff';
  pickupSlot: string | null;
  codAmount: number;
  weightG: number;
  expectedDeliveryAt: string;
  events: ShipmentEvent[];
}

export interface CancelRequestInfo {
  id: string;
  reason: string;
  status: 'Pending' | 'Approved' | 'Rejected' | 'AutoApproved';
  createdAt: string;
  dueAt: string;
  rejectReason: string | null;
}

export interface OrderDetail {
  id: string;
  code: string;
  checkoutId: string;
  shopId: string;
  shopName: string;
  shopSlug: string;
  status: OrderStatus;
  statusLabel: string;
  paymentStatus: 'Unpaid' | 'Paid' | 'Refunded';
  paymentMethod: PaymentMethod;
  carrierCode: string;
  carrierName: string | null;
  expectedDeliveryDays: number;
  address: { receiverName: string; phone: string; fullAddress: string };
  buyerNote: string | null;
  items: OrderItem[];
  subtotal: number;
  shopDiscount: number;
  platformDiscount: number;
  shippingFee: number;
  shippingDiscount: number;
  coinUsed: number;
  grandTotal: number;
  cancelReason: string | null;
  createdAt: string;
  paymentExpiresAt: string | null;
  history: { from: OrderStatus | null; to: OrderStatus; toLabel: string; actor: string; reason: string | null; occurredAt: string }[];
  shipment: ShipmentInfo | null;
  parcels: { no: number; warehouseName: string; provinceName: string; itemIds: string[]; shippingFee: number; shipment: ShipmentInfo | null }[] | null;
  cancelRequest: CancelRequestInfo | null;
  actions: { pay: boolean; cancel: boolean; requestCancel: boolean; confirmReceived: boolean; buyAgain: boolean; review: boolean; return: boolean };
  autoCompleteAt: string | null;
  /** Other unpaid orders of the same payment: cancelling this one cancels them too */
  cancelsWith: string[] | null;
}

export const ordersApi = {
  list: (tab: OrderTab, q: string, page: number) =>
    apiRequest<PagedResult<OrderSummary>>(`/orders?tab=${tab}&page=${page}&pageSize=10${q ? `&q=${encodeURIComponent(q)}` : ''}`),
  get: (code: string) => apiRequest<OrderDetail>(`/orders/${encodeURIComponent(code)}`),
  cancel: (code: string, reason: string) => apiCommand(`/orders/${encodeURIComponent(code)}/cancel`, { method: 'POST', body: { reason } }),
  requestCancel: (code: string, reason: string) =>
    apiCommand(`/orders/${encodeURIComponent(code)}/cancel-request`, { method: 'POST', body: { reason } }),
  received: (code: string) => apiCommand(`/orders/${encodeURIComponent(code)}/received`, { method: 'POST' }),
  buyAgain: (code: string) =>
    apiCommand<{ added: number; skipped: string[] }>(`/orders/${encodeURIComponent(code)}/buy-again`, { method: 'POST' }),
};

export interface Tracking {
  trackingNo: string;
  carrierName: string;
  status: string;
  statusLabel: string;
  expectedDeliveryAt: string;
  events: ShipmentEvent[];
}

export const trackingApi = {
  get: (trackingNo: string) => apiRequest<Tracking>(`/tracking/${encodeURIComponent(trackingNo.trim())}`, { auth: false }),
};

// ---------- notifications ----------

export type NotificationCategory = 'Order' | 'Promotion' | 'Wallet' | 'Activity';

export interface AppNotification {
  id: string;
  category: NotificationCategory;
  title: string;
  body: string;
  link: string | null;
  isRead: boolean;
  createdAt: string;
}

export const notificationsApi = {
  list: (category: NotificationCategory | null, page: number) =>
    apiRequest<PagedResult<AppNotification>>(`/notifications?page=${page}&pageSize=20${category ? `&category=${category}` : ''}`),
  unread: () => apiRequest<{ total: number; byCategory: Partial<Record<NotificationCategory, number>> }>('/notifications/unread'),
  read: (id: string) => apiRequest<number>(`/notifications/${id}/read`, { method: 'POST' }),
  readAll: () => apiCommand<number>('/notifications/read-all', { method: 'POST' }),
};

// ---------- vouchers & coins ----------

export interface VoucherInfo {
  id: string;
  owner: 'Platform' | 'Shop';
  shopId: string | null;
  shopName: string | null;
  code: string;
  name: string;
  type: VoucherType;
  discountValue: number;
  discountPercentBp: number;
  maxDiscount: number | null;
  minOrder: number;
  startAt: string;
  endAt: string;
  totalQuota: number | null;
  usedCount: number;
  perUserLimit: number;
  state: string;
}

export interface WalletVoucher {
  voucher: VoucherInfo;
  claimed: boolean;
  usedByMe: number;
  problem: string | null;
}

export type WalletTab = 'Valid' | 'ExpiringSoon' | 'Used' | 'Expired';

export interface CoinWallet {
  balance: number;
  expiringSoon: number;
  history: PagedResult<{ id: string; delta: number; reason: string; note: string | null; createdAt: string; expiresAt: string | null }>;
}

export const walletApi = {
  available: (shopId?: string) => apiRequest<WalletVoucher[]>(shopId ? `/vouchers?shopId=${shopId}` : '/vouchers'),
  claim: (voucherId: string) => apiCommand(`/account/vouchers/${voucherId}/claim`, { method: 'POST' }),
  mine: (tab: WalletTab) => apiRequest<WalletVoucher[]>(`/account/vouchers?tab=${tab}`),
  coins: (page: number) => apiRequest<CoinWallet>(`/account/coins?page=${page}&pageSize=20`),
};
