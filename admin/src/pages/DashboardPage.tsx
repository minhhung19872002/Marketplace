import { Card, Descriptions, Space, Tag, Typography } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { adminApi } from '../api/admin'
import { fetchHealth } from '../api/http'

const HEALTH = {
  Healthy: { text: 'Hoạt động', color: 'success' },
  Unhealthy: { text: 'Lỗi', color: 'error' },
  Unreachable: { text: 'Không kết nối được', color: 'default' },
} as const

const DashboardPage = () => {
  const health = useQuery({ queryKey: ['health'], queryFn: fetchHealth, refetchInterval: 30_000 })
  const me = useQuery({ queryKey: ['me'], queryFn: adminApi.me })

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Typography.Title level={3} style={{ margin: 0 }}>Tổng quan</Typography.Title>
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
