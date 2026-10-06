import { useMemo, useState } from 'react'
import { App as AntApp, Button, Checkbox, Form, Input, Modal, Popconfirm, Space, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminApi, type Role } from '../api/admin'
import { ApiError } from '../api/http'
import { P, can } from '../permissions'

interface RoleForm {
  code: string
  name: string
  description: string
  permissions: string[]
}

const RolesPage = ({ permissions }: { permissions: string[] }) => {
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const roles = useQuery({ queryKey: ['roles'], queryFn: adminApi.roles })
  const catalog = useQuery({ queryKey: ['permissions'], queryFn: adminApi.permissions })
  const [editing, setEditing] = useState<Role | 'new' | null>(null)
  const [form] = Form.useForm<RoleForm>()
  const canManage = can(permissions, P.RoleManage)

  const byModule = useMemo(() => {
    const groups = new Map<string, { label: string; value: string }[]>()
    for (const p of catalog.data ?? []) groups.set(p.module, [...(groups.get(p.module) ?? []), { label: p.name, value: p.code }])
    return [...groups.entries()]
  }, [catalog.data])

  const refresh = (text: string) => {
    void message.success(text)
    void queryClient.invalidateQueries({ queryKey: ['roles'] })
  }
  const fail = (err: unknown) => void message.error(err instanceof ApiError ? err.fieldErrors[0]?.message ?? err.message : 'Thao tác thất bại.')

  const save = useMutation({
    mutationFn: (values: RoleForm) => {
      const body = { ...values, description: values.description ?? '', permissions: values.permissions ?? [] }
      return editing && editing !== 'new' ? adminApi.updateRole(editing.id, body) : adminApi.createRole(body)
    },
    onSuccess: (r) => {
      setEditing(null)
      refresh(r.message)
    },
    onError: fail,
  })
  const remove = useMutation({ mutationFn: adminApi.deleteRole, onSuccess: (r) => refresh(r.message), onError: fail })

  const open = (role: Role | 'new') => {
    setEditing(role)
    form.setFieldsValue(role === 'new'
      ? { code: '', name: '', description: '', permissions: [] }
      : { code: role.code, name: role.name, description: role.description, permissions: role.permissions })
  }

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Space style={{ justifyContent: 'space-between', width: '100%' }}>
        <Typography.Title level={3} style={{ margin: 0 }}>Vai trò &amp; quyền</Typography.Title>
        {canManage && <Button type="primary" onClick={() => open('new')}>Thêm vai trò</Button>}
      </Space>
      <Table<Role>
        rowKey="id"
        loading={roles.isPending}
        dataSource={roles.data}
        pagination={false}
        columns={[
          { title: 'Mã', dataIndex: 'code', render: (c: string, r) => <Space>{c}{r.isSystem && <Tag>Hệ thống</Tag>}</Space> },
          { title: 'Tên', dataIndex: 'name' },
          {
            title: 'Quyền',
            dataIndex: 'permissions',
            render: (ps: string[]) => (ps.includes('*') ? <Tag color="gold">Toàn quyền</Tag> : `${ps.length} quyền`),
          },
          { title: 'Số người', dataIndex: 'userCount' },
          {
            title: '',
            render: (_, r) => canManage && (
              <Space>
                <Button size="small" onClick={() => open(r)}>Sửa</Button>
                {!r.isSystem && (
                  <Popconfirm title="Xoá vai trò này?" okText="Xoá" cancelText="Huỷ" onConfirm={() => remove.mutate(r.id)}>
                    <Button size="small" danger>Xoá</Button>
                  </Popconfirm>
                )}
              </Space>
            ),
          },
        ]}
      />

      <Modal title={editing === 'new' ? 'Thêm vai trò' : 'Sửa vai trò'} open={!!editing} width={720} onCancel={() => setEditing(null)}
        okText="Lưu" cancelText="Huỷ" onOk={() => form.submit()} okButtonProps={{ loading: save.isPending }}>
        <Form<RoleForm> form={form} layout="vertical" onFinish={(v) => save.mutate(v)} requiredMark={false}>
          <Form.Item label="Mã vai trò" name="code" rules={[{ required: true, message: 'Vui lòng nhập mã.' }]}>
            <Input disabled={editing !== 'new'} placeholder="VD: KIEM_DUYET" />
          </Form.Item>
          <Form.Item label="Tên" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên.' }]}>
            <Input />
          </Form.Item>
          <Form.Item label="Mô tả" name="description">
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item label="Quyền" name="permissions">
            <Checkbox.Group style={{ width: '100%' }}>
              <Space direction="vertical" style={{ width: '100%' }}>
                {byModule.map(([module, options]) => (
                  <div key={module}>
                    <Typography.Text strong>{module}</Typography.Text>
                    <div className="perm-grid">
                      {options.map((o) => <Checkbox key={o.value} value={o.value}>{o.label}</Checkbox>)}
                    </div>
                  </div>
                ))}
              </Space>
            </Checkbox.Group>
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  )
}

export default RolesPage
