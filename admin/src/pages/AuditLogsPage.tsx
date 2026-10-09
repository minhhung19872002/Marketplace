import { useState } from 'react'
import { Alert, App, Button, DatePicker, Input, Select, Space, Table, Tag, Typography } from 'antd'
import { useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import type { Dayjs } from 'dayjs'
import { adminApi, type AuditLog } from '../api/admin'
import { ApiError } from '../api/http'
import { platformApi, saveBlob } from '../api/platform'
import { formatDateTime, vnDayBoundsIso } from '../lib/datetime'
import { ToneTag } from '../components/StatusTag'

const ACTION_TONE = { CREATE: 'success', UPDATE: 'info', DELETE: 'error' } as const

const pretty = (json: string | null) => {
  if (!json) return '—'
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

const AuditLogsPage = () => {
  const { message } = App.useApp()
  // Opened from a user's detail (?userId=) or a parameter's history (?entity=SystemParameter&entityId=)
  const [params, setParams] = useSearchParams()
  const userId = params.get('userId') ?? undefined
  const entityId = params.get('entityId') ?? undefined
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [action, setAction] = useState<string | undefined>()
  const [entity, setEntity] = useState(params.get('entity') ?? '')
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null)

  // The picker yields calendar days; bounds are computed in Vietnam time
  const { from, to } = vnDayBoundsIso(range?.[0]?.format('YYYY-MM-DD'), range?.[1]?.format('YYYY-MM-DD'))
  const logs = useQuery({
    queryKey: ['audit', page, pageSize, action, entity, entityId, userId, from, to],
    queryFn: () => adminApi.auditLogs({ page, pageSize, action, entity, entityId, userId, from, to }),
  })
  const [exporting, setExporting] = useState(false)
  const exportExcel = async () => {
    setExporting(true)
    try {
      const started = await platformApi.startAuditExport({ action, entity, entityId, userId, from, to })
      message.info(started.message)
      for (let i = 0; i < 120; i++) {
        const t = await platformApi.myTask(started.data.id)
        if (t.status === 'Done') {
          saveBlob(await platformApi.myTaskFile(t.id), 'nhat-ky-thao-tac.xlsx')
          return
        }
        if (t.status === 'Failed') throw new Error(t.message ?? 'Không xuất được nhật ký.')
        await new Promise((r) => setTimeout(r, 1000))
      }
      message.warning('Tệp vẫn đang được tạo, vui lòng thử lại sau ít phút.')
    } catch (e) {
      message.error(e instanceof ApiError || e instanceof Error ? e.message : 'Không xuất được nhật ký.')
    } finally {
      setExporting(false)
    }
  }

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Title level={3} style={{ margin: 0 }}>Nhật ký thao tác</Typography.Title>
      <Space wrap>
        <Select placeholder="Hành động" allowClear style={{ width: 140 }} value={action}
          onChange={(v) => { setAction(v); setPage(1) }}
          options={[{ value: 'CREATE', label: 'Tạo' }, { value: 'UPDATE', label: 'Sửa' }, { value: 'DELETE', label: 'Xoá' }]} />
        <Input.Search placeholder="Đối tượng (VD: User)" allowClear style={{ width: 220 }} onSearch={(v) => { setEntity(v); setPage(1) }} />
        <DatePicker.RangePicker format="DD/MM/YYYY" value={range} onChange={(v) => { setRange(v); setPage(1) }} />
        {(userId || entityId) && (
          <Tag closable onClose={() => { setParams({}); setPage(1) }}>{userId ? 'Lọc theo người dùng' : 'Lọc theo đối tượng'}</Tag>
        )}
        <Button onClick={exportExcel} loading={exporting} data-testid="audit-export">Xuất Excel</Button>
      </Space>
      {logs.isError && <Alert type="error" showIcon message={logs.error instanceof ApiError ? logs.error.message : 'Không tải được nhật ký.'} />}
      <Table<AuditLog>
        rowKey="id"
        loading={logs.isPending}
        dataSource={logs.data?.items}
        pagination={{
          current: page,
          pageSize,
          total: logs.data?.totalCount,
          showSizeChanger: true,
          onChange: (p, s) => { setPage(p); setPageSize(s) },
        }}
        expandable={{
          expandedRowRender: (l) => (
            <div className="diff-grid">
              <div><Typography.Text strong>Trước</Typography.Text><pre>{pretty(l.oldValue)}</pre></div>
              <div><Typography.Text strong>Sau</Typography.Text><pre>{pretty(l.newValue)}</pre></div>
            </div>
          ),
        }}
        columns={[
          { title: 'Thời điểm', dataIndex: 'occurredAt', render: (v: string) => formatDateTime(v) },
          { title: 'Hành động', dataIndex: 'action', render: (a: AuditLog['action']) => <ToneTag tone={ACTION_TONE[a]}>{a}</ToneTag> },
          { title: 'Đối tượng', render: (_, l) => <>{l.entity} <Typography.Text type="secondary" code>{l.entityId}</Typography.Text></> },
          {
            title: 'Người làm',
            render: (_, l) => (l.userId ? <span title={l.userId}>{l.userName ?? l.userId}</span> : 'Hệ thống'),
          },
          { title: 'IP', dataIndex: 'ip', render: (v: string | null) => v ?? '—' },
        ]}
      />
    </Space>
  )
}

export default AuditLogsPage
