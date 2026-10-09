import { useState } from 'react'
import { Alert, Button, Card, Form, Input, Typography } from 'antd'
import { CheckCircleFilled } from '@ant-design/icons'
import { useQueryClient } from '@tanstack/react-query'
import { sellerApi } from '../api/seller'
import { ApiError } from '../api/http'
import { useAuthStore } from '../stores/auth'
import BrandMark from '../components/BrandMark'

const BENEFITS = [
  'Quản lý đơn hàng, in phiếu giao hàng loạt',
  'Theo dõi doanh thu, giải ngân và rút tiền minh bạch',
  'Mã giảm giá, Flash Sale, combo để tăng đơn',
  'Chat với người mua theo thời gian thực',
]

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
      setSession(await sellerApi.login(values.identifier.trim(), values.password))
      queryClient.clear()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Đăng nhập thất bại.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-split">
      <aside className="login-hero" aria-label="Giới thiệu Kênh Người Bán">
        <BrandMark inverse size="lg" />
        <div className="login-hero-copy">
          <p className="login-hero-title">Bán hàng dễ dàng, tăng trưởng bền vững</p>
          <ul className="login-hero-list">
            {BENEFITS.map((b) => <li key={b}><CheckCircleFilled aria-hidden="true" /> {b}</li>)}
          </ul>
        </div>
        <small className="login-hero-foot">Một tài khoản ShopHub cho cả mua và bán.</small>
      </aside>
      <main className="login-main">
        <Card className="login-card">
          <div className="login-card-brand"><BrandMark /></div>
          <Typography.Title level={1} className="login-title">Đăng nhập Kênh Người Bán</Typography.Title>
          <Typography.Paragraph type="secondary">Đăng nhập bằng tài khoản ShopHub của bạn.</Typography.Paragraph>
          {error && <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} data-testid="login-error" />}
          <Form<LoginForm> layout="vertical" onFinish={onFinish} requiredMark={false}>
            <Form.Item label="Số điện thoại / Email / Tên đăng nhập" name="identifier" rules={[{ required: true, message: 'Vui lòng nhập tên đăng nhập.' }]}>
              <Input autoComplete="username" aria-label="Tên đăng nhập" />
            </Form.Item>
            <Form.Item label="Mật khẩu" name="password" rules={[{ required: true, message: 'Vui lòng nhập mật khẩu.' }]}>
              <Input.Password autoComplete="current-password" aria-label="Mật khẩu" />
            </Form.Item>
            <Button type="primary" htmlType="submit" block loading={busy} data-testid="login-submit">Đăng nhập</Button>
          </Form>
        </Card>
      </main>
    </div>
  )
}

export default LoginPage
