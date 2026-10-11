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
