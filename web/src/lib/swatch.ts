// Colour swatches of variant chips (G-VIS round 2): a "Màu Sắc" option without its own photo shows a small dot in the
// colour its name says. Only CSS class names here — the colours are tokens (--sh-swatch-*), never hex codes in TSX.

const fold = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase();

// Two-word names first so that "Xanh Navy" wins over "Xanh"
const NAMES: [string, string][] = [
  ['xanh navy', 'navy'], ['xanh duong', 'blue'], ['xanh la', 'green'], ['xanh ngoc', 'teal'], ['xanh reu', 'olive'],
  ['tim man', 'plum'], ['do', 'red'], ['den', 'black'], ['trang', 'white'], ['xam', 'grey'], ['hong', 'pink'], ['tim', 'purple'],
  ['vang', 'yellow'], ['cam', 'orange'], ['nau', 'brown'], ['be', 'beige'], ['kem', 'beige'], ['bac', 'silver'], ['xanh', 'blue'],
];

/** True for a tier holding colours ("Màu", "Màu Sắc", "Color"). */
export const isColourTier = (tierName: string): boolean => /^(mau|color|colour)/.test(fold(tierName.trim()));

/** The swatch key of a colour name ("Xanh Navy" → "navy"), null when the name says no known colour. */
export const swatchOf = (value: string): string | null => {
  const words = ` ${fold(value).replace(/[^a-z0-9]+/g, ' ').trim()} `;
  return NAMES.find(([name]) => words.includes(` ${name} `))?.[1] ?? null;
};
