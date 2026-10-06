import { useState } from 'react'
import { Alert, Button, Card, Form, Input, Typography } from 'antd'
import { adminApi } from '../api/admin'
import { ApiError } from '../api/http'
import { useAuthStore } from '../stores/auth'

interface ChangeForm {
  currentPassword: string
  newPassword: string
  confirm: string
}

/** Forced on first sign-in of the seeded admin: the API blocks everything else until this succeeds. */
const ChangePasswordPage = ({ forced }: { forced: boolean }) => {
  const clear = useAuthStore((s) => s.clear)
  const [error, setError] = useState('')
  const [done, setDone] = useState('')
  const [busy, setBusy] = useState(false)
  const [form] = Form.useForm<ChangeForm>()

  const onFinish = async (values: ChangeForm) => {
    setError('')
    setBusy(true)
    try {
      const res = await adminApi.changePassword(values.currentPassword, values.newPassword)
      setDone(res.message)
      form.resetFields()
      // Permissions and the "must change" flag live in the token: sign in again with the new password
      if (forced) {
        await adminApi.logout().catch(() => undefined)
        clear()
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.fieldErrors[0]?.message ?? err.message : 'Đổi mật khẩu thất bại.')
    } finally {
      setBusy(false)
    }
  }

  const body = (
    <Card style={{ maxWidth: 480 }}>
      <Typography.Title level={4}>Đổi mật khẩu</Typography.Title>
      {forced && (
        <Alert type="warning" showIcon style={{ marginBottom: 16 }}
          message="Bạn cần đổi mật khẩu khởi tạo trước khi sử dụng trang quản trị." />
      )}
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}
      {done && <Alert type="success" showIcon message={done} style={{ marginBottom: 16 }} />}
      <Form<ChangeForm> form={form} layout="vertical" onFinish={onFinish} requiredMark={false}>
        <Form.Item label="Mật khẩu hiện tại" name="currentPassword" rules={[{ required: true, message: 'Vui lòng nhập mật khẩu hiện tại.' }]}>
          <Input.Password autoComplete="current-password" />
        </Form.Item>
        <Form.Item label="Mật khẩu mới" name="newPassword" extra="Ít nhất 8 ký tự, có cả chữ và số."
          rules={[{ required: true, message: 'Vui lòng nhập mật khẩu mới.' }]}>
          <Input.Password autoComplete="new-password" />
        </Form.Item>
        <Form.Item label="Xác nhận mật khẩu mới" name="confirm" dependencies={['newPassword']}
          rules={[
            { required: true, message: 'Vui lòng xác nhận mật khẩu.' },
            ({ getFieldValue }) => ({
              validator: (_, v) => (v === getFieldValue('newPassword') ? Promise.resolve() : Promise.reject(new Error('Mật khẩu xác nhận không khớp.'))),
            }),
          ]}>
          <Input.Password autoComplete="new-password" />
        </Form.Item>
        <Button type="primary" htmlType="submit" loading={busy}>Đổi mật khẩu</Button>
      </Form>
    </Card>
  )

  return forced ? <div className="login-screen">{body}</div> : body
}

export default ChangePasswordPage
