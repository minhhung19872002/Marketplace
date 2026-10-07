import { useState } from 'react'
import { App, Button, Card, Checkbox, Form, Input, Modal, Popconfirm, Select, Space, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { PERMISSION_LABELS, ROLE_LABELS, staffApi, type ShopStaff, type StaffInvitation, type StaffRole } from '../api/staff'
import { formatDate } from '../lib/datetime'

type Draft = { id?: string; login: string; role: Exclude<StaffRole, 'Owner'>; permissions: string[] }

/** Thiết lập shop → Tài khoản phụ: add existing ShopHub accounts as staff, pick a role and the grants. */
const StaffPage = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const board = useQuery({ queryKey: ['staff', shopId], queryFn: () => staffApi.board(shopId) })
  const [draft, setDraft] = useState<Draft | null>(null)

  const done = (r: { message: string }) => {
    message.success(r.message)
    setDraft(null)
    void queryClient.invalidateQueries({ queryKey: ['staff', shopId] })
  }
  const fail = (e: unknown) => message.error(e instanceof ApiError ? e.message : 'Không lưu được.')
  const save = useMutation({
    mutationFn: (d: Draft) => d.id
      ? staffApi.update(shopId, d.id, { role: d.role, permissions: d.permissions })
      : staffApi.add(shopId, { login: d.login.trim(), role: d.role, permissions: d.permissions }),
    onSuccess: done,
    onError: fail,
  })
  const remove = useMutation({ mutationFn: (id: string) => staffApi.remove(shopId, id), onSuccess: done, onError: fail })
  const revoke = useMutation({ mutationFn: (id: string) => staffApi.revokeInvitation(shopId, id), onSuccess: done, onError: fail })

  if (board.isError) {
    return <Card title="Tài khoản phụ"><Typography.Text type="danger">{board.error instanceof ApiError ? board.error.message : 'Không tải được.'}</Typography.Text></Card>
  }
  const data = board.data
  const defaults = (role: Draft['role']) => data?.roleDefaults[role] ?? []

  return (
    <Card
      title="Tài khoản phụ"
      extra={
        <Button type="primary" data-testid="staff-add" disabled={!data || data.staff.length >= data.maxStaff}
          onClick={() => setDraft({ login: '', role: 'CustomerService', permissions: defaults('CustomerService') })}>
          Mời nhân viên
        </Button>
      }
    >
      <Typography.Paragraph type="secondary">
        Nhân viên đăng nhập Kênh Người Bán bằng tài khoản ShopHub của chính họ và chỉ thấy các mục được cấp quyền. Người được mời vào shop sau khi
        đồng ý lời mời. Gỡ nhân viên có hiệu lực ngay.
        {data && ` Tối đa ${data.maxStaff} tài khoản.`}
      </Typography.Paragraph>
      <Table<ShopStaff>
        rowKey="id"
        loading={board.isLoading}
        dataSource={data?.staff ?? []}
        pagination={false}
        locale={{ emptyText: 'Chưa có nhân viên' }}
        columns={[
          { title: 'Nhân viên', render: (_, s) => <Space direction="vertical" size={0}><Typography.Text strong>{s.fullName}</Typography.Text><Typography.Text type="secondary">{s.phoneMasked ?? s.emailMasked}</Typography.Text></Space> },
          { title: 'Vai trò', render: (_, s) => <Tag color={s.role === 'Owner' ? 'gold' : undefined}>{ROLE_LABELS[s.role]}</Tag> },
          {
            title: 'Quyền',
            render: (_, s) => s.role === 'Owner' ? <Typography.Text type="secondary">Toàn quyền</Typography.Text>
              : <Space wrap size={[4, 4]}>{s.permissions.map((p) => <Tag key={p}>{PERMISSION_LABELS[p] ?? p}</Tag>)}</Space>,
          },
          { title: 'Từ ngày', render: (_, s) => formatDate(s.createdAt), width: 110 },
          {
            title: '',
            width: 150,
            render: (_, s) => s.role === 'Owner' ? null : (
              <Space>
                <Button size="small" onClick={() => setDraft({ id: s.id, login: s.fullName, role: s.role as Draft['role'], permissions: s.permissions })}>Sửa</Button>
                <Popconfirm title={`Gỡ ${s.fullName} khỏi shop?`} okText="Gỡ" cancelText="Không" onConfirm={() => remove.mutate(s.id)}>
                  <Button size="small" danger data-testid="staff-remove">Gỡ</Button>
                </Popconfirm>
              </Space>
            ),
          },
        ]}
      />

      {(data?.invitations.length ?? 0) > 0 && (
        <>
          <Typography.Title level={5} style={{ marginTop: 24 }}>Lời mời đang chờ trả lời</Typography.Title>
          <Table<StaffInvitation>
            rowKey="id"
            size="small"
            dataSource={data?.invitations ?? []}
            pagination={false}
            data-testid="staff-invitations"
            columns={[
              { title: 'Người được mời', render: (_, i) => <Space direction="vertical" size={0}><Typography.Text strong>{i.fullName}</Typography.Text><Typography.Text type="secondary">{i.phoneMasked ?? i.emailMasked}</Typography.Text></Space> },
              { title: 'Vai trò', render: (_, i) => <Tag>{ROLE_LABELS[i.role]}</Tag> },
              { title: 'Hết hạn', render: (_, i) => formatDateTime(i.expiresAt) },
              {
                title: '', render: (_, i) => (
                  <Popconfirm title="Thu hồi lời mời này?" okText="Thu hồi" cancelText="Không" onConfirm={() => revoke.mutate(i.id)}>
                    <Button size="small" danger>Thu hồi</Button>
                  </Popconfirm>
                ),
              },
            ]}
          />
        </>
      )}

      <Modal
        open={draft !== null}
        title={draft?.id ? `Sửa quyền — ${draft.login}` : 'Mời nhân viên'}
        okText="Lưu"
        cancelText="Huỷ"
        confirmLoading={save.isPending}
        onCancel={() => setDraft(null)}
        onOk={() => draft && save.mutate(draft)}
        okButtonProps={{ disabled: !draft || (!draft.id && draft.login.trim() === '') }}
        destroyOnClose
      >
        {draft && data && (
          <Form layout="vertical">
            {!draft.id && (
              <Form.Item label="Số điện thoại hoặc email của tài khoản ShopHub" required>
                <Input value={draft.login} onChange={(e) => setDraft({ ...draft, login: e.target.value })} data-testid="staff-login" autoFocus />
              </Form.Item>
            )}
            <Form.Item label="Vai trò">
              <Select value={draft.role} data-testid="staff-role"
                onChange={(role: Draft['role']) => setDraft({ ...draft, role, permissions: defaults(role) })}
                options={(['Manager', 'CustomerService', 'Warehouse'] as const).map((r) => ({ value: r, label: ROLE_LABELS[r] }))} />
            </Form.Item>
            <Form.Item label="Quyền (mặc định theo vai trò, chỉnh được)">
              <Checkbox.Group value={draft.permissions} onChange={(v) => setDraft({ ...draft, permissions: v as string[] })}>
                <Space direction="vertical">
                  {data.allPermissions.map((p) => <Checkbox key={p} value={p}>{PERMISSION_LABELS[p] ?? p}</Checkbox>)}
                </Space>
              </Checkbox.Group>
            </Form.Item>
          </Form>
        )}
      </Modal>
    </Card>
  )
}

export default StaffPage
