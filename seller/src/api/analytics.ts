// Dữ liệu & phân tích of the shop (spec III.8)
import { apiRequest } from './http'
import { download } from './orders'

export type Granularity = 'Day' | 'Week' | 'Month'

export interface PeriodFigures { sales: number; orders: number; buyers: number; views: number; conversionBp: number }

export interface Analytics {
  period: string
  current: PeriodFigures
  previous: PeriodFigures
  series: { label: string; sales: number; orders: number; buyers: number; views: number }[]
  topProducts: { productId: string; name: string; sold: number; sales: number; views: number; conversionBp: number }[]
  traffic: { source: string; name: string; views: number; shareBp: number }[]
  performance: {
    failedRateBp: number
    lateDeliveryRateBp: number
    chatResponseRatePercent: number
    chatResponseTime: string
    penalty: { points: number; level: 'None' | 'Restricted' | 'CampaignBan' | 'Locked'; consequence: string; restrictAt: number; campaignBanAt: number; lockAt: number }
  }
}

const q = (from: string, to: string, granularity: Granularity) => `from=${from}&to=${to}&granularity=${granularity}`

export const analyticsApi = {
  get: (shopId: string, from: string, to: string, granularity: Granularity) =>
    apiRequest<Analytics>(`/seller/shops/${shopId}/analytics?${q(from, to, granularity)}`),
  export: (shopId: string, from: string, to: string, granularity: Granularity) =>
    download(`/seller/shops/${shopId}/analytics/export?${q(from, to, granularity)}`),
}
