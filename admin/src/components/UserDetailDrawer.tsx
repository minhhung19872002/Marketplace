import { App, Alert, Button, Descriptions, Drawer, Popconfirm, Space, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ApiError } from '../api/http'
import { platformApi } from '../api/platform'
import { P, can } from '../permissions'
import { formatDateTime } from '../lib/datetime'
import { formatPrice } from '../lib/money'
import { ToneTag } from './StatusTag'

/** VI.2 user detail: orders, reviews, violations, devices; reset password; link to the user's audit trail. */
const UserDetailDrawer = ({ userId, onClose, permissions }: { userId: string | null; onClose: () => void; permissions: string[] }) => {
  const { message } = App.useApp()
  const navigate = useNavigate()
  const [temporary, setTemporary] = useState<string | null>(null)
  const detail = useQuery({ queryKey: ['user-detail', userId], queryFn: () => platformApi.user(userId!), enabled: !!userId })
  const reset = useMutation({
    mutationFn: () => platformApi.resetPassword(userId!),
    onSuccess: (r) => { setTemporary(r.data.temporaryPassword); message.success(r.message) },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không đặt lại được mật khẩu.'),
  })
  const u = detail.data
  return (
    <Drawer open={!!userId} onClose={() => { setTemporary(null); onClose() }} width={720} title={u?.fullName ?? 'Người dùng'} destroyOnClose
      extra={
        <Space>
          {can(permissions, P.AuditLogView) && <Button onClick={() => navigate(`/nhat-ky?userId=${userId}`)}>Nhật ký</Button>}
          {can(permissions, P.UserResetPassword) && (
            <Popconfirm title="Đặt lại mật khẩu và cắt mọi phiên đăng nhập?" onConfirm={() => reset.mutate()}>
              <Button danger loading={reset.isPending} data-testid="reset-password">Đặt lại mật khẩu</Button>
            </Popconfirm>
          )}
        </Space>
      }>
      {temporary && (
        <Alert type="warning" showIcon style={{ marginBottom: 12 }}
          message={<span>Mật khẩu tạm (chỉ hiện một lần): <Typography.Text code copyable data-testid="temporary-password">{temporary}</Typography.Text></span>}
          description="Gửi cho người dùng qua kênh đã xác minh. Họ phải đổi mật khẩu ở lần đăng nhập tới." />
      )}
      {u && (
        <Space direction="vertical" style={{ width: '100%' }} size="large">
          <Descriptions column={2} size="small" bordered>
            <Descriptions.Item label="SĐT">{u.phone ?? '—'}</Descriptions.Item>
            <Descriptions.Item label="Email">{u.email ?? '—'}</Descriptions.Item>
            <Descriptions.Item label="Trạng thái">{u.status}{u.lockReason ? ` — ${u.lockReason}` : ''}</Descriptions.Item>
            <Descriptions.Item label="Vai trò">{u.roles.length ? u.roles.map((r) => <Tag key={r}>{r}</Tag>) : '—'}</Descriptions.Item>
            <Descriptions.Item label="Tạo lúc">{formatDateTime(u.createdAt)}</Descriptions.Item>
            <Descriptions.Item label="Đăng nhập gần nhất">{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : '—'}</Descriptions.Item>
            <Descriptions.Item label="Đơn đã đặt">{u.orderCount}</Descriptions.Item>
            <Descriptions.Item label="Đã chi">{formatPrice(u.spent)}</Descriptions.Item>
            <Descriptions.Item label="Đánh giá">{u.reviewCount} (bị báo cáo: {u.reportedReviews})</Descriptions.Item>
            <Descriptions.Item label="Yêu cầu trả hàng">{u.returnCount}</Descriptions.Item>
            {u.ownedShops.length > 0 && <Descriptions.Item label="Chủ shop" span={2}>{u.ownedShops.join(', ')}</Descriptions.Item>}
          </Descriptions>
          <div>
            <Typography.Title level={5}>Đơn gần đây</Typography.Title>
            <Table size="small" rowKey="code" pagination={false} dataSource={u.recentOrders}
              columns={[
                { title: 'Mã', dataIndex: 'code' },
                { title: 'Shop', dataIndex: 'shopName' },
                { title: 'Trạng thái', dataIndex: 'status' },
                { title: 'Tổng', dataIndex: 'grandTotal', align: 'right', render: (v: number) => formatPrice(v) },
                { title: 'Lúc', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
              ]} />
          </div>
          <div>
            <Typography.Title level={5}>Thiết bị đăng nhập</Typography.Title>
            <Table size="small" rowKey={(_, i) => String(i)} pagination={false} dataSource={u.devices}
              columns={[
                { title: 'Thiết bị', dataIndex: 'device', render: (v: string | null) => v ?? 'Không rõ' },
                { title: 'IP', dataIndex: 'ip' },
                { title: 'Đăng nhập', dataIndex: 'signedInAt', render: (v: string) => formatDateTime(v) },
                { title: 'Phiên', dataIndex: 'active', render: (v: boolean) => (v ? <ToneTag tone="success">Đang mở</ToneTag> : <Tag>Đã đóng</Tag>) },
              ]} />
          </div>
        </Space>
      )}
    </Drawer>
  )
}

export default UserDetailDrawer
