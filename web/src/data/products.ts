import type { SyntheticEvent } from 'react';
// Mock data cho sàn TMĐT theo mô hình nghiệp vụ 
// - Không cần backend. Ảnh sản phẩm dùng ảnh thật nền trắng (dữ liệu demo),
//   có gallery + fallback SVG khi mạng lỗi.
import { PRODUCT_IMAGES } from './images';
import type {
  BannerSlide,
  Category,
  FeatureShortcut,
  FlashSaleProduct,
  MallBrand,
  Product,
  Review,
  VariantSet,
} from '../types';

export { formatPrice, formatSold } from '../lib/money';
export { removeTones } from '../lib/text';

// Ảnh SVG data-URI dự phòng (khi ảnh thật lỗi mạng)
const placeholder = (label: string, bg = '#f5f5f5', fg = '#bdbdbd'): string => {
  const svg = `<svg xmlns='http://www.w3.org/2000/svg' width='240' height='240'>
    <rect width='240' height='240' fill='${bg}'/>
    <text x='50%' y='50%' font-family='Arial' font-size='18' fill='${fg}'
      text-anchor='middle' dominant-baseline='middle'>${label}</text>
  </svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
};

// Ảnh thật khớp từ khóa, khóa theo id để ổn định giữa các lần load
const photoUrl = (keyword: string, id: number): string => `https://loremflickr.com/400/400/${keyword}?lock=${id}`;

export const categories: Category[] = [
  { id: 'thoi-trang-nam', name: 'Thời Trang Nam', icon: '👔' },
  { id: 'thoi-trang-nu', name: 'Thời Trang Nữ', icon: '👗' },
  { id: 'dien-thoai', name: 'Điện Thoại & Phụ Kiện', icon: '📱' },
  { id: 'may-tinh', name: 'Máy Tính & Laptop', icon: '💻' },
  { id: 'thiet-bi-dien-tu', name: 'Thiết Bị Điện Tử', icon: '🎧' },
  { id: 'may-anh', name: 'Máy Ảnh & Quay Phim', icon: '📷' },
  { id: 'dong-ho', name: 'Đồng Hồ', icon: '⌚' },
  { id: 'giay-nam', name: 'Giày Dép Nam', icon: '👞' },
  { id: 'giay-nu', name: 'Giày Dép Nữ', icon: '👠' },
  { id: 'tui-vi-nu', name: 'Túi Ví Nữ', icon: '👜' },
  { id: 'me-be', name: 'Mẹ & Bé', icon: '🍼' },
  { id: 'nha-cua', name: 'Nhà Cửa & Đời Sống', icon: '🏠' },
  { id: 'sac-dep', name: 'Sắc Đẹp', icon: '💄' },
  { id: 'suc-khoe', name: 'Sức Khỏe', icon: '💊' },
  { id: 'the-thao', name: 'Thể Thao & Du Lịch', icon: '⚽' },
  { id: 'o-to-xe-may', name: 'Ô Tô & Xe Máy', icon: '🏍️' },
  { id: 'do-choi', name: 'Đồ Chơi', icon: '🧸' },
  { id: 'bach-hoa', name: 'Bách Hóa Online', icon: '🛒' },
];

// Dãy tính năng nhanh (shortcut tròn) dưới banner
export const featureShortcuts: FeatureShortcut[] = [
  { id: 'ma-giam-gia', label: 'Mã Giảm Giá', icon: '🎟️', color: '#ee4d2d' },
  { id: 'hang-gia-hoi', label: 'Hàng Chọn Giá Hời', icon: '🏷️', color: '#f69113' },
  { id: 'nap-the', label: 'Nạp Thẻ & Dịch Vụ', icon: '💳', color: '#26aa99' },
  { id: 'mall', label: 'ShopHub Mall', icon: '🏬', color: '#d0011b' },
  { id: 'hang-4-sao', label: 'Hàng 4 Sao', icon: '⭐', color: '#eb9b00' },
  { id: 'deal-soc', label: 'Deal Sốc Hôm Nay', icon: '⚡', color: '#ee4d2d' },
  { id: 'voucher-xtra', label: 'Voucher Xtra', icon: '🎫', color: '#7c3aed' },
  { id: 'freeship', label: 'Freeship 0Đ', icon: '🚚', color: '#00bfa5' },
  { id: 'hang-hieu', label: 'Thương Hiệu Xịn', icon: '💎', color: '#3b82f6' },
  { id: 'tra-gop', label: 'Mua Trước Trả Sau', icon: '📆', color: '#f43f5e' },
];

// Thương hiệu trong khu ShopHub Mall
export const mallBrands: MallBrand[] = [
  { id: 'techzone', name: 'TechZone', keyword: 'electronics' },
  { id: 'fashionista', name: 'Fashionista', keyword: 'fashion' },
  { id: 'homelux', name: 'HomeLux', keyword: 'furniture' },
  { id: 'beautypro', name: 'BeautyPro', keyword: 'cosmetics' },
  { id: 'sportking', name: 'SportKing', keyword: 'sport' },
  { id: 'babycare', name: 'BabyCare', keyword: 'baby' },
];

export const banners: BannerSlide[] = [
  { id: 1, title: 'Sale 12.12 Sinh Nhật ShopHub', subtitle: 'Giảm đến 50% + Freeship 0đ', bg: 'linear-gradient(135deg,#ee4d2d,#ff7337)' },
  { id: 2, title: 'Voucher Xtra 100.000đ', subtitle: 'Áp dụng cho đơn từ 0đ', bg: 'linear-gradient(135deg,#d0011b,#f53d2d)' },
  { id: 3, title: 'Deal Công Nghệ Chớp Nhoáng', subtitle: 'Laptop - Điện thoại giảm sốc', bg: 'linear-gradient(135deg,#2b6cb0,#4299e1)' },
];

// [tên hiển thị, từ khóa ảnh, nhóm biến thể] — nghiệp vụ: nhiều SP có phân loại (màu/size)
const CATALOG: [string, string, VariantGroup][] = [
  ['Áo Thun Nam Cotton Cao Cấp', 'tshirt', 'apparel'],
  ['Đầm Nữ Dáng Suông Thời Trang', 'dress', 'apparel'],
  ['Điện Thoại Smartphone 128GB', 'smartphone', 'phone'],
  ['Tai Nghe Bluetooth Chống Ồn', 'headphones', 'color'],
  ['Giày Sneaker Unisex Đế Cao', 'sneakers', 'shoe'],
  ['Balo Laptop Chống Nước 15.6"', 'backpack', 'color'],
  ['Đồng Hồ Nam Dây Thép Không Gỉ', 'watch', 'color'],
  ['Son Kem Lì Lâu Trôi 8H', 'lipstick', 'shade'],
  ['Nồi Chiên Không Dầu 5L', 'airfryer', 'none'],
  ['Chuột Không Dây Gaming RGB', 'computer-mouse', 'color'],
  ['Bàn Phím Cơ Switch Blue', 'keyboard', 'color'],
  ['Kem Chống Nắng SPF50+', 'cosmetics', 'none'],
  ['Quần Jean Nam Slim Fit', 'jeans', 'apparel'],
  ['Túi Xách Nữ Da PU Cao Cấp', 'handbag', 'color'],
  ['Sạc Dự Phòng 20000mAh', 'powerbank', 'color'],
  ['Bình Giữ Nhiệt Inox 500ml', 'bottle', 'color'],
  ['Máy Xay Sinh Tố Đa Năng', 'blender', 'none'],
  ['Ốp Lưng Điện Thoại Trong Suốt', 'phone-case', 'phone'],
  ['Áo Khoác Hoodie Nỉ Bông', 'hoodie', 'apparel'],
  ['Loa Bluetooth Mini Cầm Tay', 'speaker', 'color'],
  ['Kính Mát Chống Tia UV', 'sunglasses', 'color'],
  ['Thảm Yoga Chống Trượt 6mm', 'yoga-mat', 'color'],
  ['Đèn LED Học Bài Bảo Vệ Mắt', 'desk-lamp', 'color'],
  ['Bộ Nồi Inox 3 Đáy Cao Cấp', 'cookware', 'none'],
  ['Áo Sơ Mi Nam Dài Tay Công Sở', 'shirt', 'apparel'],
  ['Chân Váy Nữ Xếp Ly', 'skirt', 'apparel'],
  ['Máy Ảnh Mirrorless 24MP', 'camera', 'none'],
  ['Đồng Hồ Thông Minh Smartwatch', 'smartwatch', 'color'],
  ['Giày Cao Gót Nữ 7cm', 'heels', 'shoe'],
  ['Ví Da Nam Cầm Tay', 'wallet', 'color'],
  ['Bỉm Tã Em Bé Siêu Thấm', 'diaper', 'none'],
  ['Gối Cao Su Non Memory Foam', 'pillow', 'none'],
  ['Nước Hoa Nữ Hương Hoa Cỏ', 'perfume', 'none'],
  ['Vitamin Tổng Hợp 60 Viên', 'vitamins', 'none'],
  ['Vợt Cầu Lông Carbon Siêu Nhẹ', 'badminton', 'none'],
  ['Mũ Bảo Hiểm Fullface', 'helmet', 'color'],
  ['Bộ Lego Lắp Ráp 500 Mảnh', 'lego', 'none'],
  ['Gạo ST25 Thơm Dẻo Túi 5kg', 'rice', 'none'],
  ['Chuột & Bàn Phím Combo Văn Phòng', 'keyboard', 'color'],
  ['Tai Nghe Nhét Tai Thể Thao', 'earbuds', 'color'],
];

const SHOP_NAMES = [
  'ShopHub Official Store', 'TechZone Official', 'Fashion House VN', 'Gia Dụng Thông Minh',
  'Beauty Corner', 'Sport Center', 'Mẹ & Bé Happy', 'Digital World', 'Home Living Store',
];

const LOCATIONS = ['TP. Hồ Chí Minh', 'Hà Nội', 'Đà Nẵng', 'Bình Dương', 'Hải Phòng'];

// Bộ biến thể theo nhóm (nghiệp vụ phân loại hàng )
type VariantGroup = 'apparel' | 'color' | 'shoe' | 'shade' | 'phone' | 'none';

const VARIANT_SETS: Record<VariantGroup, VariantSet | null> = {
  apparel: { label: 'Kích Cỡ', options: ['S', 'M', 'L', 'XL', 'XXL'] },
  color: { label: 'Màu Sắc', options: ['Đen', 'Trắng', 'Xanh', 'Đỏ', 'Hồng'] },
  shoe: { label: 'Size', options: ['38', '39', '40', '41', '42', '43'] },
  shade: { label: 'Tông Màu', options: ['Đỏ Cam', 'Đỏ Đô', 'Hồng Đất', 'Cam Nâu'] },
  phone: { label: 'Dung Lượng', options: ['64GB', '128GB', '256GB', '512GB'] },
  none: null,
};

// Sinh ngẫu nhiên tất định (seed theo index) để dữ liệu ổn định
const seeded = (i: number): number => {
  const x = Math.sin(i * 99.71) * 10000;
  return x - Math.floor(x);
};

const REVIEW_TEXTS = [
  'Sản phẩm rất tốt, đúng như mô tả. Giao hàng nhanh, đóng gói cẩn thận!',
  'Chất lượng ổn trong tầm giá, shop tư vấn nhiệt tình. Sẽ ủng hộ tiếp.',
  'Hàng đẹp y hình, giao nhanh. Rất hài lòng, 5 sao cho shop nha!',
  'Dùng ok, mọi thứ đều đạt yêu cầu. Cảm ơn shop đã tặng kèm quà.',
  'Giá hợp lý, chất lượng xứng đáng. Đóng gói kỹ, không móp méo.',
];

const REVIEWER_NAMES = ['nguyen****an', 'tra****my', 'hoang****88', 'le****92', 'phamm****k', 'minh****huy'];

const buildReviews = (id: number, rating: number): Review[] => {
  const count = 3 + Math.floor(seeded(id + 20) * 3);
  return Array.from({ length: count }, (_, k) => ({
    id: `${id}-${k}`,
    name: REVIEWER_NAMES[(id + k) % REVIEWER_NAMES.length],
    rating: Math.max(3, Math.round(rating - seeded(id + k + 30))),
    text: REVIEW_TEXTS[(id + k) % REVIEW_TEXTS.length],
    date: `2024-${String(1 + ((id + k) % 12)).padStart(2, '0')}-${String(1 + ((id * 3 + k) % 27)).padStart(2, '0')}`,
  }));
};

export const products: Product[] = CATALOG.map(([name, keyword, variantGroup], i) => {
  const id = i + 1;
  const originalPrice = 50000 + Math.round(seeded(i) * 195) * 10000;
  const discount = 10 + Math.round(seeded(i + 1) * 60);
  const price = Math.round((originalPrice * (100 - discount)) / 100 / 1000) * 1000;
  const sold = Math.round(seeded(i + 2) * 12000);
  const rating = Math.min(Number((4 + seeded(i + 3)).toFixed(1)), 5);
  const category = categories[Math.floor(seeded(i + 4) * categories.length)];
  const variant = VARIANT_SETS[variantGroup] || null;
  const stock = 20 + Math.round(seeded(i + 9) * 480);
  const imgEntry = PRODUCT_IMAGES[String(id) as keyof typeof PRODUCT_IMAGES] as
    | { thumb: string; gallery: string[] }
    | undefined;
  const fallbackImage = placeholder(name.split(' ').slice(0, 2).join(' '));
  return {
    id,
    name,
    image: imgEntry?.thumb || photoUrl(keyword, id),
    gallery: imgEntry?.gallery && imgEntry.gallery.length ? imgEntry.gallery : [imgEntry?.thumb || photoUrl(keyword, id)],
    fallbackImage,
    keyword,
    price,
    originalPrice,
    discount,
    sold,
    rating,
    ratingCount: Math.round(sold * (0.2 + seeded(i + 10) * 0.5)),
    liked: Math.round(seeded(i + 11) * 5000),
    stock,
    location: LOCATIONS[i % LOCATIONS.length],
    categoryId: category.id,
    categoryName: category.name,
    shopName: SHOP_NAMES[i % SHOP_NAMES.length],
    isMall: seeded(i + 5) > 0.6,
    isPreferred: seeded(i + 12) > 0.5, // "Yêu Thích" - shop uy tín
    freeship: seeded(i + 6) > 0.4,
    hasVoucher: seeded(i + 13) > 0.5,
    variant, // { label, options } hoặc null
    reviews: buildReviews(id, rating),
    specs: [
      ['Danh mục', category.name],
      ['Kho', String(stock)],
      ['Gửi từ', LOCATIONS[i % LOCATIONS.length]],
      ['Thương hiệu', 'No Brand'],
      ['Xuất xứ', 'Việt Nam'],
    ],
    description:
      `${name} - Sản phẩm chính hãng, chất lượng cao, được hàng nghìn khách hàng tin dùng. ` +
      `Thiết kế tinh tế, chất liệu bền đẹp, phù hợp sử dụng hằng ngày. ` +
      `Cam kết đổi trả trong 7 ngày nếu lỗi do nhà sản xuất. ` +
      `Giao hàng toàn quốc, hỗ trợ thanh toán khi nhận hàng (COD).`,
  };
});

// Flash sale = các sản phẩm giảm giá sâu nhất
export const flashSaleProducts: FlashSaleProduct[] = [...products]
  .sort((a, b) => b.discount - a.discount)
  .slice(0, 8)
  .map((p) => ({
    ...p,
    flashStock: 10 + Math.round(seeded(p.id + 7) * 90),
    flashSold: Math.round(seeded(p.id + 8) * 100),
  }));

export const getProductById = (id: string | number | undefined): Product | undefined => products.find((p) => p.id === Number(id));

// Khi ảnh thật lỗi (mạng chậm/chặn) -> đổi sang ảnh SVG dự phòng, tránh vỡ ảnh
export const handleImgError = (e: SyntheticEvent<HTMLImageElement>, fallback?: string): void => {
  const img = e.currentTarget;
  if (fallback && img.src !== fallback) {
    img.src = fallback;
  }
};

