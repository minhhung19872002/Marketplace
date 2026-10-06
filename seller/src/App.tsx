import { useEffect } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { App as AntApp, Button, Layout, Menu, Result, Select, Space, Spin, Tag, Typography } from 'antd'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { sellerApi } from './api/seller'
import { refreshSession } from './api/http'
import { useAuthStore } from './stores/auth'
import { useShopStore } from './stores/shop'
import LoginPage from './pages/LoginPage'
import RegisterShopPage from './pages/RegisterShopPage'
import ProductsPage from './pages/ProductsPage'
import ProductEditorPage from './pages/ProductEditorPage'
import ShopSettingsPage from './pages/ShopSettingsPage'
import VouchersPage from './pages/VouchersPage'
import './App.css'

const { Header, Sider, Content } = Layout

const MENU = [
  { path: '/san-pham', label: 'Sản phẩm' },
  { path: '/san-pham/moi', label: 'Thêm sản phẩm' },
  { path: '/ma-giam-gia', label: 'Mã giảm giá' },
  { path: '/thiet-lap', label: 'Thiết lập shop' },
  { path: '/dang-ky-ban-hang', label: 'Đăng ký shop mới' },
]

const Shell = () => {
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const { user, clear } = useAuthStore()
  const { currentShopId, select } = useShopStore()
  const shops = useQuery({ queryKey: ['my-shops'], queryFn: sellerApi.myShops })
  const shop = shops.data?.find((s) => s.id === currentShopId) ?? shops.data?.[0]

  // Keep the remembered shop valid (membership may have changed)
  useEffect(() => {
    if (shops.data && shop && shop.id !== currentShopId) select(shop.id)
  }, [shops.data, shop, currentShopId, select])

  const logout = async () => {
    await sellerApi.logout().catch(() => undefined)
    clear()
    select(null)
    queryClient.clear()
  }

  if (shops.isPending) return <div className="center-screen"><Spin size="large" /></div>
  if (shops.isError) return <Result status="error" title="Không tải được danh sách shop" extra={<Button onClick={() => shops.refetch()}>Thử lại</Button>} />

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider width={220} theme="light" breakpoint="lg" collapsedWidth={0}>
        <Typography.Title level={4} className="brand">ShopHub</Typography.Title>
        {shop && (
          <Menu mode="inline"
            selectedKeys={[MENU.filter((m) => location.pathname.startsWith(m.path)).sort((a, b) => b.path.length - a.path.length)[0]?.path ?? '/san-pham']}
            items={MENU.map((m) => ({ key: m.path, label: m.label }))} onClick={(e) => navigate(e.key)} />
        )}
      </Sider>
      <Layout>
        <Header className="app-header">
          <Space>
            <Typography.Text strong>Kênh Người Bán</Typography.Text>
            {shops.data && shops.data.length > 0 && (
              <Select value={shop?.id} style={{ minWidth: 220 }} onChange={select} aria-label="Chọn shop"
                options={shops.data.map((s) => ({ value: s.id, label: s.name }))} />
            )}
            {shop?.status === 'PendingReview' && <Tag color="gold">Chờ duyệt</Tag>}
          </Space>
          <Space>
            <Typography.Text>{user?.fullName}</Typography.Text>
            <Button size="small" onClick={logout} data-testid="logout">Đăng xuất</Button>
          </Space>
        </Header>
        <Content className="app-content">
          {!shop ? (
            <Routes>
              <Route path="*" element={<RegisterShopPage />} />
            </Routes>
          ) : (
            <Routes>
              <Route path="/" element={<Navigate to="/san-pham" replace />} />
              <Route path="/san-pham" element={<ProductsPage shopId={shop.id} />} />
              <Route path="/san-pham/:id" element={<ProductEditorPage key={location.pathname} shopId={shop.id} />} />
              <Route path="/ma-giam-gia" element={<VouchersPage shopId={shop.id} />} />
              <Route path="/thiet-lap" element={<ShopSettingsPage shop={shop} />} />
              <Route path="/dang-ky-ban-hang" element={<RegisterShopPage />} />
              <Route path="*" element={<Navigate to="/san-pham" replace />} />
            </Routes>
          )}
        </Content>
      </Layout>
    </Layout>
  )
}

const Gate = () => {
  const { status, user } = useAuthStore()

  useEffect(() => {
    void refreshSession()
  }, [])

  if (status === 'checking') return <div className="center-screen"><Spin size="large" /></div>
  if (status === 'anonymous' || !user) return <LoginPage />
  return <Shell />
}

function App() {
  return (
    <AntApp>
      <BrowserRouter basename="/seller">
        <Gate />
      </BrowserRouter>
    </AntApp>
  )
}

export default App
