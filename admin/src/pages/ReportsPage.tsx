import { useState } from 'react'
import { Alert, App, Button, Card, Select, Skeleton, Space, Typography } from 'antd'
import { FileExcelOutlined, FilePdfOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { REPORTS, reportsApi, saveBlob, type ReportKind } from '../api/platform'
import ReportChart from '../components/ReportChart'
import ReportTableView from '../components/ReportTableView'
import RangePicker, { lastDays } from '../components/RangePicker'
import PageHeader from '../components/PageHeader'

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

  const label = REPORTS.find((r) => r.kind === kind)?.label
  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <PageHeader title="Báo cáo" description="Mỗi báo cáo gồm biểu đồ và bảng số liệu; tệp Excel / PDF xuất từ đúng các dòng này"
        actions={(
          <>
            <Button type="primary" icon={<FileExcelOutlined aria-hidden />} onClick={() => download('Xlsx')} data-testid="export-xlsx">Xuất Excel</Button>
            <Button icon={<FilePdfOutlined aria-hidden />} onClick={() => download('Pdf')}>Xuất PDF</Button>
          </>
        )} />
      <Card className="data-card" styles={{ body: { padding: 0 } }}>
        <div className="filter-bar">
          <div className="filter-bar-left">
            <Select<ReportKind> value={kind} onChange={setKind} style={{ width: 300 }} options={REPORTS.map((r) => ({ value: r.kind, label: r.label }))}
              aria-label="Chọn báo cáo" data-testid="report-kind" />
            <RangePicker value={range} onChange={setRange} />
          </div>
        </div>
      </Card>
      {report.isError && <Alert type="error" showIcon message={report.error instanceof ApiError ? report.error.message : 'Không tải được báo cáo.'} />}
      {report.isPending && <Card className="stat-card"><Skeleton active paragraph={{ rows: 8 }} /></Card>}
      {report.data && (
        <>
          <Card className="stat-card" title={label} extra={<Typography.Text type="secondary">{report.data.table.subtitle}</Typography.Text>}>
            <ReportChart kind={report.data.chart.kind} series={report.data.chart.series} series2={report.data.chart.series2} points={report.data.chart.points} />
          </Card>
          <Card className="data-card" styles={{ body: { padding: 0 } }}>
            <ReportTableView table={report.data.table} />
          </Card>
        </>
      )}
    </Space>
  )
}

export default ReportsPage
