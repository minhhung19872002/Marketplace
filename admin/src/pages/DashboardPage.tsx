import { useState } from 'react'
import { Card, Col, Descriptions, Row, Space, Statistic, Tag, Typography } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { adminApi } from '../api/admin'
import { fetchHealth } from '../api/http'
import { reportsApi, type Kpis } from '../api/platform'
import ReportChart from '../components/ReportChart'
import RangePicker, { lastDays } from '../components/RangePicker'
import { P, can } from '../permissions'
import { formatNumber, formatPercentBp, formatPrice } from '../lib/money'
import { palette } from '../theme'

const HEALTH = {
  Healthy: { text: 'Hoạt động', color: 'success' },
  Unhealthy: { text: 'Lỗi', color: 'error' },
  Unreachable: { text: 'Không kết nối được', color: 'default' },
} as const

const KPI: { key: keyof Kpis; label: string; kind: 'money' | 'count' | 'bp'; lowerIsBetter?: boolean }[] = [
  { key: 'gmv', label: 'GMV', kind: 'money' },
  { key: 'orders', label: 'Số đơn', kind: 'count' },
  { key: 'newBuyers', label: 'Người dùng mới', kind: 'count' },
  { key: 'newShops', label: 'Shop mới', kind: 'count' },
  { key: 'cancelRateBp', label: 'Tỉ lệ huỷ', kind: 'bp', lowerIsBetter: true },
  { key: 'returnRateBp', label: 'Tỉ lệ trả hàng', kind: 'bp', lowerIsBetter: true },
  { key: 'feeRevenue', label: 'Doanh thu phí', kind: 'money' },
]

const show = (v: number, kind: 'money' | 'count' | 'bp') => (kind === 'money' ? formatPrice(v) : kind === 'bp' ? formatPercentBp(v) : formatNumber(v))

/** Change vs the previous period of the same length (spec VI.1). */
const Change = ({ now, before, lowerIsBetter }: { now: number; before: number; lowerIsBetter?: boolean }) => {
  if (before === 0) return <Typography.Text type="secondary">kỳ trước: 0</Typography.Text>
  const pct = Math.round(((now - before) * 1000) / before) / 10
  const good = lowerIsBetter ? pct <= 0 : pct >= 0
  return <span style={{ color: good ? palette.up : palette.down }}>{pct >= 0 ? '▲' : '▼'} {formatNumber(Math.abs(pct))}% so với kỳ trước</span>
}

const DashboardPage = ({ permissions }: { permissions: string[] }) => {
  const navigate = useNavigate()
  const [range, setRange] = useState(lastDays(30))
  const health = useQuery({ queryKey: ['health'], queryFn: fetchHealth, refetchInterval: 30_000 })
  const me = useQuery({ queryKey: ['me'], queryFn: adminApi.me })
  const canReports = can(permissions, P.ReportView)
  const overview = useQuery({ queryKey: ['overview', range], queryFn: () => reportsApi.overview(range), enabled: canReports })
  const o = overview.data

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Space style={{ justifyContent: 'space-between', width: '100%' }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>Tổng quan</Typography.Title>
        {canReports && <RangePicker value={range} onChange={setRange} />}
      </Space>
      {canReports && (
        <>
          <Row gutter={[12, 12]}>
            {KPI.map((k) => (
              <Col key={k.key} xs={12} md={8} xl={6} xxl={3}>
                <Card size="small" loading={overview.isLoading} data-testid={`kpi-${k.key}`}>
                  <Statistic title={k.label} value={o ? show(o.current[k.key], k.kind) : '—'} />
                  {o && <Change now={o.current[k.key]} before={o.previous[k.key]} lowerIsBetter={k.lowerIsBetter} />}
                </Card>
              </Col>
            ))}
          </Row>
          <Row gutter={[12, 12]}>
            <Col xs={24} xl={16}>
              <Card title={`GMV & số đơn — ${o?.period ?? ''}`}>
                {o && (
                  <ReportChart kind="line" series="GMV" series2="Số đơn"
                    points={o.gmvSeries.map((p, i) => ({ label: p.label, value: p.value, value2: o.orderSeries[i]?.value ?? 0 }))} />
                )}
              </Card>
            </Col>
            <Col xs={24} xl={8}>
              <Card title="Việc chờ xử lý">
                {o && (
                  <Descriptions column={1} size="small">
                    <Descriptions.Item label="Shop chờ duyệt"><a onClick={() => navigate('/shop')}>{o.pending.shopsToReview}</a></Descriptions.Item>
                    <Descriptions.Item label="Sản phẩm chờ duyệt"><a onClick={() => navigate('/duyet-san-pham')}>{o.pending.productsToReview}</a></Descriptions.Item>
                    <Descriptions.Item label="Khiếu nại chưa phân xử"><a onClick={() => navigate('/khieu-nai')}>{o.pending.openDisputes}</a></Descriptions.Item>
                    <Descriptions.Item label="Rút tiền chờ duyệt"><a onClick={() => navigate('/tai-chinh')}>{o.pending.pendingWithdrawals}</a></Descriptions.Item>
                    <Descriptions.Item label="Báo cáo sản phẩm"><a onClick={() => navigate('/bao-cao-san-pham')}>{o.pending.openProductReports}</a></Descriptions.Item>
                    <Descriptions.Item label="Báo cáo đánh giá"><a onClick={() => navigate('/bao-cao-danh-gia')}>{o.pending.openReviewReports}</a></Descriptions.Item>
                  </Descriptions>
                )}
              </Card>
            </Col>
          </Row>
        </>
      )}
      <Card title="Trạng thái hệ thống">
        <Space>
          <span>API:</span>
          {health.data ? (
            <Tag color={HEALTH[health.data].color} data-testid="api-health">{HEALTH[health.data].text}</Tag>
          ) : (
            <Tag>Đang kiểm tra…</Tag>
          )}
        </Space>
      </Card>
      {me.data && (
        <Card title="Tài khoản của bạn">
          <Descriptions column={1} size="small">
            <Descriptions.Item label="Họ tên">{me.data.fullName}</Descriptions.Item>
            <Descriptions.Item label="Vai trò">{me.data.roles.map((r) => <Tag key={r}>{r}</Tag>)}</Descriptions.Item>
            <Descriptions.Item label="Số quyền">{me.data.permissions.includes('*') ? 'Toàn quyền' : me.data.permissions.length}</Descriptions.Item>
          </Descriptions>
        </Card>
      )}
    </Space>
  )
}

export default DashboardPage
