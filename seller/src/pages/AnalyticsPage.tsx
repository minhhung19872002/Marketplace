import { useState } from 'react'
import { Alert, App, Button, Card, Col, DatePicker, Descriptions, Progress, Row, Segmented, Space, Statistic, Table, Tabs, Typography } from 'antd'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { analyticsApi, type Analytics, type Granularity, type PeriodFigures } from '../api/analytics'
import { ApiError } from '../api/http'
import { addDaysIso, vnTodayIso } from '../lib/datetime'
import { formatNumber, formatPercent, formatPrice } from '../lib/money'
import { palette } from '../theme'
import { ArrowUpOutlined, ArrowDownOutlined } from '@ant-design/icons'

const FIGURES: { key: keyof PeriodFigures; label: string; kind: 'money' | 'count' | 'bp' }[] = [
  { key: 'sales', label: 'Doanh số', kind: 'money' },
  { key: 'orders', label: 'Đơn hàng', kind: 'count' },
  { key: 'buyers', label: 'Người mua', kind: 'count' },
  { key: 'views', label: 'Lượt xem sản phẩm', kind: 'count' },
  { key: 'conversionBp', label: 'Tỉ lệ chuyển đổi', kind: 'bp' },
]

const show = (v: number, kind: 'money' | 'count' | 'bp') => (kind === 'money' ? formatPrice(v) : kind === 'bp' ? formatPercent(v / 100) : formatNumber(v))

const Change = ({ now, before }: { now: number; before: number }) => {
  if (before === 0) return <Typography.Text type="secondary">kỳ trước: 0</Typography.Text>
  const pct = Math.round(((now - before) * 1000) / before) / 10
  return <span className={`trend ${pct >= 0 ? 'trend-up' : 'trend-down'}`}>{pct >= 0 ? <ArrowUpOutlined /> : <ArrowDownOutlined />} {formatPercent(Math.abs(pct), 1)} so với kỳ trước</span>
}

const PENALTY_ALERT = { None: 'success', Restricted: 'info', CampaignBan: 'warning', Locked: 'error' } as const

const Performance = ({ data }: { data: Analytics }) => {
  const p = data.performance
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Alert type={PENALTY_ALERT[p.penalty.level]} showIcon message={`Điểm phạt hiện tại: ${p.penalty.points}`}
        description={`${p.penalty.consequence} Ngưỡng: hạn chế hiển thị từ ${p.penalty.restrictAt} điểm, cấm tham gia chiến dịch từ ${p.penalty.campaignBanAt} điểm, khoá shop ở ${p.penalty.lockAt} điểm.`} />
      <Descriptions bordered column={1} size="small">
        <Descriptions.Item label="Tỉ lệ đơn không thành công (shop huỷ, hệ thống huỷ, giao thất bại)">
          <Progress percent={p.failedRateBp / 100} size="small" style={{ maxWidth: 300 }} format={() => formatPercent(p.failedRateBp / 100)} />
        </Descriptions.Item>
        <Descriptions.Item label="Tỉ lệ giao hàng trễ hẹn">
          <Progress percent={p.lateDeliveryRateBp / 100} size="small" style={{ maxWidth: 300 }} format={() => formatPercent(p.lateDeliveryRateBp / 100)} />
        </Descriptions.Item>
        <Descriptions.Item label="Tỉ lệ phản hồi chat">{formatPercent(p.chatResponseRatePercent, 0)} — phản hồi {p.chatResponseTime}</Descriptions.Item>
      </Descriptions>
    </Space>
  )
}

/** Dữ liệu & phân tích (spec III.8): figures vs the previous period, trend, top products, traffic sources, performance. */
const AnalyticsPage = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const today = vnTodayIso()
  const [from, setFrom] = useState(addDaysIso(today, -29))
  const [to, setTo] = useState(today)
  const [granularity, setGranularity] = useState<Granularity>('Day')
  const data = useQuery({ queryKey: ['analytics', shopId, from, to, granularity], queryFn: () => analyticsApi.get(shopId, from, to, granularity), retry: false })
  const d = data.data

  const exportExcel = async () => {
    try {
      const blob = await analyticsApi.export(shopId, from, to, granularity)
      const a = document.createElement('a')
      a.href = URL.createObjectURL(blob)
      a.download = `du-lieu-ban-hang-${from}-${to}.xlsx`
      a.click()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : 'Không xuất được dữ liệu.')
    }
  }

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="middle">
      <Space style={{ justifyContent: 'space-between', width: '100%' }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>Dữ liệu & phân tích</Typography.Title>
        <Space wrap>
          <DatePicker.RangePicker format="DD/MM/YYYY" allowClear={false} value={[dayjs(from), dayjs(to)]}
            onChange={(v) => { if (v?.[0] && v[1]) { setFrom(v[0].format('YYYY-MM-DD')); setTo(v[1].format('YYYY-MM-DD')) } }} />
          <Segmented<Granularity> value={granularity} onChange={setGranularity}
            options={[{ value: 'Day', label: 'Ngày' }, { value: 'Week', label: 'Tuần' }, { value: 'Month', label: 'Tháng' }]} />
          <Button onClick={exportExcel} data-testid="analytics-export">Xuất Excel</Button>
        </Space>
      </Space>
      {data.isError && <Alert type="error" showIcon message={data.error instanceof ApiError ? data.error.message : 'Không tải được dữ liệu.'} />}
      {d && (
        <>
          <Typography.Text type="secondary">{d.period}</Typography.Text>
          <Row gutter={[12, 12]}>
            {FIGURES.map((f) => (
              <Col key={f.key} xs={12} md={8} xl={4}>
                <Card size="small" data-testid={`figure-${f.key}`}>
                  <Statistic title={f.label} value={show(d.current[f.key], f.kind)} />
                  <Change now={d.current[f.key]} before={d.previous[f.key]} />
                </Card>
              </Col>
            ))}
          </Row>
          <Card title="Xu hướng">
            <ResponsiveContainer width="100%" height={280}>
              <LineChart data={d.series.map((p) => ({ label: p.label, 'Doanh số': p.sales, 'Lượt xem': p.views }))}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="label" />
                <YAxis yAxisId="a" />
                <YAxis yAxisId="b" orientation="right" />
                <Tooltip formatter={(v: number) => formatNumber(v)} />
                <Legend />
                <Line yAxisId="a" type="monotone" dataKey="Doanh số" stroke={palette.primary} dot={false} />
                <Line yAxisId="b" type="monotone" dataKey="Lượt xem" stroke={palette.secondary} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </Card>
          <Tabs items={[
            {
              key: 'top', label: 'Sản phẩm bán chạy',
              children: (
                <Table rowKey="productId" size="small" pagination={false} dataSource={d.topProducts}
                  columns={[
                    { title: 'Sản phẩm', dataIndex: 'name' },
                    { title: 'Đã bán', dataIndex: 'sold', align: 'right' },
                    { title: 'Doanh số', dataIndex: 'sales', align: 'right', render: (v: number) => formatPrice(v) },
                    { title: 'Lượt xem', dataIndex: 'views', align: 'right' },
                    { title: 'Chuyển đổi', dataIndex: 'conversionBp', align: 'right', render: (v: number) => formatPercent(v / 100) },
                  ]} />
              ),
            },
            {
              key: 'traffic', label: 'Nguồn truy cập',
              children: (
                <ResponsiveContainer width="100%" height={260}>
                  <BarChart data={d.traffic.map((t) => ({ label: t.name, 'Lượt xem': t.views }))} layout="vertical">
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis type="number" />
                    <YAxis type="category" dataKey="label" width={200} />
                    <Tooltip formatter={(v: number) => formatNumber(v)} />
                    <Bar dataKey="Lượt xem" fill={palette.primary} />
                  </BarChart>
                </ResponsiveContainer>
              ),
            },
            { key: 'performance', label: 'Hiệu quả hoạt động', children: <Performance data={d} /> },
          ]} />
        </>
      )}
    </Space>
  )
}

export default AnalyticsPage
