// Permission codes the admin UI gates on (mirror of backend Application/Security/Permissions.cs)
export const P = {
  All: '*',
  SystemParameterView: 'SYS.PARAMETER.VIEW',
  SystemParameterUpdate: 'SYS.PARAMETER.UPDATE',
  AuditLogView: 'SYS.AUDIT.VIEW',
  UserView: 'IAM.USER.VIEW',
  UserLock: 'IAM.USER.LOCK',
  UserAssignRole: 'IAM.USER.ASSIGN_ROLE',
  RoleView: 'IAM.ROLE.VIEW',
  RoleManage: 'IAM.ROLE.MANAGE',
  CategoryManage: 'CATALOG.CATEGORY.MANAGE',
  BrandManage: 'CATALOG.BRAND.MANAGE',
  ProductReview: 'CATALOG.PRODUCT.REVIEW',
  ProductBan: 'CATALOG.PRODUCT.BAN',
  ShopView: 'SHOP.SHOP.VIEW',
  ShopReview: 'SHOP.SHOP.REVIEW',
  ShopLock: 'SHOP.SHOP.LOCK',
  ShopLabel: 'SHOP.SHOP.LABEL',
} as const

export const can = (permissions: readonly string[] | undefined, code: string): boolean =>
  !!permissions && (permissions.includes(P.All) || permissions.includes(code))
