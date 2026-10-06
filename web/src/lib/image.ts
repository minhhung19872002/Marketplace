import type { SyntheticEvent } from 'react';

// Neutral SVG shown when a product photo is missing or fails to load
const svg = `<svg xmlns='http://www.w3.org/2000/svg' width='240' height='240'><rect width='240' height='240' fill='#f5f5f5'/><path d='M84 150l26-32 20 24 14-16 24 24z' fill='#d8d8d8'/><circle cx='100' cy='96' r='10' fill='#d8d8d8'/></svg>`;
export const PLACEHOLDER_IMAGE = `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;

export const imageOrPlaceholder = (url: string | null | undefined): string => url || PLACEHOLDER_IMAGE;

/** onError handler: swap a broken image for the placeholder once (no loop if that fails too). */
export const handleImgError = (e: SyntheticEvent<HTMLImageElement>): void => {
  const img = e.currentTarget;
  if (img.src !== PLACEHOLDER_IMAGE) img.src = PLACEHOLDER_IMAGE;
};

/** Category icons are stored as an emoji or an image URL. */
export const isImageUrl = (icon: string | null | undefined): icon is string => !!icon && /^(https?:)?\/\//.test(icon);
