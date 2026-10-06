import { useEffect } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { App as AntApp, Button, Layout, Menu, Result, Space, Spin, Typography } from 'antd'
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
import AuditLogsPage from './pages/AuditLogsPage'
import ProductReviewPage from './pages/ProductReviewPage'
import ShopsPage from './pages/ShopsPage'
import CategoriesPage from './pages/CategoriesPage'
import DisputesPage from './pages/DisputesPage'
import FinancePage from './pages/FinancePage'
import ReviewReportsPage from './pages/ReviewReportsPage'
import VouchersPage from './pages/VouchersPage'
import './App.css'

const { Header, Sider, Content } = Layout

// Menu entries appear only when the signed-in admin holds the permission (the API enforces it regardless)
const MENU = [
  { path: '/', label: 'Tổng quan', permission: null },
  { path: '/duyet-san-pham', label: 'Duyệt sản phẩm', permission: P.ProductReview },
  { path: '/shop', label: 'Shop', permission: P.ShopView },
  { path: '/nganh-hang', label: 'Ngành hàng', permission: P.CategoryManage },
  { path: '/voucher', label: 'Voucher của sàn', permission: P.VoucherManage },
  { path: '/khieu-nai', label: 'Khiếu nại trả hàng', permission: P.DisputeResolve },
  { path: '/bao-cao-danh-gia', label: 'Báo cáo đánh giá', permission: P.ReviewModerate },
  { path: '/tai-chinh', label: 'Tài chính', permission: P.FinanceLedgerView },
  { path: '/nguoi-dung', label: 'Người dùng', permission: P.UserView },
  { path: '/vai-tro', label: 'Vai trò & quyền', permission: P.RoleView },
  { path: '/tham-so', label: 'Tham số hệ thống', permission: P.SystemParameterView },
  { path: '/nhat-ky', label: 'Nhật ký thao tác', permission: P.AuditLogView },
] as const

const Forbidden = () => <Result status="403" title="Không có quyền" subTitle="Bạn không có quyền truy cập trang này." />

const Shell = () => {
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const { user, clear } = useAuthStore()
  const me = useQuery({ queryKey: ['me'], queryFn: adminApi.me })
  const perms = me.data?.permissions ?? []
  const items = MENU.filter((m) => m.permission === null || can(perms, m.permission))
  const guard = (permission: string, element: JSX.Element) => (can(perms, permission) ? element : <Forbidden />)

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

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider width={230} theme="light" breakpoint="lg" collapsedWidth={0}>
        <Typography.Title level={4} className="brand">ShopHub</Typography.Title>
        <Menu
          mode="inline"
          selectedKeys={[items.find((m) => m.path !== '/' && location.pathname.startsWith(m.path))?.path ?? '/']}
          items={items.map((m) => ({ key: m.path, label: m.label }))}
          onClick={(e) => navigate(e.key)}
          data-testid="admin-menu"
        />
      </Sider>
      <Layout>
        <Header className="app-header">
          <Typography.Text strong>Quản Trị Sàn</Typography.Text>
          <Space>
            <Typography.Text>{user?.fullName}</Typography.Text>
            <Button size="small" onClick={() => navigate('/doi-mat-khau')}>Đổi mật khẩu</Button>
            <Button size="small" onClick={logout} data-testid="logout">Đăng xuất</Button>
          </Space>
        </Header>
        <Content className="app-content">
          <Routes>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/duyet-san-pham" element={guard(P.ProductReview, <ProductReviewPage permissions={perms} />)} />
            <Route path="/shop" element={guard(P.ShopView, <ShopsPage permissions={perms} />)} />
            <Route path="/nganh-hang" element={guard(P.CategoryManage, <CategoriesPage />)} />
            <Route path="/voucher" element={guard(P.VoucherManage, <VouchersPage />)} />
            <Route path="/khieu-nai" element={guard(P.DisputeResolve, <DisputesPage />)} />
            <Route path="/bao-cao-danh-gia" element={guard(P.ReviewModerate, <ReviewReportsPage />)} />
            <Route path="/tai-chinh" element={guard(P.FinanceLedgerView, <FinancePage permissions={perms} />)} />
            <Route path="/nguoi-dung" element={guard(P.UserView, <UsersPage permissions={perms} />)} />
            <Route path="/vai-tro" element={guard(P.RoleView, <RolesPage permissions={perms} />)} />
            <Route path="/tham-so" element={guard(P.SystemParameterView, <ParametersPage permissions={perms} />)} />
            <Route path="/nhat-ky" element={guard(P.AuditLogView, <AuditLogsPage />)} />
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
