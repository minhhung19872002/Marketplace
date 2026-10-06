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
import DashboardPage from './pages/DashboardPage'
import OrdersPage from './pages/OrdersPage'
import ReviewsPage from './pages/ReviewsPage'
import ReturnsPage from './pages/ReturnsPage'
import FinancePage from './pages/FinancePage'
import MarketingPage from './pages/MarketingPage'
import ChatPage from './pages/ChatPage'
import AnalyticsPage from './pages/AnalyticsPage'
import StaffPage from './pages/StaffPage'
import ShopCategoriesPage from './pages/ShopCategoriesPage'
import DecorationPage from './pages/DecorationPage'
import { stopRealtime } from './lib/realtime'
import './App.css'

const { Header, Sider, Content } = Layout

// `perm`: the shop grant a staff member needs to see the entry (owners hold every grant)
const MENU: { path: string; label: string; perm?: string }[] = [
  { path: '/tong-quan', label: 'Bảng điều khiển' },
  { path: '/don-hang', label: 'Đơn hàng', perm: 'ORDER.VIEW' },
  { path: '/tra-hang', label: 'Trả hàng / Hoàn tiền', perm: 'ORDER.VIEW' },
  { path: '/danh-gia', label: 'Đánh giá', perm: 'REVIEW.MANAGE' },
  { path: '/chat', label: 'Chat', perm: 'CHAT.MANAGE' },
  { path: '/tai-chinh', label: 'Tài chính', perm: 'FINANCE.VIEW' },
  { path: '/phan-tich', label: 'Dữ liệu & phân tích', perm: 'ORDER.VIEW' },
  { path: '/san-pham', label: 'Sản phẩm', perm: 'PRODUCT.VIEW' },
  { path: '/san-pham/moi', label: 'Thêm sản phẩm', perm: 'PRODUCT.MANAGE' },
  { path: '/ma-giam-gia', label: 'Mã giảm giá', perm: 'MARKETING.MANAGE' },
  { path: '/marketing', label: 'Kênh Marketing', perm: 'MARKETING.MANAGE' },
  { path: '/thiet-lap', label: 'Thiết lập shop', perm: 'SETTINGS.MANAGE' },
  { path: '/trang-tri-shop', label: 'Trang trí shop', perm: 'SETTINGS.MANAGE' },
  { path: '/danh-muc-shop', label: 'Danh mục của shop', perm: 'PRODUCT.VIEW' },
  { path: '/tai-khoan-phu', label: 'Tài khoản phụ', perm: 'STAFF.MANAGE' },
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
  const menu = MENU.filter((m) => !m.perm || shop?.permissions.includes(m.perm))

  // Keep the remembered shop valid (membership may have changed)
  useEffect(() => {
    if (shops.data && shop && shop.id !== currentShopId) select(shop.id)
  }, [shops.data, shop, currentShopId, select])

  const logout = async () => {
    await sellerApi.logout().catch(() => undefined)
    await stopRealtime()
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
            selectedKeys={[menu.filter((m) => location.pathname.startsWith(m.path)).sort((a, b) => b.path.length - a.path.length)[0]?.path ?? '/san-pham']}
            items={menu.map((m) => ({ key: m.path, label: m.label }))} onClick={(e) => navigate(e.key)} />
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
              <Route path="/" element={<Navigate to="/tong-quan" replace />} />
              <Route path="/tong-quan" element={<DashboardPage shopId={shop.id} />} />
              <Route path="/don-hang" element={<OrdersPage shopId={shop.id} />} />
              <Route path="/tra-hang" element={<ReturnsPage shopId={shop.id} />} />
              <Route path="/danh-gia" element={<ReviewsPage shopId={shop.id} />} />
              <Route path="/chat" element={<ChatPage key={shop.id} shopId={shop.id} />} />
              <Route path="/tai-chinh" element={<FinancePage shopId={shop.id} />} />
              <Route path="/phan-tich" element={<AnalyticsPage key={shop.id} shopId={shop.id} />} />
              <Route path="/san-pham" element={<ProductsPage shopId={shop.id} />} />
              <Route path="/san-pham/:id" element={<ProductEditorPage key={location.pathname} shopId={shop.id} />} />
              <Route path="/ma-giam-gia" element={<VouchersPage shopId={shop.id} />} />
              <Route path="/marketing" element={<MarketingPage shopId={shop.id} />} />
              <Route path="/thiet-lap" element={<ShopSettingsPage key={shop.id} shop={shop} />} />
              <Route path="/tai-khoan-phu" element={<StaffPage shopId={shop.id} />} />
              <Route path="/trang-tri-shop" element={<DecorationPage key={shop.id} shopId={shop.id} shopSlug={shop.slug} />} />
              <Route path="/danh-muc-shop" element={<ShopCategoriesPage shopId={shop.id} />} />
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
