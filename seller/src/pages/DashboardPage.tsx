import type { ReactNode } from 'react'
import { Card, Col, List, Row, Skeleton, Typography } from 'antd'
import {
  AlertOutlined, ArrowDownOutlined, ArrowUpOutlined, CalendarOutlined, CarOutlined, CheckCircleOutlined, ClockCircleOutlined, CloseCircleOutlined,
  DollarOutlined, InboxOutlined, LineChartOutlined, RiseOutlined, RollbackOutlined, StopOutlined, WarningOutlined,
} from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { ordersApi, type SalesFigure } from '../api/orders'
import { formatNumber, formatPercent, formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'

/** ↑ / ↓ change against the previous period; "—" when that period had nothing; nothing at all when the API gives no previous figure. */
const Change = ({ now, before }: { now: number; before: number | undefined }) => {
  if (before === undefined) return null
  if (before === 0) return <span className="trend trend-flat" title="Kỳ trước chưa có doanh số">— so với kỳ trước</span>
  const pct = ((now - before) * 100) / before
  const up = pct >= 0
  return (
    <span className={`trend ${up ? 'trend-up' : 'trend-down'}`}>
      {up ? <ArrowUpOutlined /> : <ArrowDownOutlined />} {formatPercent(Math.abs(pct), 1)} so với kỳ trước
    </span>
  )
}

const SalesCard = ({ title, icon, figure, previous, loading }: {
  title: string; icon: ReactNode; figure: SalesFigure | undefined; previous: SalesFigure | undefined; loading: boolean
}) => (
  <Card className="stat-card" data-testid="sales-card">
    <div className="stat-card-head">
      <span className="stat-card-title">{title}</span>
      <span className="stat-card-icon">{icon}</span>
    </div>
    {loading ? <Skeleton active paragraph={{ rows: 1 }} title={false} /> : (
      <>
        <div className="stat-card-value">{formatPrice(figure?.revenue ?? 0)}</div>
        <div className="stat-card-sub">
          <span>{formatNumber(figure?.orders ?? 0)} đơn hàng</span>
          <Change now={figure?.revenue ?? 0} before={previous?.revenue} />
        </div>
        <Typography.Text type="secondary" className="stat-card-traffic" data-testid="traffic">
          {formatNumber(figure?.views ?? 0)} lượt xem · {formatNumber(figure?.visitors ?? 0)} người xem · chuyển đổi {formatPercent((figure?.conversionBp ?? 0) / 100)}
        </Typography.Text>
      </>
    )}
  </Card>
)

/** Bảng điều khiển (spec III.2): việc cần làm + doanh số. */
const DashboardPage = ({ shopId }: { shopId: string }) => {
  const navigate = useNavigate()
  const { data, isPending } = useQuery({ queryKey: ['dashboard', shopId], queryFn: () => ordersApi.dashboard(shopId), refetchInterval: 60_000 })
  // `alert`: a non-zero value needs the seller's attention (shown in the warning tone)
  const todo: { label: string; value: number | undefined; to: string; testId: string; icon: ReactNode; alert?: boolean }[] = [
    { label: 'Chờ xác nhận', value: data?.toConfirm, to: '/don-hang?tab=ToConfirm', testId: 'todo-confirm', icon: <ClockCircleOutlined /> },
    { label: 'Chờ lấy hàng', value: data?.toShip, to: '/don-hang?tab=ToShip', testId: 'todo-ship', icon: <InboxOutlined /> },
    { label: 'Đang giao', value: data?.shipping, to: '/don-hang?tab=Shipping', testId: 'todo-shipping', icon: <CarOutlined /> },
    { label: 'Yêu cầu huỷ', value: data?.cancelRequests, to: '/don-hang?tab=CancelRequests', testId: 'todo-cancel', icon: <CloseCircleOutlined />, alert: true },
    { label: 'Giao thất bại / hoàn', value: data?.deliveryProblems, to: '/don-hang?tab=Failed', testId: 'todo-failed', icon: <AlertOutlined />, alert: true },
    { label: 'Sản phẩm bị khoá', value: data?.bannedProducts, to: '/san-pham?tab=Violation', testId: 'todo-banned', icon: <StopOutlined />, alert: true },
    { label: `Sắp hết hàng (≤ ${formatNumber(data?.lowStockThreshold ?? 0)})`, value: data?.lowStockProducts, to: '/san-pham?tab=LowStock', testId: 'todo-low', icon: <WarningOutlined />, alert: true },
    { label: 'Trả hàng chờ xử lý', value: data?.returnsPending, to: '/tra-hang', testId: 'todo-returns', icon: <RollbackOutlined />, alert: true },
    { label: 'Đã xử lý hôm nay', value: data?.processedToday, to: '/don-hang?tab=ToShip', testId: 'todo-processed', icon: <CheckCircleOutlined /> },
  ]
  const sales = [
    { title: 'Doanh số hôm nay', icon: <DollarOutlined />, figure: data?.today, previous: data?.yesterday },
    { title: 'Doanh số 7 ngày', icon: <RiseOutlined />, figure: data?.last7Days, previous: data?.previous7Days },
    { title: 'Doanh số 30 ngày', icon: <CalendarOutlined />, figure: data?.last30Days, previous: data?.previous30Days },
  ]
  return (
    <div className="dashboard">
      <div className="page-head">
        <Typography.Title level={3} style={{ margin: 0 }}>Bảng điều khiển</Typography.Title>
        <Typography.Text type="secondary">Tình hình bán hàng và những việc cần xử lý của shop</Typography.Text>
      </div>
      <Row gutter={[16, 16]}>
        {sales.map((s) => (
          <Col key={s.title} xs={24} md={8}>
            <SalesCard {...s} loading={isPending} />
          </Col>
        ))}
      </Row>
      <Card title="Việc cần làm" style={{ marginTop: 16 }}>
        <div className="todo-grid">
          {todo.map((t) => {
            const value = t.value ?? 0
            return (
              <Card key={t.label} size="small" hoverable onClick={() => navigate(t.to)} data-testid={t.testId}
                className={`todo-card${value > 0 ? (t.alert ? ' todo-card-alert' : ' todo-card-active') : ''}`}>
                <span className="todo-card-icon">{t.icon}</span>
                <span className="todo-card-body">
                  <span className="todo-card-value">{formatNumber(value)}</span>
                  <span className="todo-card-label">{t.label}</span>
                </span>
              </Card>
            )
          })}
        </div>
      </Card>
      <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
        <Col xs={24} lg={8}>
          <Card title="Hiệu quả hoạt động" extra={<LineChartOutlined />}>
            <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
              Điểm phạt hiện tại: <b data-testid="penalty-points">{data?.penaltyPoints ?? 0}</b>
            </Typography.Paragraph>
            <Typography.Link onClick={() => navigate('/phan-tich')}>Xem dữ liệu & phân tích</Typography.Link>
          </Card>
        </Col>
        <Col xs={24} lg={16}>
          <Card title="Thông báo của sàn" data-testid="announcements">
            <List
              size="small"
              dataSource={data?.announcements ?? []}
              locale={{ emptyText: 'Chưa có thông báo' }}
              renderItem={(a) => (
                <List.Item>
                  <List.Item.Meta title={a.title} description={<>{a.body}<br /><Typography.Text type="secondary">{formatDateTime(a.createdAt)}</Typography.Text></>} />
                </List.Item>
              )}
            />
          </Card>
        </Col>
      </Row>
    </div>
  )
}

export default DashboardPage
