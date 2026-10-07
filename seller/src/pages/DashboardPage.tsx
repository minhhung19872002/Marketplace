import { Card, Col, List, Row, Statistic, Typography } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { ordersApi } from '../api/orders'
import { formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'

/** Bảng điều khiển (spec III.2): việc cần làm + doanh số. */
const DashboardPage = ({ shopId }: { shopId: string }) => {
  const navigate = useNavigate()
  const { data } = useQuery({ queryKey: ['dashboard', shopId], queryFn: () => ordersApi.dashboard(shopId), refetchInterval: 60_000 })
  const todo: { label: string; value: number | undefined; to: string; testId: string }[] = [
    { label: 'Chờ xác nhận', value: data?.toConfirm, to: '/don-hang?tab=ToConfirm', testId: 'todo-confirm' },
    { label: 'Chờ lấy hàng', value: data?.toShip, to: '/don-hang?tab=ToShip', testId: 'todo-ship' },
    { label: 'Đang giao', value: data?.shipping, to: '/don-hang?tab=Shipping', testId: 'todo-shipping' },
    { label: 'Yêu cầu huỷ', value: data?.cancelRequests, to: '/don-hang?tab=CancelRequests', testId: 'todo-cancel' },
    { label: 'Giao thất bại / hoàn', value: data?.deliveryProblems, to: '/don-hang?tab=Failed', testId: 'todo-failed' },
    { label: 'Sản phẩm bị khoá', value: data?.bannedProducts, to: '/san-pham?tab=Banned', testId: 'todo-banned' },
    { label: `Sắp hết hàng (≤ ${data?.lowStockThreshold ?? 0})`, value: data?.lowStockSkus, to: '/san-pham?tab=LowStock', testId: 'todo-low' },
    { label: 'Trả hàng chờ xử lý', value: data?.returnsPending, to: '/tra-hang', testId: 'todo-returns' },
    { label: 'Đã xử lý hôm nay', value: data?.processedToday, to: '/don-hang?tab=ToShip', testId: 'todo-processed' },
  ]
  return (
    <>
      <Card title="Việc cần làm">
        <Row gutter={[16, 16]}>
          {todo.map((t) => (
            <Col key={t.label} xs={12} md={6} lg={3} style={{ minWidth: 150 }}>
              <Card size="small" hoverable onClick={() => navigate(t.to)} data-testid={t.testId}>
                <Statistic title={t.label} value={t.value ?? 0} />
              </Card>
            </Col>
          ))}
        </Row>
      </Card>
      <Card title="Doanh số" style={{ marginTop: 16 }}>
        <Row gutter={16}>
          {([['Hôm nay', data?.today], ['7 ngày', data?.last7Days], ['30 ngày', data?.last30Days]] as const).map(([label, f]) => (
            <Col key={label} span={8}>
              <Statistic title={`Doanh số ${label}`} value={formatPrice(f?.revenue ?? 0)} />
              <Typography.Text type="secondary">{f?.orders ?? 0} đơn hàng</Typography.Text>
              <br />
              <Typography.Text type="secondary" data-testid="traffic">
                {f?.views ?? 0} lượt xem · {f?.visitors ?? 0} người xem · chuyển đổi {((f?.conversionBp ?? 0) / 100).toFixed(2)}%
              </Typography.Text>
            </Col>
          ))}
        </Row>
        <Typography.Paragraph type="secondary" style={{ marginTop: 12 }}>
          Điểm phạt hiện tại: <b data-testid="penalty-points">{data?.penaltyPoints ?? 0}</b>
        </Typography.Paragraph>
      </Card>
      <Card title="Thông báo của sàn" style={{ marginTop: 16 }} data-testid="announcements">
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
    </>
  )
}

export default DashboardPage
