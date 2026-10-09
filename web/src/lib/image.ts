import type { SyntheticEvent } from 'react';

// Neutral SVG shown when a product photo is missing or fails to load
const svg = `<svg xmlns='http://www.w3.org/2000/svg' width='240' height='240'><rect width='240' height='240' fill='#f5f5f5'/><path d='M84 150l26-32 20 24 14-16 24 24z' fill='#d8d8d8'/><circle cx='100' cy='96' r='10' fill='#d8d8d8'/></svg>`;
export const PLACEHOLDER_IMAGE = `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;

export const imageOrPlaceholder = (url: string | null | undefined): string => url || PLACEHOLDER_IMAGE;

// Uploaded photos are stored in three widths: {key}_200.webp, {key}_600.webp, {key}_1200.webp (MediaAsset)
const SIZED = /_(200|600|1200)\.webp$/;
const WIDTHS = [200, 600, 1200] as const;

/** The stored variant of an uploaded photo closest to `width`; other URLs (emoji, signed, external) are kept. */
export const sizedImage = (url: string | null | undefined, width: (typeof WIDTHS)[number]): string =>
  url && SIZED.test(url) ? url.replace(SIZED, `_${width}.webp`) : imageOrPlaceholder(url);

/** `srcSet` over the three stored widths, so phones stop downloading the 1200 px photo for a card (G3, mobile LCP). */
export const imageSrcSet = (url: string | null | undefined): string | undefined =>
  url && SIZED.test(url) ? WIDTHS.map((w) => `${url.replace(SIZED, `_${w}.webp`)} ${w}w`).join(', ') : undefined;

/** onError handler: swap a broken image for the placeholder once (no loop if that fails too). */
export const handleImgError = (e: SyntheticEvent<HTMLImageElement>): void => {
  const img = e.currentTarget;
  if (img.src !== PLACEHOLDER_IMAGE) img.src = PLACEHOLDER_IMAGE;
};

/** Category icons are stored as an emoji or an image URL. */
export const isImageUrl = (icon: string | null | undefined): icon is string => !!icon && /^(https?:)?\/\//.test(icon);
