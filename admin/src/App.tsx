import { useEffect, type ReactNode } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { App as AntApp, Avatar, Button, Dropdown, Layout, Menu, Result, Space, Spin, Tooltip } from 'antd'
import {
  AppstoreOutlined, AuditOutlined, BarChartOutlined, CarOutlined, CommentOutlined, DashboardOutlined, DownOutlined,
  FileSearchOutlined, FileTextOutlined, FireOutlined, GiftOutlined, KeyOutlined, LockOutlined, LogoutOutlined, MoonOutlined, NotificationOutlined,
  ProfileOutlined, RightOutlined, SafetyCertificateOutlined, ScheduleOutlined, SettingOutlined, ShopOutlined, StarOutlined, SunOutlined, TeamOutlined,
  WalletOutlined, WarningOutlined,
} from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { adminApi } from './api/admin'
import { refreshSession } from './api/http'
import { P, can } from './permissions'
import { useAuthStore } from './stores/auth'
import LoginPage from './pages/LoginPage'
import ChangePasswordPage from './pages/ChangePasswordPage'
import DashboardPage from './pages/DashboardPage'
import UsersPage from './pages/UsersPage'
import RolesPage from './pages/RolesPage'
import ParametersPage from './pages/ParametersPage'
import LaunchChecklistPage from './pages/LaunchChecklistPage'
import AuditLogsPage from './pages/AuditLogsPage'
import ProductReviewPage from './pages/ProductReviewPage'
import ShopsPage from './pages/ShopsPage'
import CategoriesPage from './pages/CategoriesPage'
import DisputesPage from './pages/DisputesPage'
import FinancePage from './pages/FinancePage'
import MarketingPage from './pages/MarketingPage'
import ReviewReportsPage from './pages/ReviewReportsPage'
import ChatReportsPage from './pages/ChatReportsPage'
import VouchersPage from './pages/VouchersPage'
import ReportsPage from './pages/ReportsPage'
import OrdersPage from './pages/OrdersPage'
import ContentPage from './pages/ContentPage'
import ProvidersPage from './pages/ProvidersPage'
import CatalogExtrasPage from './pages/CatalogExtrasPage'
import JobsPage from './pages/JobsPage'
import HotKeywordsPage from './pages/HotKeywordsPage'
import { useThemeMode } from './stores/themeMode'
import BrandMark from './components/BrandMark'
import './App.css'

const { Header, Sider, Content } = Layout

type MenuEntry = { path: string; label: string; icon: ReactNode; permission: string | null; anyOf?: string[] }

// Ordered by how often an operator opens them, grouped Vận hành / Kinh doanh / Hệ thống. An entry appears only when the
// signed-in admin holds the permission (the API enforces it regardless).
const MENU: { group: string; items: MenuEntry[] }[] = [
  {
    group: 'Vận hành',
    items: [
      { path: '/', label: 'Tổng quan', icon: <DashboardOutlined aria-hidden />, permission: null },
      { path: '/don-hang', label: 'Đơn hàng', icon: <ProfileOutlined aria-hidden />, permission: P.OrderView },
      { path: '/duyet-san-pham', label: 'Duyệt sản phẩm', icon: <SafetyCertificateOutlined aria-hidden />, permission: P.ProductReview },
      { path: '/shop', label: 'Shop', icon: <ShopOutlined aria-hidden />, permission: P.ShopView },
      { path: '/khieu-nai', label: 'Khiếu nại trả hàng', icon: <AuditOutlined aria-hidden />, permission: P.DisputeResolve },
      { path: '/nguoi-dung', label: 'Người dùng', icon: <TeamOutlined aria-hidden />, permission: P.UserView },
      { path: '/bao-cao-san-pham', label: 'Sản phẩm vi phạm', icon: <WarningOutlined aria-hidden />, permission: null, anyOf: [P.ProductBan, P.BrandManage] },
      { path: '/bao-cao-danh-gia', label: 'Báo cáo đánh giá', icon: <StarOutlined aria-hidden />, permission: P.ReviewModerate },
      { path: '/bao-cao-chat', label: 'Chat bị báo cáo', icon: <CommentOutlined aria-hidden />, permission: P.ChatReview },
    ],
  },
  {
    group: 'Kinh doanh',
    items: [
      { path: '/voucher', label: 'Voucher của sàn', icon: <GiftOutlined aria-hidden />, permission: P.VoucherManage },
      { path: '/marketing', label: 'Marketing', icon: <NotificationOutlined aria-hidden />, permission: P.MarketingManage },
      { path: '/tai-chinh', label: 'Tài chính', icon: <WalletOutlined aria-hidden />, permission: P.FinanceLedgerView },
      { path: '/bao-cao', label: 'Báo cáo', icon: <BarChartOutlined aria-hidden />, permission: P.ReportView },
      { path: '/tu-khoa-hot', label: 'Từ khoá hot', icon: <FireOutlined aria-hidden />, permission: P.HotKeywordManage },
    ],
  },
  {
    group: 'Hệ thống',
    items: [
      { path: '/nganh-hang', label: 'Ngành hàng', icon: <AppstoreOutlined aria-hidden />, permission: P.CategoryManage },
      { path: '/noi-dung', label: 'Nội dung & mẫu tin', icon: <FileTextOutlined aria-hidden />, permission: P.ContentManage },
      { path: '/tham-so', label: 'Tham số hệ thống', icon: <SettingOutlined aria-hidden />, permission: P.SystemParameterView },
      { path: '/kiem-tra-mo-ban', label: 'Kiểm tra trước khi mở bán', icon: <SafetyCertificateOutlined aria-hidden />, permission: P.SystemParameterView },
      { path: '/vai-tro', label: 'Vai trò & quyền', icon: <KeyOutlined aria-hidden />, permission: P.RoleView },
      { path: '/nhat-ky', label: 'Nhật ký thao tác', icon: <FileSearchOutlined aria-hidden />, permission: P.AuditLogView },
      { path: '/nha-cung-cap', label: 'Vận chuyển & cổng thanh toán', icon: <CarOutlined aria-hidden />, permission: P.ProviderManage },
      { path: '/viec-nen', label: 'Việc nền', icon: <ScheduleOutlined aria-hidden />, permission: P.JobDashboardView },
    ],
  },
]

const ENTRIES = MENU.flatMap((g) => g.items)

const Forbidden = () => <Result status="403" title="Không có quyền" subTitle="Bạn không có quyền truy cập trang này." />

const Shell = () => {
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const { user, clear } = useAuthStore()
  const { mode, toggle } = useThemeMode()
  const me = useQuery({ queryKey: ['me'], queryFn: adminApi.me })
  const perms = me.data?.permissions ?? []
  const allowed = (m: MenuEntry) => (m.anyOf ? m.anyOf.some((c) => can(perms, c)) : m.permission === null || can(perms, m.permission))
  const groups = MENU.map((g) => ({ ...g, items: g.items.filter(allowed) })).filter((g) => g.items.length > 0)
  const guard = (permission: string, element: JSX.Element) => (can(perms, permission) ? element : <Forbidden />)
  const current = ENTRIES.find((m) => m.path !== '/' && (location.pathname === m.path || location.pathname.startsWith(`${m.path}/`)))?.path ?? '/'

  const logout = async () => {
    await adminApi.logout().catch(() => undefined)
    clear()
    queryClient.clear()
  }

  if (me.isPending) return <div className="center-screen"><Spin size="large" /></div>
  if (me.data && me.data.permissions.length === 0) {
    return (
      <Result status="403" title="Tài khoản không có quyền quản trị"
        extra={<Button onClick={logout}>Đăng xuất</Button>} />
    )
  }

  const themeLabel = mode === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'
  const roles = me.data?.permissions.includes('*') ? 'Toàn quyền' : me.data?.roles.join(', ')
  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider width={232} theme="light" breakpoint="lg" collapsedWidth={0} className="app-sider">
        <div className="brand"><BrandMark /></div>
        <Menu
          mode="inline"
          selectedKeys={[current]}
          items={groups.map((g) => ({
            type: 'group' as const,
            key: g.group,
            label: g.group,
            children: g.items.map((m) => ({ key: m.path, label: m.label, icon: m.icon })),
          }))}
          onClick={(e) => navigate(e.key)}
          data-testid="admin-menu"
        />
      </Sider>
      <Layout>
        <Header className="app-header">
          <span className="app-header-title">
            {location.pathname === '/doi-mat-khau' ? 'Đổi mật khẩu' : (
              <>
                <span className="app-header-group">{MENU.find((g) => g.items.some((m) => m.path === current))?.group}</span>
                <RightOutlined aria-hidden className="app-header-sep" />
                {ENTRIES.find((m) => m.path === current)?.label}
              </>
            )}
          </span>
          <Space size="small">
            <Tooltip title={themeLabel}>
              <Button type="text" shape="circle" icon={mode === 'dark' ? <SunOutlined /> : <MoonOutlined />} onClick={toggle}
                aria-label={themeLabel} data-testid="theme-toggle" />
            </Tooltip>
            <Dropdown trigger={['click']} placement="bottomRight"
              menu={{
                items: [
                  { key: 'who', disabled: true, label: <span className="user-menu-who"><b>{user?.fullName}</b>{roles && <small>{roles}</small>}</span> },
                  { type: 'divider' },
                  { key: 'password', icon: <LockOutlined aria-hidden />, label: 'Đổi mật khẩu', onClick: () => navigate('/doi-mat-khau') },
                  { key: 'logout', icon: <LogoutOutlined aria-hidden />, danger: true, label: <span data-testid="logout">Đăng xuất</span>, onClick: () => void logout() },
                ],
              }}>
              <Button type="text" className="user-menu-trigger" data-testid="user-menu" aria-label="Tài khoản">
                <Avatar size="small" className="user-avatar">{(user?.fullName ?? '?').trim().charAt(0).toUpperCase()}</Avatar>
                <span className="user-menu-text">
                  <span className="user-menu-name">{user?.fullName}</span>
                  {roles && <span className="user-menu-role">{roles}</span>}
                </span>
                <DownOutlined className="user-menu-caret" />
              </Button>
            </Dropdown>
          </Space>
        </Header>
        <Content className="app-content">
          <Routes>
            <Route path="/" element={<DashboardPage permissions={perms} />} />
            <Route path="/bao-cao" element={guard(P.ReportView, <ReportsPage />)} />
            <Route path="/don-hang" element={guard(P.OrderView, <OrdersPage permissions={perms} />)} />
            <Route path="/bao-cao-san-pham" element={can(perms, P.ProductBan) || can(perms, P.BrandManage) ? <CatalogExtrasPage permissions={perms} /> : <Forbidden />} />
            <Route path="/noi-dung" element={guard(P.ContentManage, <ContentPage />)} />
            <Route path="/nha-cung-cap" element={guard(P.ProviderManage, <ProvidersPage />)} />
            <Route path="/duyet-san-pham" element={guard(P.ProductReview, <ProductReviewPage permissions={perms} />)} />
            <Route path="/shop" element={guard(P.ShopView, <ShopsPage permissions={perms} />)} />
            <Route path="/nganh-hang" element={guard(P.CategoryManage, <CategoriesPage />)} />
            <Route path="/voucher" element={guard(P.VoucherManage, <VouchersPage />)} />
            <Route path="/marketing" element={guard(P.MarketingManage, <MarketingPage />)} />
            <Route path="/tu-khoa-hot" element={guard(P.HotKeywordManage, <HotKeywordsPage />)} />
            <Route path="/khieu-nai" element={guard(P.DisputeResolve, <DisputesPage />)} />
            <Route path="/bao-cao-danh-gia" element={guard(P.ReviewModerate, <ReviewReportsPage />)} />
            <Route path="/bao-cao-chat" element={guard(P.ChatReview, <ChatReportsPage />)} />
            <Route path="/tai-chinh" element={guard(P.FinanceLedgerView, <FinancePage permissions={perms} />)} />
            <Route path="/nguoi-dung" element={guard(P.UserView, <UsersPage permissions={perms} />)} />
            <Route path="/vai-tro" element={guard(P.RoleView, <RolesPage permissions={perms} />)} />
            <Route path="/tham-so" element={guard(P.SystemParameterView, <ParametersPage permissions={perms} />)} />
            <Route path="/kiem-tra-mo-ban" element={guard(P.SystemParameterView, <LaunchChecklistPage />)} />
            <Route path="/nhat-ky" element={guard(P.AuditLogView, <AuditLogsPage />)} />
            <Route path="/viec-nen" element={guard(P.JobDashboardView, <JobsPage permissions={perms} />)} />
            <Route path="/doi-mat-khau" element={<ChangePasswordPage forced={false} />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
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
  if (user.mustChangePassword) return <ChangePasswordPage forced />
  return <Shell />
}

function App() {
  return (
    <AntApp>
      <BrowserRouter basename="/admin">
        <Gate />
      </BrowserRouter>
    </AntApp>
  )
}

export default App
