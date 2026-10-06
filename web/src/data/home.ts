// Static home-page marketing content. Banners and shortcuts become admin-managed in Phase 9 (marketing);
// colours are CSS tone classes (tone-*) defined in the component stylesheets.
import type { BannerSlide, FeatureShortcut } from '../types';

export const banners: BannerSlide[] = [
  { id: 1, title: 'Sinh Nhật ShopHub', subtitle: 'Hàng nghìn sản phẩm chính hãng giá tốt', tone: 'tone-primary', to: '/tim-kiem?sort=BestSelling' },
  { id: 2, title: 'ShopHub Mall', subtitle: 'Thương hiệu chính hãng — đổi trả 15 ngày', tone: 'tone-mall', to: '/tim-kiem?mall=true' },
  { id: 3, title: 'Công Nghệ Mới Về', subtitle: 'Điện thoại, laptop, phụ kiện mới nhất', tone: 'tone-tech', to: '/tim-kiem?q=dien+thoai&sort=Newest' },
];

export const featureShortcuts: FeatureShortcut[] = [
  { id: 'mall', label: 'ShopHub Mall', icon: '🏬', tone: 'tone-mall', to: '/tim-kiem?mall=true' },
  { id: 'preferred', label: 'Shop Yêu Thích', icon: '💖', tone: 'tone-primary', to: '/tim-kiem?preferred=true' },
  { id: 'four-star', label: 'Hàng 4 Sao', icon: '⭐', tone: 'tone-amber', to: '/tim-kiem?minRating=4' },
  { id: 'best', label: 'Bán Chạy', icon: '🔥', tone: 'tone-orange', to: '/tim-kiem?sort=BestSelling' },
  { id: 'new', label: 'Hàng Mới Về', icon: '🆕', tone: 'tone-teal', to: '/tim-kiem?sort=Newest' },
  { id: 'cheap', label: 'Giá Từ Thấp', icon: '🏷️', tone: 'tone-orange', to: '/tim-kiem?sort=PriceAsc' },
  { id: 'in-stock', label: 'Có Sẵn Hàng', icon: '📦', tone: 'tone-teal', to: '/tim-kiem?inStock=true' },
  { id: 'tech', label: 'Công Nghệ', icon: '💻', tone: 'tone-tech', to: '/danh-muc/may-tinh-laptop' },
  { id: 'beauty', label: 'Sắc Đẹp', icon: '💄', tone: 'tone-violet', to: '/danh-muc/sac-dep' },
  { id: 'home', label: 'Nhà Cửa', icon: '🏠', tone: 'tone-amber', to: '/danh-muc/nha-cua-doi-song' },
];
