import { useEffect, type ReactNode } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { App as AntApp, Avatar, Button, Dropdown, Layout, Menu, Result, Select, Space, Spin, Tag, Tooltip } from 'antd'
import {
  AppstoreAddOutlined, BarChartOutlined, BgColorsOutlined, DashboardOutlined, DownOutlined, FileExcelOutlined, FolderOutlined,
  LogoutOutlined, MessageOutlined, MoonOutlined, NotificationOutlined, PlusCircleOutlined, RollbackOutlined, SettingOutlined, ShopOutlined,
  ShoppingOutlined, StarOutlined, SunOutlined, TagsOutlined, TeamOutlined, TruckOutlined, WalletOutlined, ProfileOutlined,
} from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { sellerApi } from './api/seller'
import { refreshSession } from './api/http'
import { useAuthStore } from './stores/auth'
import { useShopStore } from './stores/shop'
import LoginPage from './pages/LoginPage'
import RegisterShopPage from './pages/RegisterShopPage'
import ShopStatusPage from './pages/ShopStatusPage'
import InvitationsPage from './pages/InvitationsPage'
import { staffApi } from './api/staff'
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
import LogisticsPage from './pages/LogisticsPage'
import ShopCategoriesPage from './pages/ShopCategoriesPage'
import DecorationPage from './pages/DecorationPage'
import BulkPage from './pages/BulkPage'
import { stopRealtime } from './lib/realtime'
import { useThemeMode } from './stores/themeMode'
import BrandMark from './components/BrandMark'
import './App.css'

const { Header, Sider, Content } = Layout

// `perm`: the shop grant a staff member needs to see the entry (owners hold every grant)
const MENU: { path: string; label: string; icon: ReactNode; perm?: string }[] = [
  { path: '/tong-quan', label: 'Bảng điều khiển', icon: <DashboardOutlined aria-hidden /> },
  { path: '/don-hang', label: 'Đơn hàng', icon: <ProfileOutlined aria-hidden />, perm: 'ORDER.VIEW' },
  { path: '/tra-hang', label: 'Trả hàng / Hoàn tiền', icon: <RollbackOutlined aria-hidden />, perm: 'ORDER.VIEW' },
  { path: '/danh-gia', label: 'Đánh giá', icon: <StarOutlined aria-hidden />, perm: 'REVIEW.MANAGE' },
  { path: '/chat', label: 'Chat', icon: <MessageOutlined aria-hidden />, perm: 'CHAT.MANAGE' },
  { path: '/tai-chinh', label: 'Tài chính', icon: <WalletOutlined aria-hidden />, perm: 'FINANCE.VIEW' },
  { path: '/phan-tich', label: 'Dữ liệu & phân tích', icon: <BarChartOutlined aria-hidden />, perm: 'ORDER.VIEW' },
  { path: '/san-pham', label: 'Sản phẩm', icon: <ShoppingOutlined aria-hidden />, perm: 'PRODUCT.VIEW' },
  { path: '/san-pham/moi', label: 'Thêm sản phẩm', icon: <PlusCircleOutlined aria-hidden />, perm: 'PRODUCT.MANAGE' },
  { path: '/san-pham/hang-loat', label: 'Excel hàng loạt', icon: <FileExcelOutlined aria-hidden />, perm: 'PRODUCT.VIEW' },
  { path: '/ma-giam-gia', label: 'Mã giảm giá', icon: <TagsOutlined aria-hidden />, perm: 'MARKETING.MANAGE' },
  { path: '/marketing', label: 'Kênh Marketing', icon: <NotificationOutlined aria-hidden />, perm: 'MARKETING.MANAGE' },
  { path: '/thiet-lap', label: 'Thiết lập shop', icon: <SettingOutlined aria-hidden />, perm: 'SETTINGS.MANAGE' },
  { path: '/kho-hang', label: 'Kho hàng & vận chuyển', icon: <TruckOutlined aria-hidden />, perm: 'SETTINGS.MANAGE' },
  { path: '/trang-tri-shop', label: 'Trang trí shop', icon: <BgColorsOutlined aria-hidden />, perm: 'SETTINGS.MANAGE' },
  { path: '/danh-muc-shop', label: 'Danh mục của shop', icon: <FolderOutlined aria-hidden />, perm: 'PRODUCT.VIEW' },
  { path: '/tai-khoan-phu', label: 'Tài khoản phụ', icon: <TeamOutlined aria-hidden />, perm: 'STAFF.MANAGE' },
  { path: '/dang-ky-ban-hang', label: 'Đăng ký shop mới', icon: <AppstoreAddOutlined aria-hidden /> },
]

const Shell = () => {
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const { user, clear } = useAuthStore()
  const { mode, toggle } = useThemeMode()
  const { currentShopId, select } = useShopStore()
  const shops = useQuery({ queryKey: ['my-shops'], queryFn: sellerApi.myShops })
  const shop = shops.data?.find((s) => s.id === currentShopId) ?? shops.data?.[0]
  // Invitations to work as staff of a shop (D6) — reachable whatever the state of the user's own shops
  const invitations = useQuery({ queryKey: ['my-invitations'], queryFn: staffApi.myInvitations })
  const onInvitations = location.pathname === '/loi-moi'
  // Selling screens only for a shop that sells (active / on vacation); otherwise its status page (D3)
  const selling = shop?.status === 'Active' || shop?.status === 'Vacation'
  const menu = selling ? MENU.filter((m) => !m.perm || shop?.permissions.includes(m.perm)) : []

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
        <div className="brand"><BrandMark /></div>
        {shop && (
          <Menu mode="inline"
            selectedKeys={[menu.filter((m) => location.pathname.startsWith(m.path)).sort((a, b) => b.path.length - a.path.length)[0]?.path ?? '/san-pham']}
            items={menu.map((m) => ({ key: m.path, label: m.label, icon: m.icon }))} onClick={(e) => navigate(e.key)} />
        )}
      </Sider>
      <Layout>
        <Header className="app-header">
          <Space wrap>
            {shops.data && shops.data.length > 0 && (
              <Select value={shop?.id} style={{ minWidth: 220 }} onChange={select} aria-label="Chọn shop"
                options={shops.data.map((s) => ({ value: s.id, label: s.name }))} />
            )}
            {shop?.status === 'PendingReview' && <Tag color="gold">Chờ duyệt</Tag>}
            {shop?.status === 'Rejected' && <Tag color="red">Bị từ chối</Tag>}
            {shop?.status === 'Locked' && <Tag color="red">Bị khoá</Tag>}
            {(invitations.data?.length ?? 0) > 0 && (
              <Button size="small" type="dashed" onClick={() => navigate('/loi-moi')} data-testid="invitations-link">
                Lời mời làm nhân viên ({invitations.data?.length})
              </Button>
            )}
          </Space>
          <Space size="small">
            <Tooltip title={mode === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'}>
              <Button type="text" shape="circle" className="header-icon-btn" icon={mode === 'dark' ? <SunOutlined /> : <MoonOutlined />} onClick={toggle}
                aria-label={mode === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'} data-testid="theme-toggle" />
            </Tooltip>
            <Dropdown trigger={['click']} placement="bottomRight"
              menu={{
                items: [
                  { key: 'who', disabled: true, label: <span className="user-menu-who"><b>{user?.fullName}</b>{shop && <small>{shop.name}</small>}</span> },
                  { type: 'divider' },
                  ...(selling && shop?.permissions.includes('SETTINGS.MANAGE')
                    ? [{ key: 'settings', icon: <ShopOutlined aria-hidden />, label: 'Thiết lập shop', onClick: () => navigate('/thiet-lap') }] : []),
                  { key: 'logout', icon: <LogoutOutlined aria-hidden />, danger: true, label: <span data-testid="logout">Đăng xuất</span>, onClick: () => void logout() },
                ],
              }}>
              <Button type="text" className="user-menu-trigger" data-testid="user-menu" aria-label="Tài khoản">
                <Avatar size="small" className="user-avatar">{(user?.fullName ?? '?').trim().charAt(0).toUpperCase()}</Avatar>
                <span className="user-menu-text">
                  <span className="user-menu-name">{user?.fullName}</span>
                  {shop && <span className="user-menu-shop">{shop.name}</span>}
                </span>
                <DownOutlined className="user-menu-caret" />
              </Button>
            </Dropdown>
          </Space>
        </Header>
        <Content className="app-content">
          {onInvitations ? (
            <InvitationsPage />
          ) : !shop ? (
            <Routes>
              <Route path="*" element={<RegisterShopPage />} />
            </Routes>
          ) : !selling ? (
            <ShopStatusPage key={shop.id} shop={shop} />
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
              <Route path="/san-pham/hang-loat" element={<BulkPage key={shop.id} shopId={shop.id} />} />
              <Route path="/san-pham/:id" element={<ProductEditorPage key={location.pathname} shopId={shop.id} />} />
              <Route path="/ma-giam-gia" element={<VouchersPage shopId={shop.id} />} />
              <Route path="/marketing" element={<MarketingPage shopId={shop.id} />} />
              <Route path="/thiet-lap" element={<ShopSettingsPage key={shop.id} shop={shop} />} />
              <Route path="/tai-khoan-phu" element={<StaffPage shopId={shop.id} />} />
              <Route path="/kho-hang" element={<LogisticsPage key={shop.id} shopId={shop.id} />} />
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
