import { useState, type ReactNode } from 'react'
import { Alert, Card, Col, Row, Skeleton, Space, Typography } from 'antd'
import {
  ArrowDownOutlined, ArrowUpOutlined, AuditOutlined, CommentOutlined, DollarOutlined, PercentageOutlined, RightOutlined, SafetyCertificateOutlined,
  ShopOutlined, ShoppingCartOutlined, StarOutlined, UserAddOutlined, WalletOutlined, WarningOutlined,
} from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { fetchHealth } from '../api/http'
import { reportsApi, type Kpis } from '../api/platform'
import ReportChart from '../components/ReportChart'
import RangePicker, { lastDays } from '../components/RangePicker'
import PageHeader from '../components/PageHeader'
import { ToneTag } from '../components/StatusTag'
import { P, can } from '../permissions'
import { formatNumber, formatPercentBp, formatPrice } from '../lib/money'

const HEALTH = {
  Healthy: { text: 'API hoạt động', tone: 'success' },
  Unhealthy: { text: 'API lỗi', tone: 'error' },
  Unreachable: { text: 'Không kết nối được API', tone: 'default' },
} as const

type Kind = 'money' | 'count' | 'bp'

const show = (v: number, kind: Kind) => (kind === 'money' ? formatPrice(v) : kind === 'bp' ? formatPercentBp(v) : formatNumber(v))

/** ↑ / ↓ change against the previous period of the same length (spec VI.1); green when it moved the good way. */
const Change = ({ now, before, lowerIsBetter }: { now: number; before: number; lowerIsBetter?: boolean }) => {
  if (before === 0) return <span className="trend trend-flat">Kỳ trước chưa có số liệu</span>
  const pct = Math.round(((now - before) * 1000) / before) / 10
  const good = lowerIsBetter ? pct <= 0 : pct >= 0
  return (
    <span className={`trend ${good ? 'trend-up' : 'trend-down'}`}>
      {pct >= 0 ? <ArrowUpOutlined aria-hidden /> : <ArrowDownOutlined aria-hidden />} {formatNumber(Math.abs(pct))}% so với kỳ trước
    </span>
  )
}

const StatCard = ({ title, icon, value, extra, loading, testId }: {
  title: string; icon: ReactNode; value: ReactNode; extra?: ReactNode; loading: boolean; testId: string
}) => (
  <Card className="stat-card" data-testid={testId}>
    <div className="stat-card-head">
      <span className="stat-card-title">{title}</span>
      <span className="stat-card-icon">{icon}</span>
    </div>
    {loading ? <Skeleton active paragraph={{ rows: 1 }} title={false} /> : (
      <>
        <div className="stat-card-value">{value}</div>
        <div className="stat-card-sub">{extra}</div>
      </>
    )}
  </Card>
)

const CARDS: { key: keyof Kpis; title: string; icon: ReactNode; kind: Kind }[] = [
  { key: 'gmv', title: 'GMV', icon: <DollarOutlined />, kind: 'money' },
  { key: 'orders', title: 'Đơn hàng', icon: <ShoppingCartOutlined />, kind: 'count' },
  { key: 'feeRevenue', title: 'Doanh thu phí', icon: <WalletOutlined />, kind: 'money' },
  { key: 'newBuyers', title: 'Người mua mới', icon: <UserAddOutlined />, kind: 'count' },
  { key: 'newShops', title: 'Shop mới', icon: <ShopOutlined />, kind: 'count' },
]

/** VI.1 Tổng quan: KPI vs the previous period, GMV & orders over time, the queues waiting for an operator. */
const DashboardPage = ({ permissions }: { permissions: string[] }) => {
  const navigate = useNavigate()
  const [range, setRange] = useState(lastDays(30))
  const health = useQuery({ queryKey: ['health'], queryFn: fetchHealth, refetchInterval: 30_000 })
  const canReports = can(permissions, P.ReportView)
  const overview = useQuery({ queryKey: ['overview', range], queryFn: () => reportsApi.overview(range), enabled: canReports })
  const o = overview.data
  const loading = canReports && overview.isPending

  const todo: { label: string; value: number | undefined; to: string; icon: ReactNode; permission: string; testId: string }[] = [
    { label: 'Shop chờ duyệt', value: o?.pending.shopsToReview, to: '/shop', icon: <ShopOutlined />, permission: P.ShopView, testId: 'todo-shops' },
    { label: 'Sản phẩm chờ duyệt', value: o?.pending.productsToReview, to: '/duyet-san-pham', icon: <SafetyCertificateOutlined />, permission: P.ProductReview, testId: 'todo-products' },
    { label: 'Khiếu nại chưa phân xử', value: o?.pending.openDisputes, to: '/khieu-nai', icon: <AuditOutlined />, permission: P.DisputeResolve, testId: 'todo-disputes' },
    { label: 'Rút tiền chờ duyệt', value: o?.pending.pendingWithdrawals, to: '/tai-chinh?tab=withdrawals', icon: <WalletOutlined />, permission: P.FinanceWithdrawalApprove, testId: 'todo-withdrawals' },
    { label: 'Báo cáo sản phẩm', value: o?.pending.openProductReports, to: '/bao-cao-san-pham', icon: <WarningOutlined />, permission: P.ProductBan, testId: 'todo-product-reports' },
    { label: 'Báo cáo đánh giá', value: o?.pending.openReviewReports, to: '/bao-cao-danh-gia', icon: <StarOutlined />, permission: P.ReviewModerate, testId: 'todo-review-reports' },
    // The overview does not count open chat reports yet: the tile only links to the queue
    { label: 'Chat bị báo cáo', value: o?.pending.openChatReports, to: '/bao-cao-chat', icon: <CommentOutlined />, permission: P.ChatReview, testId: 'todo-chat-reports' },
  ]

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <PageHeader title="Tổng quan" description="Số liệu so với kỳ trước cùng độ dài và việc đang chờ xử lý"
        actions={(
          <>
            {health.data && <span data-testid="api-health"><ToneTag tone={HEALTH[health.data].tone}>{HEALTH[health.data].text}</ToneTag></span>}
            {canReports && <RangePicker value={range} onChange={setRange} />}
          </>
        )} />
      {canReports && (
        <>
          {o?.warnings?.map((w) => (
            <Alert key={w} type="warning" showIcon message={w} data-testid="overview-warning"
              action={<a onClick={() => navigate('/tham-so')}>Mở tham số</a>} />
          ))}
          <Row gutter={[16, 16]}>
            {CARDS.map((c) => (
              <Col key={c.key} xs={24} md={12} xl={8}>
                <StatCard title={c.title} icon={c.icon} loading={loading} testId={`kpi-${c.key}`}
                  value={o ? show(o.current[c.key], c.kind) : '—'}
                  extra={o && <Change now={o.current[c.key]} before={o.previous[c.key]} />} />
              </Col>
            ))}
            <Col xs={24} md={12} xl={8}>
              <StatCard title="Tỉ lệ huỷ / trả hàng" icon={<PercentageOutlined />} loading={loading} testId="kpi-cancelRateBp"
                value={o ? `${formatPercentBp(o.current.cancelRateBp)} / ${formatPercentBp(o.current.returnRateBp)}` : '—'}
                extra={o && (
                  <>
                    <span>Huỷ <Change now={o.current.cancelRateBp} before={o.previous.cancelRateBp} lowerIsBetter /></span>
                    <span data-testid="kpi-returnRateBp">Trả <Change now={o.current.returnRateBp} before={o.previous.returnRateBp} lowerIsBetter /></span>
                  </>
                )} />
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} xl={16}>
              <Card title="GMV và số đơn" className="stat-card" extra={o && <Typography.Text type="secondary">{o.period}</Typography.Text>}>
                {loading ? <Skeleton active paragraph={{ rows: 6 }} /> : o && (
                  <ReportChart kind="line" series="GMV" series2="Số đơn" height={300}
                    points={o.gmvSeries.map((p, i) => ({ label: p.label, value: p.value, value2: o.orderSeries[i]?.value ?? 0 }))} />
                )}
              </Card>
            </Col>
            <Col xs={24} xl={8}>
              <Card title="Việc chờ xử lý" className="stat-card" data-testid="pending-tasks">
                <div className="todo-grid">
                  {todo.filter((t) => can(permissions, t.permission)).map((t) => {
                    const value = t.value ?? 0
                    return (
                      <Card key={t.label} size="small" hoverable onClick={() => navigate(t.to)} data-testid={t.testId}
                        className={`todo-card${value > 0 ? ' todo-card-alert' : ''}`}>
                        <span className="todo-card-icon">{t.icon}</span>
                        <span className="todo-card-body">
                          <span className="todo-card-label">{t.label}</span>
                          {t.value !== undefined ? <span className="todo-card-value">{loading ? '…' : formatNumber(value)}</span> : <RightOutlined aria-hidden />}
                        </span>
                      </Card>
                    )
                  })}
                </div>
              </Card>
            </Col>
          </Row>
        </>
      )}
      {!canReports && (
        <Card>
          <Typography.Text type="secondary">Tài khoản của bạn chưa có quyền xem số liệu kinh doanh. Dùng menu bên trái để mở các việc được giao.</Typography.Text>
        </Card>
      )}
    </Space>
  )
}

export default DashboardPage
