// Static pages, help center, site identity (spec II.12) and product reports (II.4).
import { apiCommand, apiRequest } from './http';

export interface CmsPage { slug: string; title: string; content: string; topic: string | null; kind: 'Page' | 'Help'; updatedAt: string }

export interface HelpItem { slug: string; title: string; topic: string | null; kind: 'Page' | 'Help' }

export interface SiteInfo {
  platformName: string;
  hotline: string;
  supportEmail: string;
  legalName: string;
  legalAddress: string;
  taxCode: string;
  businessLicense: string;
  // Only networks whose https link is set (SITE.SOCIAL_*)
  social: { name: string; url: string }[];
  // Zalo Official Account id (SITE.ZALO_OA_ID); null = no Zalo share button
  zaloOaId: string | null;
}

export type ProductReportReason = 'Counterfeit' | 'Prohibited' | 'WrongInfo' | 'Offensive' | 'IntellectualProperty' | 'Other';

export const REPORT_REASONS: { value: ProductReportReason; label: string }[] = [
  { value: 'Counterfeit', label: 'Hàng giả, hàng nhái' },
  { value: 'Prohibited', label: 'Hàng cấm' },
  { value: 'WrongInfo', label: 'Thông tin sai lệch' },
  { value: 'Offensive', label: 'Nội dung phản cảm' },
  { value: 'IntellectualProperty', label: 'Vi phạm sở hữu trí tuệ' },
  { value: 'Other', label: 'Khác' },
];

export const contentApi = {
  page: (slug: string) => apiRequest<CmsPage>(`/cms/${encodeURIComponent(slug)}`, { auth: false }),
  help: (q: string) => apiRequest<HelpItem[]>(`/help${q ? `?q=${encodeURIComponent(q)}` : ''}`, { auth: false }),
  site: () => apiRequest<SiteInfo>('/site', { auth: false }),
  reportProduct: (productId: string, reason: ProductReportReason, details: string | null) =>
    apiCommand(`/products/${productId}/reports`, { method: 'POST', body: { reason, details } }),
};
