import { useState } from 'react'
import { Alert, Button, Card, Form, Input, Typography } from 'antd'
import { CheckCircleFilled } from '@ant-design/icons'
import { useQueryClient } from '@tanstack/react-query'
import { adminApi } from '../api/admin'
import { ApiError } from '../api/http'
import { useAuthStore } from '../stores/auth'
import './LoginPage.css'

const BENEFITS = [
  'Duyệt shop, sản phẩm và phân xử khiếu nại',
  'Theo dõi GMV, đơn hàng và dòng tiền của sàn',
  'Phân quyền theo vai trò, nhật ký thao tác đầy đủ',
]

/** ShopHub brand mark: bag icon + name + "Quản trị sàn" (same bag as the buyer site's logo). */
const LoginBrand = ({ inverse = false }: { inverse?: boolean }) => (
  <span className={`login-brand${inverse ? ' login-brand-inverse' : ''}`}>
    <span className="login-brand-icon" aria-hidden="true">
      <svg viewBox="0 0 32 32" width="100%" height="100%">
        <path d="M10 11h12l-1 12a2 2 0 0 1-2 1.8H13a2 2 0 0 1-2-1.8L10 11z" fill="none" stroke="currentColor" strokeWidth="2" strokeLinejoin="round" />
        <path d="M12.5 11a3.5 3.5 0 0 1 7 0" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
      </svg>
    </span>
    <span className="login-brand-text">
      <span className="login-brand-name">ShopHub</span>
      <span className="login-brand-label">Quản trị sàn</span>
    </span>
  </span>
)

interface LoginForm {
  identifier: string
  password: string
}

const LoginPage = () => {
  const setSession = useAuthStore((s) => s.setSession)
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const onFinish = async (values: LoginForm) => {
    setError('')
    setBusy(true)
    try {
      setSession(await adminApi.login(values.identifier.trim(), values.password))
      queryClient.clear()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Đăng nhập thất bại.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="admin-login">
      <aside className="admin-login-hero" aria-label="Giới thiệu trang quản trị">
        <LoginBrand inverse />
        <div>
          <p className="admin-login-hero-title">Vận hành sàn ShopHub ở một nơi</p>
          <ul className="admin-login-hero-list">
            {BENEFITS.map((b) => <li key={b}><CheckCircleFilled aria-hidden="true" /> {b}</li>)}
          </ul>
        </div>
        <small>Chỉ dành cho nhân sự được cấp quyền quản trị.</small>
      </aside>
      <main className="admin-login-main">
        <Card className="login-card admin-login-card">
          <div className="admin-login-card-brand"><LoginBrand /></div>
          <Typography.Title level={1} className="admin-login-title">Đăng nhập quản trị sàn</Typography.Title>
          <Typography.Paragraph type="secondary">Đăng nhập bằng tài khoản quản trị.</Typography.Paragraph>
          {error && <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} data-testid="login-error" />}
          <Form<LoginForm> layout="vertical" onFinish={onFinish} requiredMark={false}>
            <Form.Item label="Tên đăng nhập / Email / SĐT" name="identifier" rules={[{ required: true, message: 'Vui lòng nhập tên đăng nhập.' }]}>
              <Input autoComplete="username" aria-label="Tên đăng nhập" />
            </Form.Item>
            <Form.Item label="Mật khẩu" name="password" rules={[{ required: true, message: 'Vui lòng nhập mật khẩu.' }]}>
              <Input.Password autoComplete="current-password" aria-label="Mật khẩu" />
            </Form.Item>
            <Button type="primary" htmlType="submit" block loading={busy} data-testid="login-submit">
              Đăng nhập
            </Button>
          </Form>
        </Card>
      </main>
    </div>
  )
}

export default LoginPage
