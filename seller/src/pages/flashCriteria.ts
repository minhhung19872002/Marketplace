import type { CategoryNode } from '../api/seller'

/** Every category id (any level) → its name. */
export const categoryNames = (nodes: CategoryNode[], out = new Map<string, string>()): Map<string, string> => {
  for (const n of nodes) {
    out.set(n.id, n.name)
    categoryNames(n.children, out)
  }
  return out
}

/** All rules of a Flash Sale slot, the category rule included (D6). */
export const flashCriteriaText = (s: { minDiscountBp: number; minRating: number; categoryIds: string[] }, names: Map<string, string>): string => {
  const categories = s.categoryIds.length === 0 ? 'mọi ngành hàng' : `ngành: ${s.categoryIds.map((id) => names.get(id) ?? 'ngành đã ẩn').join(', ')}`
  return `Giảm ≥ ${s.minDiscountBp / 100}% · đánh giá ≥ ${s.minRating}★ · ${categories}`
}
