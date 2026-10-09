import type { ReactElement } from 'react';
import { isImageUrl } from '../lib/image';
import CategoryIcon from './CategoryIcon';

// App-style, multi-colour glyphs of the home shortcuts (G-VIS). Drawn here, ShopHub's own artwork; the colours come from
// CSS classes (sc-*) so no colour code sits in TSX. Keys: the icon codes seeded by MarketingSeeder.Shortcuts, plus aliases.
const GLYPHS: Record<string, () => ReactElement> = {
  voucher: () => (
    <>
      <path className="sc-orange" d="M4 9.5A1.5 1.5 0 0 1 5.5 8h21A1.5 1.5 0 0 1 28 9.5V13a3 3 0 0 0 0 6v3.5a1.5 1.5 0 0 1-1.5 1.5h-21A1.5 1.5 0 0 1 4 22.5V19a3 3 0 0 0 0-6V9.5z" />
      <path className="sc-yellow" d="M20 8h2v16h-2z" opacity=".9" />
      <circle className="sc-white" cx="11" cy="13" r="1.8" />
      <circle className="sc-white" cx="16" cy="19" r="1.8" />
      <path className="sc-white-stroke" d="M16.5 12 10.5 20" strokeWidth="1.8" strokeLinecap="round" />
    </>
  ),
  freeship: () => (
    <>
      <path className="sc-green" d="M3 10a1.5 1.5 0 0 1 1.5-1.5h13A1.5 1.5 0 0 1 19 10v11H3V10z" />
      <path className="sc-teal" d="M19 13h5.2l3.8 4.6V21H19v-8z" />
      <path className="sc-white" d="M21 14.8h2.6l2.1 2.6H21z" />
      <circle className="sc-dark" cx="8.5" cy="22" r="2.8" />
      <circle className="sc-dark" cx="23" cy="22" r="2.8" />
      <circle className="sc-white" cx="8.5" cy="22" r="1" />
      <circle className="sc-white" cx="23" cy="22" r="1" />
      <path className="sc-white-stroke" d="M6.5 12.5h7M6.5 15.5h4.5" strokeWidth="1.6" strokeLinecap="round" />
    </>
  ),
  deal: () => (
    <>
      <circle className="sc-red" cx="16" cy="16" r="12" />
      <path className="sc-yellow" d="M17.8 5.5 9.5 17.5h6l-1.6 9 8.6-12.2h-6.1l1.4-8.8z" />
    </>
  ),
  mall: () => (
    <>
      <path className="sc-white sc-outline" d="M6 14h20v12.5a1.5 1.5 0 0 1-1.5 1.5h-17A1.5 1.5 0 0 1 6 26.5V14z" />
      <path className="sc-red" d="M5 6h22l2 7H3l2-7z" />
      <path className="sc-white" d="M10 6h3l-.6 7H8.6L10 6zm9 0h3l1.4 7h-3.8L19 6z" />
      <path className="sc-red" d="M13.5 19h5v9h-5z" />
      <path className="sc-red" d="M3 13h26a3.3 3.3 0 0 1-6.5 0 3.3 3.3 0 0 1-6.5 0 3.3 3.3 0 0 1-6.5 0A3.3 3.3 0 0 1 3 13z" opacity=".75" />
    </>
  ),
  preferred: () => (
    <>
      <path className="sc-pink" d="M16 27.5S4 20.6 4 12.3A6.3 6.3 0 0 1 16 9.4a6.3 6.3 0 0 1 12 2.9c0 8.3-12 15.2-12 15.2z" />
      <path className="sc-white" d="M9.6 10.2a3 3 0 0 1 3.4-1.1 1 1 0 0 1-.6 1.9 1 1 0 0 0-1.1.4 1 1 0 0 1-1.7-1.2z" opacity=".85" />
      <path className="sc-yellow" d="m24.5 3 1.1 2.4 2.4 1.1-2.4 1.1-1.1 2.4-1.1-2.4L21 6.5l2.4-1.1z" />
    </>
  ),
  star: () => (
    <>
      <path className="sc-yellow sc-outline-orange" d="m16 3.8 3.7 7.6 8.3 1.2-6 5.9 1.4 8.3L16 22.9l-7.4 3.9L10 18.5l-6-5.9 8.3-1.2z" />
      <path className="sc-orange" d="M16 9.2 18 13.4l4.6.7-3.3 3.2.8 4.6L16 19.7z" opacity=".55" />
    </>
  ),
  hot: () => (
    <>
      <path className="sc-orange" d="M16 28c-5.5 0-9.5-3.9-9.5-9 0-4.6 3-7.4 5-10.4.7 2.3 1.9 3.6 3.2 4.2C14.4 8.6 16.4 5.4 19.6 3c-.4 4.4 1.6 6.8 3.6 9.3 1.4 1.8 2.3 3.9 2.3 6.7 0 5.1-4 9-9.5 9z" />
      <path className="sc-yellow" d="M16 28c-2.9 0-5-2-5-4.8 0-2.6 1.8-4.1 3.1-6 .5 1.3 1.2 2 2 2.4.1-2 1.1-3.8 2.9-5.1-.1 2.6 1 4 2 5.3.7 1 1.1 2 1.1 3.4C22.1 26 19.6 28 16 28z" />
    </>
  ),
  new: () => (
    <>
      <path className="sc-blue" d="M5 11.5 16 6l11 5.5V23L16 28.5 5 23V11.5z" />
      <path className="sc-blue-dark" d="M16 17v11.5L5 23V11.5z" />
      <path className="sc-white" d="m5 11.5 11 5.5 11-5.5-1.8-.9L16 15.2l-9.2-4.6z" opacity=".55" />
      <path className="sc-yellow" d="m25 2 1.3 3 3 1.3-3 1.3-1.3 3-1.3-3-3-1.3 3-1.3z" />
    </>
  ),
  price: () => (
    <>
      <path className="sc-green" d="M4 6.5A2.5 2.5 0 0 1 6.5 4h8.3a2.5 2.5 0 0 1 1.8.7l10.7 10.7a2.5 2.5 0 0 1 0 3.6l-8 8a2.5 2.5 0 0 1-3.6 0L5 16.3a2.5 2.5 0 0 1-.7-1.8L4 6.5z" />
      <circle className="sc-white" cx="10" cy="10" r="2.4" />
      <path className="sc-white-stroke" d="m14.5 18.5 5-5M15 14h.01M19 18h.01" strokeWidth="2" strokeLinecap="round" />
    </>
  ),
  stock: () => (
    <>
      <path className="sc-brown" d="M4 10.5 15 6l11 4.5v12L15 27 4 22.5v-12z" />
      <path className="sc-brown-dark" d="M15 15v12L4 22.5v-12z" />
      <path className="sc-white" d="m4 10.5 11 4.5 11-4.5L15 6z" opacity=".35" />
      <circle className="sc-green sc-ring" cx="24" cy="23" r="6" />
      <path className="sc-white-stroke" d="m21.3 23 1.9 1.9 3.6-3.7" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
    </>
  ),
};

// Codes the admin may type for the same shortcut
const ALIASES: Record<string, string> = {
  rating: 'star', bestseller: 'hot', cheap: 'price', instock: 'stock',
  // Emoji codes of databases seeded before the icon codes (written as escapes: no emoji in the source)
  '\u{1F39F}\u{FE0F}': 'voucher', '\u{1F39F}': 'voucher', '\u{1F69A}': 'freeship', '\u{26A1}': 'deal', '\u{1F3EC}': 'mall',
  '\u{1F496}': 'preferred', '\u{2B50}': 'star', '\u{1F525}': 'hot', '\u{1F195}': 'new', '\u{1F3F7}\u{FE0F}': 'price',
  '\u{1F3F7}': 'price', '\u{1F4E6}': 'stock',
};

/** The shortcut's glyph: an uploaded image, one of the drawn glyphs, else the line icon of the code. */
const ShortcutIcon = ({ icon }: { icon: string | null | undefined }) => {
  if (isImageUrl(icon)) return <img src={icon} alt="" width={30} height={30} loading="lazy" />;
  const code = (icon ?? '').trim().toLowerCase();
  const glyph = GLYPHS[ALIASES[code] ?? code];
  if (!glyph) return <span className="sc-fallback"><CategoryIcon icon={icon} size={24} /></span>;
  return <svg className="sc-glyph" width="30" height="30" viewBox="0 0 32 32" aria-hidden>{glyph()}</svg>;
};

export default ShortcutIcon;
