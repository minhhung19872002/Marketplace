// Canonical buyer-site URLs (spec II.4) — the server's sitemap and crawler pages use the same shape.

/** /san-pham/{slug}-i.{shopId}.{productId} */
export const productPath = (slug: string, shopId: string, id: string): string => `/san-pham/${slug}-i.${shopId}.${id}`;

/** The product id at the end of /san-pham/{id} or the canonical form; null when there is none. */
export const productIdOf = (key: string): string | null => /([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i.exec(key)?.[1] ?? null;
