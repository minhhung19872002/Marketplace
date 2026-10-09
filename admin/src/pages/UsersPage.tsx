import { useState } from 'react'
import { App as AntApp, Button, Checkbox, Input, InputNumber, Modal, Select, Space, Tag, Typography } from 'antd'
import { DollarOutlined, KeyOutlined, LockOutlined, UnlockOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminApi, type AdminUser, type UserStatus } from '../api/admin'
import { promoApi } from '../api/promo'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { P, can } from '../permissions'
import { USER_STATUS } from '../lib/status'
import UserDetailDrawer from '../components/UserDetailDrawer'
import DataTable from '../components/DataTable'
import RowActions from '../components/RowActions'
import StatusTag from '../components/StatusTag'

const STATUSES = Object.keys(USER_STATUS) as UserStatus[]

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
  const [granting, setGranting] = useState<AdminUser | null>(null)
  const [coins, setCoins] = useState<number | null>(10000)
  const [coinReason, setCoinReason] = useState('')
  const [viewing, setViewing] = useState<string | null>(null)

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
  const grant = useMutation({
    mutationFn: () => promoApi.grantCoins(granting!.id, coins ?? 0, coinReason),
    onSuccess: (r) => {
      setGranting(null)
      setCoinReason('')
      done(`${r.message} Số dư mới: ${r.data} xu.`)
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
    <>
      <DataTable<AdminUser>
        header={{ title: 'Người dùng', description: 'Tra cứu tài khoản người mua, người bán và quản trị; khoá, gán vai trò, tặng hoặc thu hồi xu' }}
        search={{ value: q, onSearch: (v) => { setQ(v); setPage(1) }, placeholder: 'Tên, email, SĐT, tên đăng nhập' }}
        filters={(
          <>
            <Select<UserStatus> placeholder="Mọi trạng thái" allowClear style={{ width: 160 }} value={status} aria-label="Lọc theo trạng thái"
              onChange={(v) => { setStatus(v); setPage(1) }}
              options={STATUSES.map((s) => ({ value: s, label: USER_STATUS[s].label }))} />
            <Checkbox checked={adminsOnly} onChange={(e) => { setAdminsOnly(e.target.checked); setPage(1) }}>Chỉ quản trị viên</Checkbox>
          </>
        )}
        onReset={() => { setQ(''); setStatus(undefined); setAdminsOnly(false); setPage(1) }}
        rowKey="id"
        loading={users.isPending}
        fetching={users.isFetching && !users.isPending}
        error={users.error}
        dataSource={users.data?.items}
        emptyText="Không có người dùng phù hợp"
        paging={{ page, pageSize, total: users.data?.totalCount, sizeChanger: true, onChange: (p, s) => { setPage(p); setPageSize(s) } }}
        columns={[
          { title: 'Họ tên', dataIndex: 'fullName', render: (v: string, u) => <><span className="cell-main">{v}</span>{u.username && <span className="cell-sub">@{u.username}</span>}</> },
          { title: 'Liên hệ', render: (_, u) => <>{u.phone && <span className="cell-nowrap">{u.phone}</span>}{u.email && <span className="cell-sub">{u.email}</span>}</> },
          { title: 'Vai trò', dataIndex: 'roles', render: (rs: string[]) => rs.map((r) => <Tag key={r} bordered={false}>{r}</Tag>) },
          {
            title: 'Trạng thái',
            dataIndex: 'status',
            render: (s: UserStatus, u) => <StatusTag map={USER_STATUS} value={s} title={u.lockReason ?? undefined} />,
          },
          { title: 'Tạo lúc', dataIndex: 'createdAt', render: (v: string) => <span className="cell-nowrap">{formatDateTime(v)}</span> },
          { title: 'Đăng nhập gần nhất', dataIndex: 'lastLoginAt', render: (v: string | null) => <span className="cell-nowrap">{v ? formatDateTime(v) : '—'}</span> },
          {
            title: '', key: 'actions', align: 'right',
            render: (_, u) => (
              <RowActions name={u.fullName}
                primary={<Button size="small" onClick={() => setViewing(u.id)} data-testid="user-detail">Chi tiết</Button>}
                items={[
                  {
                    key: 'roles', icon: <KeyOutlined aria-hidden />, label: 'Gán vai trò', hidden: !(can(permissions, P.UserAssignRole) && roles.data),
                    onClick: () => {
                      setAssigning(u)
                      setRoleIds((roles.data ?? []).filter((r) => u.roles.includes(r.code)).map((r) => r.id))
                    },
                  },
                  { key: 'coins', icon: <DollarOutlined aria-hidden />, label: 'Tặng / thu hồi xu', testId: 'grant-coins', hidden: !can(permissions, P.CoinGrant), onClick: () => setGranting(u) },
                  { key: 'unlock', icon: <UnlockOutlined aria-hidden />, label: 'Mở khoá', hidden: !(can(permissions, P.UserLock) && u.status === 'Locked'),
                    confirm: { title: `Mở khoá tài khoản ${u.fullName}?`, okText: 'Mở khoá' }, onClick: () => unlock.mutateAsync(u.id).catch(() => undefined) },
                  // Opens the reason dialog: the reason is the confirmation
                  { key: 'lock', icon: <LockOutlined aria-hidden />, label: 'Khoá tài khoản', danger: true,
                    hidden: !(can(permissions, P.UserLock) && u.status === 'Active'), onClick: () => setLocking(u) },
                ]} />
            ),
          },
        ]}
      />

      <UserDetailDrawer userId={viewing} onClose={() => setViewing(null)} permissions={permissions} />

      <Modal title={`ShopHub Xu — ${granting?.fullName ?? ''}`} open={!!granting} onCancel={() => setGranting(null)} cancelText="Huỷ"
        okText="Cập nhật" okButtonProps={{ disabled: !coins || !coinReason.trim(), loading: grant.isPending }} onOk={() => grant.mutate()}>
        <Space direction="vertical" style={{ width: '100%' }}>
          <InputNumber value={coins} onChange={setCoins} step={1000} style={{ width: '100%' }} addonAfter="xu" placeholder="Số âm để thu hồi" />
          <Input.TextArea rows={2} placeholder="Lý do (bắt buộc)" value={coinReason} onChange={(e) => setCoinReason(e.target.value)} />
        </Space>
      </Modal>

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
    </>
  )
}

export default UsersPage
