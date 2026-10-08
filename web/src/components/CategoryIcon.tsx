import {
  Baby, BadgePercent, Bike, Camera, Dumbbell, Flame, Footprints, Headphones, Heart, House, Laptop, LayoutGrid, Package,
  PackageCheck, Pill, Shirt, ShoppingBag, ShoppingBasket, Smartphone, Sparkles, Star, Store, Tag, Ticket, ToyBrick, Truck,
  Watch, Zap, type LucideIcon,
} from 'lucide-react';
import { isImageUrl } from '../lib/image';

// Category icons are stored as an image URL or a short code (historically an emoji, set by the admin). Known codes map
// to an SVG icon so the storefront never draws emoji; anything else falls back to a neutral grid icon.
// Keys are written as escapes so no emoji character sits in the source.
const BY_CODE: Record<string, LucideIcon> = {
  '\u{1F454}': Shirt, '\u{1F457}': Shirt, '\u{1F4F1}': Smartphone, '\u{1F4BB}': Laptop, '\u{1F3A7}': Headphones,
  '\u{1F4F7}': Camera, '\u{231A}': Watch, '\u{1F45E}': Footprints, '\u{1F460}': Footprints, '\u{1F45C}': ShoppingBag,
  '\u{1F37C}': Baby, '\u{1F3E0}': House, '\u{1F484}': Sparkles, '\u{1F48A}': Pill, '\u{26BD}': Dumbbell,
  '\u{1F3CD}\u{FE0F}': Bike, '\u{1F3CD}': Bike, '\u{1F9F8}': ToyBrick, '\u{1F6D2}': ShoppingBasket,
  shirt: Shirt, phone: Smartphone, laptop: Laptop, audio: Headphones, camera: Camera, watch: Watch, shoes: Footprints,
  bag: ShoppingBag, baby: Baby, home: House, beauty: Sparkles, health: Pill, sport: Dumbbell, motor: Bike, toy: ToyBrick,
  grocery: ShoppingBasket,
  // Home shortcuts (Mã giảm giá, Freeship, Deal sốc, Mall…)
  '\u{1F39F}\u{FE0F}': Ticket, '\u{1F39F}': Ticket, '\u{1F69A}': Truck, '\u{26A1}': Zap, '\u{1F3EC}': Store, '\u{1F496}': Heart,
  '\u{2B50}': Star, '\u{1F525}': Flame, '\u{1F195}': Sparkles, '\u{1F3F7}\u{FE0F}': Tag, '\u{1F3F7}': Tag, '\u{1F4E6}': PackageCheck,
  voucher: Ticket, freeship: Truck, deal: Zap, mall: Store, preferred: Heart, star: Star, hot: Flame, new: Sparkles,
  price: Tag, stock: PackageCheck, sale: BadgePercent, package: Package,
};

export const categoryIconFor = (code: string | null | undefined): LucideIcon => (code && BY_CODE[code.trim()]) || LayoutGrid;

const CategoryIcon = ({ icon, size = 28 }: { icon: string | null | undefined; size?: number }) => {
  if (isImageUrl(icon)) return <img src={icon} alt="" width={size} height={size} loading="lazy" />;
  const Icon = categoryIconFor(icon);
  return <Icon size={size} strokeWidth={1.6} aria-hidden />;
};

export default CategoryIcon;
