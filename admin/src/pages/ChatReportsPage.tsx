import { useState } from 'react'
import { Alert, App, Button, Card, Drawer, Input, InputNumber, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { platformApi, type ChatReportRow, type ChatReportStatus } from '../api/platform'
import { formatDateTime } from '../lib/datetime'

const STATUS: Record<ChatReportStatus, { label: string; color?: string }> = {
  Open: { label: 'Chờ xử lý', color: 'gold' },
  Dismissed: { label: 'Không vi phạm' },
  Penalized: { label: 'Đã phạt', color: 'red' },
}

/** Chat bị báo cáo: read the conversation (every opening is logged), dismiss or give the shop penalty points. */
const ChatReportsPage = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<ChatReportStatus>('Open')
  const [page, setPage] = useState(1)
  const [openId, setOpenId] = useState<string | null>(null)
  const [resolution, setResolution] = useState('')
  const [penalize, setPenalize] = useState(false)
  const [points, setPoints] = useState<number>(2)

  const list = useQuery({
    queryKey: ['chat-reports', status, page],
    queryFn: () => platformApi.chatReports(status, page),
    placeholderData: keepPreviousData,
  })
  const detail = useQuery({ queryKey: ['chat-report', openId], queryFn: () => platformApi.chatReport(openId!), enabled: !!openId })
  const resolve = useMutation({
    mutationFn: () => platformApi.resolveChatReport(openId!, resolution.trim(), penalize ? points : null),
    onSuccess: (r) => {
      message.success(r.message)
      setOpenId(null)
      void queryClient.invalidateQueries({ queryKey: ['chat-reports'] })
    },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không xử lý được.'),
  })

  const open = (row: ChatReportRow) => {
    setOpenId(row.id)
    setResolution('')
    setPenalize(false)
    setPoints(2)
  }
  const report = detail.data?.report

  return (
    <Card title="Chat bị báo cáo">
      <Tabs activeKey={status} onChange={(k) => { setStatus(k as ChatReportStatus); setPage(1) }}
        items={(Object.keys(STATUS) as ChatReportStatus[]).map((k) => ({ key: k, label: STATUS[k].label }))} />
      <Table<ChatReportRow>
        rowKey="id"
        loading={list.isLoading}
        dataSource={list.data?.items ?? []}
        locale={{ emptyText: 'Không có báo cáo' }}
        pagination={list.data && list.data.totalCount > list.data.pageSize
          ? { current: page, pageSize: list.data.pageSize, total: list.data.totalCount, onChange: setPage } : false}
        columns={[
          { title: 'Shop', dataIndex: 'shopName' },
          { title: 'Người báo cáo', dataIndex: 'reporterName' },
          { title: 'Lý do', dataIndex: 'reason' },
          { title: 'Số báo cáo', dataIndex: 'reportsOnConversation', width: 100 },
          { title: 'Lúc', width: 160, render: (_, r) => formatDateTime(r.createdAt) },
          { title: 'Trạng thái', width: 130, render: (_, r) => <Tag color={STATUS[r.status].color}>{STATUS[r.status].label}</Tag> },
          { title: '', width: 120, render: (_, r) => <Button size="small" onClick={() => open(r)} data-testid="chat-report-open">Xem hội thoại</Button> },
        ]}
      />

      <Drawer open={!!openId} width={640} onClose={() => setOpenId(null)} title={report ? `Hội thoại với ${report.shopName}` : 'Hội thoại'} destroyOnClose>
        <Alert type="info" showIcon style={{ marginBottom: 12 }} message="Mỗi lần mở hội thoại đều được ghi vào nhật ký thao tác." />
        {report && (
          <Space direction="vertical" style={{ width: '100%' }}>
            <Typography.Text>Báo cáo của {report.reporterName}: <b>{report.reason}</b></Typography.Text>
            {detail.data && detail.data.totalMessages > detail.data.messages.length && (
              <Typography.Text type="secondary">Hiện {detail.data.messages.length} / {detail.data.totalMessages} tin gần nhất.</Typography.Text>
            )}
            <div className="chat-review" data-testid="chat-review">
              {detail.data?.messages.map((m) => (
                <div key={m.id} className={`chat-review-msg chat-review-${m.senderRole.toLowerCase()}`}>
                  <Typography.Text type="secondary">{m.senderRole === 'Buyer' ? 'Người mua' : m.senderRole === 'Shop' ? 'Shop' : 'Hệ thống'} · {formatDateTime(m.createdAt)}</Typography.Text>
                  <div>{m.type === 'Text' ? m.body : `[${m.type}] ${m.body}`}</div>
                  {m.flagged && <Tag color="orange">Có thông tin liên hệ ngoài sàn</Tag>}
                </div>
              ))}
            </div>
            {report.status === 'Open' ? (
              <Space direction="vertical" style={{ width: '100%' }}>
                <Input.TextArea rows={3} maxLength={500} placeholder="Kết quả xử lý (bắt buộc)" value={resolution}
                  onChange={(e) => setResolution(e.target.value)} data-testid="chat-report-resolution" />
                <Space>
                  <Switch checked={penalize} onChange={setPenalize} data-testid="chat-report-penalize" />
                  <span>Ghi điểm phạt cho shop</span>
                  {penalize && <InputNumber min={1} max={20} value={points} onChange={(v) => setPoints(v ?? 1)} addonAfter="điểm" />}
                </Space>
                <Button type="primary" danger={penalize} disabled={!resolution.trim()} loading={resolve.isPending} onClick={() => resolve.mutate()}
                  data-testid="chat-report-resolve">
                  {penalize ? `Phạt ${points} điểm và đóng báo cáo` : 'Không vi phạm — đóng báo cáo'}
                </Button>
              </Space>
            ) : (
              <Alert type="success" message={`${STATUS[report.status].label}: ${report.resolution ?? ''}`} />
            )}
          </Space>
        )}
      </Drawer>
    </Card>
  )
}

export default ChatReportsPage
