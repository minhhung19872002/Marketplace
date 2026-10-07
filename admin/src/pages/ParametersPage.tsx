import { useState } from 'react'
import { App as AntApp, Button, Input, Modal, Segmented, Space, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminApi, type SystemParameter } from '../api/admin'
import { jobsApi } from '../api/jobs'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { P, can } from '../permissions'
import { useNavigate } from 'react-router-dom'

const GROUPS = [
  { label: 'Tất cả', value: '' },
  { label: 'Thông tin sàn', value: 'SITE' },
  { label: 'Tài khoản', value: 'AUTH' },
  { label: 'Người dùng', value: 'ACCOUNT' },
  { label: 'Việc nền', value: 'JOB' },
]

const ParametersPage = ({ permissions }: { permissions: string[] }) => {
  const navigate = useNavigate()
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const [group, setGroup] = useState('')
  const [editing, setEditing] = useState<SystemParameter | null>(null)
  const [value, setValue] = useState('')
  const [error, setError] = useState('')
  const params = useQuery({ queryKey: ['parameters', group], queryFn: () => adminApi.parameters(group || undefined) })
  // Next run of each schedule as Hangfire computes it (only for admins who may see the jobs)
  const jobs = useQuery({ queryKey: ['jobs'], queryFn: jobsApi.list, enabled: can(permissions, P.JobDashboardView) })
  const nextRun = (key: string) => jobs.data?.find((j) => j.parameterKey === key)?.nextExecution ?? null

  const save = useMutation({
    mutationFn: () => adminApi.updateParameter(editing!.key, value, editing!.version),
    onSuccess: (r) => {
      setEditing(null)
      void message.success(r.message)
      void queryClient.invalidateQueries({ queryKey: ['parameters'] })
    },
    onError: (err) => setError(err instanceof ApiError ? err.fieldErrors[0]?.message ?? err.message : 'Lưu thất bại.'),
  })

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Title level={3} style={{ margin: 0 }}>Tham số hệ thống</Typography.Title>
      <Segmented options={GROUPS} value={group} onChange={(v) => setGroup(String(v))} />
      <Table<SystemParameter>
        rowKey="key"
        loading={params.isPending}
        dataSource={params.data}
        pagination={false}
        columns={[
          { title: 'Tham số', render: (_, p) => <><div>{p.name}</div><Typography.Text type="secondary" code>{p.key}</Typography.Text></> },
          {
            title: 'Giá trị', dataIndex: 'value', render: (v: string, p) => {
              const next = p.dataType === 'Cron' ? nextRun(p.key) : null
              return (
                <Space direction="vertical" size={0}>
                  <Space><span>{v}</span><Tag>{p.dataType === 'Cron' ? 'Cron · giờ Việt Nam' : p.dataType}</Tag></Space>
                  {next && <Typography.Text type="secondary" data-testid={`next-run-${p.key}`}>Lần chạy tới: {formatDateTime(`${next}${next.endsWith('Z') ? '' : 'Z'}`)}</Typography.Text>}
                </Space>
              )
            },
          },
          { title: 'Mô tả', dataIndex: 'description' },
          { title: 'Sửa lúc', dataIndex: 'updatedAt', render: (v: string | null) => (v ? formatDateTime(v) : '—') },
          {
            title: '',
            render: (_, p) => (
              <Space>
                {can(permissions, P.SystemParameterUpdate) && (
                  <Button size="small" onClick={() => { setEditing(p); setValue(p.value); setError('') }}>Sửa</Button>
                )}
                {can(permissions, P.AuditLogView) && (
                  <Button size="small" onClick={() => navigate(`/nhat-ky?entity=SystemParameter&entityId=${p.id}`)}>Lịch sử</Button>
                )}
              </Space>
            ),
          },
        ]}
      />
      <Modal title={editing?.name} open={!!editing} onCancel={() => setEditing(null)} okText="Lưu" cancelText="Huỷ"
        onOk={() => save.mutate()} okButtonProps={{ loading: save.isPending }}>
        <Typography.Paragraph type="secondary">{editing?.description}</Typography.Paragraph>
        <Input value={value} onChange={(e) => setValue(e.target.value)} status={error ? 'error' : undefined} />
        {error && <Typography.Text type="danger">{error}</Typography.Text>}
      </Modal>
    </Space>
  )
}

export default ParametersPage
