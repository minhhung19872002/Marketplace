import { useState } from 'react'
import { Alert, Button, Card, Form, Input, Typography } from 'antd'
import { useQueryClient } from '@tanstack/react-query'
import { adminApi } from '../api/admin'
import { ApiError } from '../api/http'
import { useAuthStore } from '../stores/auth'

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
    <div className="login-screen">
      <Card className="login-card">
        <Typography.Title level={3}>ShopHub — Quản Trị Sàn</Typography.Title>
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
    </div>
  )
}

export default LoginPage
