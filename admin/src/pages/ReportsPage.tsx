import { useState } from 'react'
import { App, Button, Card, Select, Space, Typography } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { REPORTS, reportsApi, saveBlob, type ReportKind } from '../api/platform'
import ReportChart from '../components/ReportChart'
import ReportTableView from '../components/ReportTableView'
import RangePicker, { lastDays } from '../components/RangePicker'

/** VI.10 Báo cáo: every report as chart + table, exported as Excel or PDF from the same rows. */
const ReportsPage = () => {
  const { message } = App.useApp()
  const [kind, setKind] = useState<ReportKind>('GmvByTime')
  const [range, setRange] = useState(lastDays(30))
  const report = useQuery({ queryKey: ['report', kind, range], queryFn: () => reportsApi.report(kind, range), retry: false })

  const download = async (format: 'Xlsx' | 'Pdf') => {
    try {
      saveBlob(await reportsApi.export(kind, range, format), `bao-cao-${kind}-${range.from}-${range.to}.${format === 'Pdf' ? 'pdf' : 'xlsx'}`)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : 'Không xuất được báo cáo.')
    }
  }

  return (
    <Card
      title="Báo cáo"
      extra={
        <Space wrap>
          <Button onClick={() => download('Xlsx')} data-testid="export-xlsx">Xuất Excel</Button>
          <Button onClick={() => download('Pdf')}>Xuất PDF</Button>
        </Space>
      }
    >
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        <Space wrap>
          <Select<ReportKind> value={kind} onChange={setKind} style={{ width: 280 }} options={REPORTS.map((r) => ({ value: r.kind, label: r.label }))}
            data-testid="report-kind" />
          <RangePicker value={range} onChange={setRange} />
        </Space>
        {report.isError && <Typography.Text type="danger">{report.error instanceof ApiError ? report.error.message : 'Không tải được báo cáo.'}</Typography.Text>}
        {report.data && (
          <>
            <Typography.Text type="secondary">{report.data.table.subtitle}</Typography.Text>
            <ReportChart kind={report.data.chart.kind} series={report.data.chart.series} series2={report.data.chart.series2} points={report.data.chart.points} />
            <ReportTableView table={report.data.table} />
          </>
        )}
      </Space>
    </Card>
  )
}

export default ReportsPage
