import { useEffect, useState } from 'react'
import { Layout, Menu, Typography, Card, Tag, Space } from 'antd'
import { fetchHealth, type HealthStatus } from './api/system'

const { Header, Sider, Content } = Layout

const MENU_ITEMS = ['Tổng quan', 'Sản phẩm', 'Đơn hàng', 'Marketing', 'Tài chính', 'Thiết lập shop']

const STATUS_LABEL: Record<HealthStatus, { text: string; color: string }> = {
  Healthy: { text: 'Hoạt động', color: 'success' },
  Degraded: { text: 'Suy giảm', color: 'warning' },
  Unhealthy: { text: 'Lỗi', color: 'error' },
  Unreachable: { text: 'Không kết nối được', color: 'default' },
}

function App() {
  const [health, setHealth] = useState<HealthStatus | null>(null)

  useEffect(() => {
    const ctrl = new AbortController()
    fetchHealth(ctrl.signal).then(setHealth)
    return () => ctrl.abort()
  }, [])

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider width={220} theme="light">
        <Typography.Title level={4} style={{ padding: '16px 24px', margin: 0 }}>
          ShopHub
        </Typography.Title>
        <Menu
          mode="inline"
          defaultSelectedKeys={['0']}
          items={MENU_ITEMS.map((label, i) => ({ key: String(i), label }))}
        />
      </Sider>
      <Layout>
        <Header style={{ background: 'transparent', paddingInline: 24 }}>
          <Typography.Title level={3} style={{ margin: '16px 0' }}>
            Kênh Người Bán
          </Typography.Title>
        </Header>
        <Content style={{ padding: 24 }}>
          <Card title="Trạng thái hệ thống">
            <Space>
              <span>API:</span>
              {health ? (
                <Tag color={STATUS_LABEL[health].color} data-testid="api-health">
                  {STATUS_LABEL[health].text}
                </Tag>
              ) : (
                <Tag>Đang kiểm tra…</Tag>
              )}
            </Space>
          </Card>
        </Content>
      </Layout>
    </Layout>
  )
}

export default App
