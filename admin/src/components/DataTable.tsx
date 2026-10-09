import { useEffect, useState, type ReactNode } from 'react'
import { Button, Card, Empty, Input, Skeleton, Table } from 'antd'
import type { TableProps } from 'antd'
import { UndoOutlined } from '@ant-design/icons'
import { ApiError } from '../api/http'
import { formatNumber } from '../lib/money'
import PageHeader, { type PageHeaderProps } from './PageHeader'

/** Server-side paging: the page the API returned and its total. */
export interface DataTablePaging {
  page: number
  pageSize: number
  total: number | undefined
  onChange: (page: number, pageSize: number) => void
  /** Let the operator choose 10 / 20 / 50 / 100 rows. */
  sizeChanger?: boolean
}

export interface DataTableSearch {
  /** The committed term (Enter / search button); the box keeps its own draft until then. */
  value: string
  onSearch: (value: string) => void
  placeholder: string
  testId?: string
  width?: number
}

export interface DataTableProps<T> extends Omit<TableProps<T>, 'pagination' | 'title' | 'loading' | 'locale' | 'footer'> {
  /** Page title, description and primary action — omit inside a tab (the page has its own header). */
  header?: PageHeaderProps
  search?: DataTableSearch
  /** Selects / segmented / checkboxes after the search box. */
  filters?: ReactNode
  /** Shows "Đặt lại": back to the default filters. */
  onReset?: () => void
  /** Right side of the filter bar (the primary action of a tab). */
  actions?: ReactNode
  /** Under the filter bar, above the rows (bulk-selection bar, notices). */
  toolbar?: ReactNode
  /** Server paging; 'client' pages the rows already loaded (20 a page); false shows every row. */
  paging?: DataTablePaging | 'client' | false
  /** Nothing loaded yet: skeleton rows instead of the table. */
  loading?: boolean
  /** Reloading rows already shown (filter / page change): the table dims under a spinner. */
  fetching?: boolean
  error?: unknown
  emptyText?: string
  testId?: string
}

const totalText = (total: number, range: [number, number]) =>
  total === 0 ? '0 / 0' : `${formatNumber(range[0])}–${formatNumber(range[1])} / ${formatNumber(total)}`

/**
 * The one list pattern of the admin app: page header, filter bar (search + filters + "Đặt lại"), server paging with the
 * total ("1–20 / 356"), sticky column headers, empty / loading / error states. Wide tables scroll inside the card, never
 * the page.
 */
function DataTable<T extends object>({
  header, search, filters, onReset, actions, toolbar, paging = false, loading, fetching, error, emptyText, testId, scroll, columns, ...table
}: DataTableProps<T>) {
  // The row-actions column (key 'actions') stays pinned on the right while a wide table scrolls
  const pinned = columns?.map((c) => (c.key === 'actions' ? { fixed: 'right' as const, ...c } : c))
  const [draft, setDraft] = useState(search?.value ?? '')
  // "Đặt lại" / a link clears the committed term: show it in the box too
  useEffect(() => setDraft(search?.value ?? ''), [search?.value])

  const hasBar = !!(search || filters || onReset || actions)
  const pagination: TableProps<T>['pagination'] = paging === false ? false
    : paging === 'client' ? { pageSize: 20, showSizeChanger: false, showTotal: totalText, size: 'default', hideOnSinglePage: true }
    : {
        current: paging.page,
        pageSize: paging.pageSize,
        total: paging.total ?? 0,
        showSizeChanger: paging.sizeChanger ?? false,
        pageSizeOptions: [10, 20, 50, 100],
        showTotal: totalText,
        onChange: paging.onChange,
      }
  const errorText = error ? (error instanceof ApiError ? error.message : 'Không tải được dữ liệu.') : null

  return (
    <div className="data-table" data-testid={testId}>
      {header && <PageHeader {...header} />}
      <Card className="data-card" styles={{ body: { padding: 0 } }}>
        {hasBar && (
          <div className="filter-bar">
            <div className="filter-bar-left">
              {search && (
                <Input.Search allowClear placeholder={search.placeholder} value={draft} style={{ width: search.width ?? 300 }}
                  onChange={(e) => setDraft(e.target.value)}
                  onSearch={(v) => search.onSearch(v.trim())} aria-label={search.placeholder} data-testid={search.testId} />
              )}
              {filters}
              {onReset && <Button icon={<UndoOutlined aria-hidden />} onClick={onReset}>Đặt lại</Button>}
            </div>
            {actions && <div className="filter-bar-right">{actions}</div>}
          </div>
        )}
        {toolbar && <div className="table-toolbar">{toolbar}</div>}
        {loading ? (
          <div className="table-skeleton" aria-busy="true" aria-label="Đang tải">
            <Skeleton active title={false} paragraph={{ rows: 8, width: '100%' }} />
          </div>
        ) : (
          <Table<T>
            {...table}
            columns={pinned}
            loading={fetching}
            pagination={pagination}
            sticky={{ offsetHeader: 64 }}
            scroll={{ x: 'max-content', ...scroll }}
            locale={{
              emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={errorText ?? emptyText ?? 'Không có dữ liệu'} />,
            }}
          />
        )}
      </Card>
    </div>
  )
}

export default DataTable
