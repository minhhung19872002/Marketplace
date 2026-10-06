import { Table } from 'antd'
import type { CellKind, ReportTable } from '../api/platform'
import { formatNumber, formatPercentBp, formatPrice } from '../lib/money'

export const formatCell = (value: string | number, kind: CellKind): string =>
  kind === 'Money' ? formatPrice(Number(value)) : kind === 'Integer' ? formatNumber(Number(value)) : kind === 'Percent' ? formatPercentBp(Number(value)) : String(value)

/** The report's own rows and totals — the same table the Excel / PDF export contains. */
const ReportTableView = ({ table }: { table: ReportTable }) => (
  <Table
    size="small"
    rowKey={(_, i) => String(i)}
    dataSource={table.rows.map((r) => ({ cells: r }))}
    pagination={table.rows.length > 50 ? { pageSize: 50 } : false}
    scroll={{ x: true }}
    columns={table.columns.map((c, i) => ({
      title: c.title,
      align: c.kind === 'Text' ? 'left' : 'right',
      render: (_: unknown, row: { cells: (string | number)[] }) => formatCell(row.cells[i], c.kind),
    }))}
    summary={() => table.totals ? (
      <Table.Summary.Row>
        {table.columns.map((c, i) => (
          <Table.Summary.Cell key={c.title} index={i} align={c.kind === 'Text' ? 'left' : 'right'}>
            <b>{formatCell(table.totals![i] ?? '', c.kind)}</b>
          </Table.Summary.Cell>
        ))}
      </Table.Summary.Row>
    ) : null}
    data-testid="report-table"
  />
)

export default ReportTableView
