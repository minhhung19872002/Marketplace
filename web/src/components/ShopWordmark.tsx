import { handleImgError } from '../lib/image';
import './ShopWordmark.css';

interface Props {
  name: string;
  wordmarkUrl?: string | null;
  height?: number;
  className?: string;
}

/**
 * A shop's wide logo, the mark and the full name on one line (G-VIS round 2): the same picture in the Mall tiles, on the
 * shop page and in the shop block of a product page. Contained, never cropped. A shop without one gets its name in bold.
 */
const ShopWordmark = ({ name, wordmarkUrl, height = 36, className = '' }: Props) => {
  const label = name.replace(/^Mall /, '');
  return wordmarkUrl ? (
    <img className={`shop-wordmark ${className}`} src={wordmarkUrl} alt={label} loading="lazy" height={height} width={Math.round((height * 10) / 3)}
      style={{ height }} onError={handleImgError} data-testid="shop-wordmark" />
  ) : (
    <span className={`shop-wordmark shop-wordmark--text ${className}`} style={{ lineHeight: `${height}px` }}>{label}</span>
  );
};

export default ShopWordmark;

interface ShopLogoProps {
  shop: { name: string; logoUrl?: string | null; wordmarkUrl?: string | null };
  /** The frame of the place (round avatar, square tile…): kept for a shop without a wide logo */
  className: string;
  height?: number;
}

/**
 * The shop's one logo (G-VIS round 3, L196): its wide logo when it has one — never next to its square / initial avatar —
 * else the square logo, else the initial. Used by the product page's shop block, the shop page, the related shop of a
 * search, the followed shops and the header suggestions.
 */
export const ShopLogo = ({ shop, className, height = 40 }: ShopLogoProps) => (shop.wordmarkUrl
  ? (
    <span className={`${className} shop-logo--wide`}>
      <ShopWordmark name={shop.name} wordmarkUrl={shop.wordmarkUrl} height={height} />
    </span>
  )
  : <span className={className}>{shop.logoUrl ? <img src={shop.logoUrl} alt="" onError={handleImgError} /> : shop.name.charAt(0)}</span>);
