import './Illustration.css';

/**
 * Small drawings for empty / missing pages (G5-B): flat shapes in the brand tokens (`.sh-art-*` classes, no colour in
 * the markup), so they follow the palette. Decorative: hidden from screen readers.
 */

/** An empty shopping cart on a soft disc, with a few sparkles. */
export const EmptyCartArt = ({ size = 160 }: { size?: number }) => (
  <svg className="sh-art" width={size} height={size * 0.8} viewBox="0 0 200 160" aria-hidden="true">
    <ellipse className="sh-art-ground" cx="100" cy="142" rx="70" ry="9" />
    <circle className="sh-art-soft" cx="100" cy="74" r="62" />
    <path className="sh-art-line" d="M42 44h16l14 62h66l12-44H64" />
    <circle className="sh-art-fill" cx="80" cy="122" r="8" />
    <circle className="sh-art-fill" cx="130" cy="122" r="8" />
    <path className="sh-art-line sh-art-thin" d="M86 78h40M92 92h28" />
    <path className="sh-art-spark" d="M156 30v12M150 36h12M38 92v8M34 96h8M150 92v6M147 95h6" />
  </svg>
);

/** A magnifier over a torn page: the page asked for is not here (404). */
export const NotFoundArt = ({ size = 200 }: { size?: number }) => (
  <svg className="sh-art" width={size} height={size * 0.8} viewBox="0 0 200 160" aria-hidden="true">
    <ellipse className="sh-art-ground" cx="100" cy="144" rx="74" ry="9" />
    <circle className="sh-art-soft" cx="100" cy="74" r="64" />
    <path className="sh-art-paper" d="M58 30h62l22 22v58l-8-6-8 6-8-6-8 6-8-6-8 6-8-6-8 6-8-6-8 6z" />
    <path className="sh-art-line sh-art-thin" d="M120 30v22h22M72 62h44M72 76h52M72 90h30" />
    <circle className="sh-art-lens" cx="132" cy="104" r="20" />
    <path className="sh-art-line" d="M146 118l18 18" />
    <path className="sh-art-mark" d="M126 98l12 12M138 98l-12 12" />
  </svg>
);

/** A phone showing the storefront in miniature: header band, banner, product tiles (app download page). */
export const PhoneArt = ({ size = 220 }: { size?: number }) => (
  <svg className="sh-art sh-art--phone" width={size * 0.62} height={size} viewBox="0 0 124 200" aria-hidden="true">
    <rect className="sh-art-device" x="4" y="2" width="116" height="196" rx="18" />
    <rect className="sh-art-screen" x="11" y="12" width="102" height="176" rx="10" />
    <rect className="sh-art-fill" x="11" y="12" width="102" height="26" rx="10" />
    <rect className="sh-art-paper" x="19" y="21" width="62" height="9" rx="4.5" />
    <rect className="sh-art-soft" x="19" y="46" width="86" height="34" rx="6" />
    <path className="sh-art-line sh-art-thin" d="M28 58h30M28 68h20" />
    <rect className="sh-art-tile" x="19" y="88" width="41" height="44" rx="5" />
    <rect className="sh-art-tile" x="64" y="88" width="41" height="44" rx="5" />
    <rect className="sh-art-tile" x="19" y="136" width="41" height="44" rx="5" />
    <rect className="sh-art-tile" x="64" y="136" width="41" height="44" rx="5" />
    <path className="sh-art-mark" d="M24 124h22M69 124h18M24 172h16M69 172h22" />
  </svg>
);
