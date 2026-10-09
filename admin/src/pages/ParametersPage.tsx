import { useState } from 'react'
import { App as AntApp, Button, Input, Modal, Select, Space, Tag, Typography } from 'antd'
import { EditOutlined, HistoryOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminApi, type SystemParameter } from '../api/admin'
import { jobsApi } from '../api/jobs'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { P, can } from '../permissions'
import { useNavigate } from 'react-router-dom'
import DataTable from '../components/DataTable'
import RowActions from '../components/RowActions'

// Names of the known groups; any other group the API returns is listed under its own code
const GROUP_LABEL: Record<string, string> = {
  SITE: 'Thông tin sàn',
  AUTH: 'Đăng nhập & bảo mật',
  ACCOUNT: 'Người dùng',
  JOB: 'Việc nền',
  PAYMENT: 'Thanh toán',
  LOGISTICS: 'Vận chuyển',
  ORDER: 'Đơn hàng',
  CART: 'Giỏ hàng',
  RETURN: 'Trả hàng',
  FINANCE: 'Tài chính',
  MARKETING: 'Marketing',
  PRODUCT: 'Sản phẩm',
  MEDIA: 'Ảnh & video',
  REVIEW: 'Đánh giá',
  COIN: 'ShopHub Xu',
  MEMBER: 'Hạng thành viên',
  CHAT: 'Chat',
  SEARCH: 'Tìm kiếm',
  SHOP: 'Shop',
}

/** Case- and accent-insensitive "contains". */
const fold = (v: string) => v.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase()

const ParametersPage = ({ permissions }: { permissions: string[] }) => {
  const navigate = useNavigate()
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const [group, setGroup] = useState<string | undefined>()
  const [q, setQ] = useState('')
  const [editing, setEditing] = useState<SystemParameter | null>(null)
  const [value, setValue] = useState('')
  const [error, setError] = useState('')
  // Every parameter at once (a few hundred at most): groups and search filter on the client
  const params = useQuery({ queryKey: ['parameters', ''], queryFn: () => adminApi.parameters() })
  const groups = [...new Set((params.data ?? []).map((p) => p.group))].sort((a, b) => (GROUP_LABEL[a] ?? a).localeCompare(GROUP_LABEL[b] ?? b, 'vi'))
  const rows = (params.data ?? []).filter((p) => (!group || p.group === group) && (!q || [p.key, p.name, p.description].some((v) => fold(v).includes(fold(q)))))
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
    <>
      <DataTable<SystemParameter>
        header={{ title: 'Tham số hệ thống', description: 'Cấu hình vận hành của sàn theo nhóm; mỗi lần sửa được ghi nhật ký, lịch việc nền đăng ký lại ngay' }}
        search={{ value: q, onSearch: setQ, placeholder: 'Tên, khoá hoặc mô tả tham số' }}
        filters={(
          <Select allowClear placeholder="Mọi nhóm" style={{ width: 200 }} value={group} onChange={setGroup} aria-label="Lọc theo nhóm"
            options={groups.map((g) => ({ value: g, label: GROUP_LABEL[g] ?? g }))} />
        )}
        onReset={() => { setQ(''); setGroup(undefined) }}
        rowKey="key"
        loading={params.isPending}
        error={params.error}
        dataSource={rows}
        paging="client"
        emptyText="Không có tham số phù hợp"
        columns={[
          {
            title: 'Tham số',
            render: (_, p) => (
              <div style={{ maxWidth: 460 }}>
                <span className="cell-main">{p.name}</span> <Typography.Text type="secondary" code>{p.key}</Typography.Text>
                <span className="cell-sub">{p.description}</span>
              </div>
            ),
          },
          {
            title: 'Giá trị', dataIndex: 'value', render: (v: string, p) => {
              const next = p.dataType === 'Cron' ? nextRun(p.key) : null
              return (
                <Space direction="vertical" size={0} style={{ maxWidth: 300 }}>
                  <Space wrap size={6}><span style={{ wordBreak: 'break-all' }}>{v}</span><Tag bordered={false}>{p.dataType === 'Cron' ? 'Cron · giờ Việt Nam' : p.dataType}</Tag></Space>
                  {next && <Typography.Text type="secondary" data-testid={`next-run-${p.key}`}>Lần chạy tới: {formatDateTime(`${next}${next.endsWith('Z') ? '' : 'Z'}`)}</Typography.Text>}
                </Space>
              )
            },
          },
          { title: 'Sửa lúc', dataIndex: 'updatedAt', render: (v: string | null) => <span className="cell-nowrap">{v ? formatDateTime(v) : '—'}</span> },
          {
            title: '', key: 'actions', align: 'right',
            render: (_, p) => (
              <RowActions name={p.name}
                primary={can(permissions, P.SystemParameterUpdate) && (
                  <Button size="small" icon={<EditOutlined aria-hidden />} onClick={() => { setEditing(p); setValue(p.value); setError('') }}>Sửa</Button>
                )}
                items={[{
                  key: 'history', icon: <HistoryOutlined aria-hidden />, label: 'Lịch sử thay đổi', hidden: !can(permissions, P.AuditLogView),
                  onClick: () => navigate(`/nhat-ky?entity=SystemParameter&entityId=${p.id}`),
                }]} />
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
    </>
  )
}

export default ParametersPage
