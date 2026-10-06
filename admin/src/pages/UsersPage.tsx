import { useState } from 'react'
import { App as AntApp, Button, Checkbox, Input, Modal, Select, Space, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminApi, type AdminUser, type UserStatus } from '../api/admin'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { P, can } from '../permissions'

const STATUS: Record<UserStatus, { text: string; color: string }> = {
  Active: { text: 'Hoạt động', color: 'green' },
  Locked: { text: 'Đã khoá', color: 'red' },
  Deleted: { text: 'Đã xoá', color: 'default' },
}

const UsersPage = ({ permissions }: { permissions: string[] }) => {
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [q, setQ] = useState('')
  const [status, setStatus] = useState<UserStatus | undefined>()
  const [adminsOnly, setAdminsOnly] = useState(false)
  const [locking, setLocking] = useState<AdminUser | null>(null)
  const [lockReason, setLockReason] = useState('')
  const [assigning, setAssigning] = useState<AdminUser | null>(null)
  const [roleIds, setRoleIds] = useState<string[]>([])

  const users = useQuery({
    queryKey: ['users', page, pageSize, q, status, adminsOnly],
    queryFn: () => adminApi.users({ page, pageSize, q, status, adminsOnly: adminsOnly || undefined }),
  })
  const roles = useQuery({ queryKey: ['roles'], queryFn: adminApi.roles, enabled: can(permissions, P.RoleView) })

  const done = (text: string) => {
    void message.success(text)
    void queryClient.invalidateQueries({ queryKey: ['users'] })
  }
  const fail = (err: unknown) => void message.error(err instanceof ApiError ? err.message : 'Thao tác thất bại.')

  const lock = useMutation({
    mutationFn: () => adminApi.lockUser(locking!.id, lockReason),
    onSuccess: (r) => {
      setLocking(null)
      setLockReason('')
      done(r.message)
    },
    onError: fail,
  })
  const unlock = useMutation({ mutationFn: adminApi.unlockUser, onSuccess: (r) => done(r.message), onError: fail })
  const assign = useMutation({
    mutationFn: () => adminApi.setUserRoles(assigning!.id, roleIds),
    onSuccess: (r) => {
      setAssigning(null)
      done(r.message)
    },
    onError: fail,
  })

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Title level={3} style={{ margin: 0 }}>Người dùng</Typography.Title>
      <Space wrap>
        <Input.Search placeholder="Tên, email, SĐT, tên đăng nhập" allowClear style={{ width: 300 }}
          onSearch={(v) => { setQ(v); setPage(1) }} />
        <Select<UserStatus> placeholder="Trạng thái" allowClear style={{ width: 160 }} value={status}
          onChange={(v) => { setStatus(v); setPage(1) }}
          options={(Object.keys(STATUS) as UserStatus[]).map((s) => ({ value: s, label: STATUS[s].text }))} />
        <Checkbox checked={adminsOnly} onChange={(e) => { setAdminsOnly(e.target.checked); setPage(1) }}>Chỉ quản trị viên</Checkbox>
      </Space>
      <Table<AdminUser>
        rowKey="id"
        loading={users.isPending}
        dataSource={users.data?.items}
        pagination={{
          current: page,
          pageSize,
          total: users.data?.totalCount,
          showSizeChanger: true,
          showTotal: (t) => `${t} người dùng`,
          onChange: (p, s) => { setPage(p); setPageSize(s) },
        }}
        locale={{ emptyText: users.isError ? 'Không tải được danh sách.' : 'Không có người dùng phù hợp.' }}
        columns={[
          { title: 'Họ tên', dataIndex: 'fullName' },
          { title: 'Liên hệ', render: (_, u) => [u.phone, u.email, u.username && `@${u.username}`].filter(Boolean).join(' · ') },
          { title: 'Vai trò', dataIndex: 'roles', render: (rs: string[]) => rs.map((r) => <Tag key={r}>{r}</Tag>) },
          {
            title: 'Trạng thái',
            dataIndex: 'status',
            render: (s: UserStatus, u) => <Tag color={STATUS[s].color} title={u.lockReason ?? undefined}>{STATUS[s].text}</Tag>,
          },
          { title: 'Tạo lúc', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
          { title: 'Đăng nhập gần nhất', dataIndex: 'lastLoginAt', render: (v: string | null) => (v ? formatDateTime(v) : '—') },
          {
            title: '',
            render: (_, u) => (
              <Space>
                {can(permissions, P.UserLock) && u.status === 'Active' && (
                  <Button size="small" danger onClick={() => setLocking(u)}>Khoá</Button>
                )}
                {can(permissions, P.UserLock) && u.status === 'Locked' && (
                  <Button size="small" onClick={() => unlock.mutate(u.id)}>Mở khoá</Button>
                )}
                {can(permissions, P.UserAssignRole) && roles.data && (
                  <Button size="small" onClick={() => {
                    setAssigning(u)
                    setRoleIds(roles.data.filter((r) => u.roles.includes(r.code)).map((r) => r.id))
                  }}>Vai trò</Button>
                )}
              </Space>
            ),
          },
        ]}
      />

      <Modal title={`Khoá tài khoản ${locking?.fullName ?? ''}`} open={!!locking} onCancel={() => setLocking(null)}
        okText="Khoá" okButtonProps={{ danger: true, disabled: !lockReason.trim(), loading: lock.isPending }}
        onOk={() => lock.mutate()} cancelText="Huỷ">
        <Typography.Paragraph type="secondary">Mọi phiên đăng nhập của người này sẽ bị cắt ngay.</Typography.Paragraph>
        <Input.TextArea rows={3} placeholder="Lý do khoá (bắt buộc)" value={lockReason} onChange={(e) => setLockReason(e.target.value)} />
      </Modal>

      <Modal title={`Vai trò của ${assigning?.fullName ?? ''}`} open={!!assigning} onCancel={() => setAssigning(null)}
        okText="Lưu" cancelText="Huỷ" onOk={() => assign.mutate()} okButtonProps={{ loading: assign.isPending }}>
        <Checkbox.Group value={roleIds} onChange={(v) => setRoleIds(v as string[])} style={{ display: 'flex', flexDirection: 'column', gap: 8 }}
          options={roles.data?.map((r) => ({ value: r.id, label: `${r.name} (${r.code})` })) ?? []} />
      </Modal>
    </Space>
  )
}

export default UsersPage
