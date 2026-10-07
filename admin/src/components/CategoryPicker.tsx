import { TreeSelect } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { catalogApi, type CategoryNode } from '../api/catalog'

interface TreeOption { value: string; title: string; children: TreeOption[] }

const toTree = (nodes: CategoryNode[]): TreeOption[] => nodes.map((n) => ({ value: n.id, title: n.name, children: toTree(n.children) }))

interface Props {
  value?: string[]
  onChange?: (ids: string[]) => void
  /** Vouchers match the product's own (leaf) category: ticking a parent then yields its leaves */
  leavesOnly?: boolean
  placeholder?: string
}

/** Several categories from the admin tree (form control for AntD Form.Item). */
const CategoryPicker = ({ value, onChange, leavesOnly = false, placeholder = 'Mọi ngành hàng' }: Props) => {
  const categories = useQuery({ queryKey: ['admin-categories'], queryFn: catalogApi.categories })
  return (
    <TreeSelect
      value={value ?? []}
      onChange={(ids: string[]) => onChange?.(ids)}
      treeData={toTree(categories.data ?? [])}
      loading={categories.isLoading}
      treeCheckable
      showCheckedStrategy={leavesOnly ? TreeSelect.SHOW_CHILD : TreeSelect.SHOW_PARENT}
      showSearch
      treeNodeFilterProp="title"
      allowClear
      placeholder={placeholder}
      style={{ width: '100%' }}
      data-testid="category-picker"
    />
  )
}

export default CategoryPicker
