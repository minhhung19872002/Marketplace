import { useState } from 'react'
import { Alert, App, Button, Form, Input, InputNumber, Modal, Space, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { platformApi, type Penalty, type PenaltyLevel } from '../api/platform'
import { formatDateTime } from '../lib/datetime'
import { ToneTag } from './StatusTag'

const LEVEL: Record<PenaltyLevel, { text: string; type: 'success' | 'info' | 'warning' | 'error' }> = {
  None: { text: 'Không bị hạn chế', type: 'success' },
  Restricted: { text: 'Hạn chế hiển thị', type: 'info' },
  CampaignBan: { text: 'Cấm tham gia chiến dịch', type: 'warning' },
  Locked: { text: 'Khoá shop', type: 'error' },
}

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : fallback)

/** VI.3 điểm phạt: the total is the sum of the rows that still count; thresholds and their consequence. */
const ShopPenalties = ({ shopId, canEdit }: { shopId: string; canEdit: boolean }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [adding, setAdding] = useState(false)
  const [revoking, setRevoking] = useState<Penalty | null>(null)
  const [reason, setReason] = useState('')
  const [form] = Form.useForm<{ points: number; reason: string; expiresInDays?: number }>()
  const data = useQuery({ queryKey: ['penalties', shopId], queryFn: () => platformApi.penalties(shopId) })
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['penalties', shopId] })
    void queryClient.invalidateQueries({ queryKey: ['shop', shopId] })
    void queryClient.invalidateQueries({ queryKey: ['shops'] })
  }
  const add = useMutation({
    mutationFn: (v: { points: number; reason: string; expiresInDays?: number }) => platformApi.addPenalty(shopId, { ...v, expiresInDays: v.expiresInDays ?? null }),
    onSuccess: (r) => { message.success(r.message); setAdding(false); form.resetFields(); refresh() },
    onError: (e) => message.error(errorText(e, 'Không ghi được điểm phạt.')),
  })
  const revoke = useMutation({
    mutationFn: () => platformApi.revokePenalty(revoking!.id, reason),
    onSuccess: (r) => { message.success(r.message); setRevoking(null); setReason(''); refresh() },
    onError: (e) => message.error(errorText(e, 'Không gỡ được điểm phạt.')),
  })
  const s = data.data?.status
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Space style={{ justifyContent: 'space-between', width: '100%' }}>
        <Typography.Title level={5} style={{ margin: 0 }}>Điểm phạt</Typography.Title>
        {canEdit && <Button size="small" onClick={() => setAdding(true)} data-testid="add-penalty">Ghi điểm phạt</Button>}
      </Space>
      {s && (
        <Alert type={LEVEL[s.level].type} showIcon message={`${s.points} điểm — ${LEVEL[s.level].text}`}
          description={`${s.consequence} Ngưỡng: hạn chế hiển thị ${s.restrictAt}, cấm chiến dịch ${s.campaignBanAt}, khoá ${s.lockAt} điểm.`} />
      )}
      <Table<Penalty> size="small" rowKey="id" loading={data.isLoading} dataSource={data.data?.items ?? []} pagination={false}
        columns={[
          { title: 'Điểm', dataIndex: 'points', align: 'right' },
          { title: 'Lý do', render: (_, p) => `${p.reason}${p.orderCode ? ` (đơn ${p.orderCode})` : ''}` },
          { title: 'Ghi bởi', dataIndex: 'givenBy' },
          { title: 'Lúc', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
          { title: 'Hết hạn', dataIndex: 'expiresAt', render: (v: string | null) => (v ? formatDateTime(v) : 'Không') },
          {
            title: 'Tính điểm', key: 'c',
            render: (_, p) => p.revokedAt ? <Tag title={p.revokeReason ?? undefined}>Đã gỡ</Tag> : p.counts ? <ToneTag tone="error">Đang tính</ToneTag> : <Tag>Hết hạn</Tag>,
          },
          { title: '', key: 'x', render: (_, p) => canEdit && p.counts && <Button size="small" onClick={() => setRevoking(p)}>Gỡ</Button> },
        ]} />
      <Modal open={adding} title="Ghi điểm phạt" okText="Ghi" confirmLoading={add.isPending} onCancel={() => setAdding(false)} onOk={() => form.submit()}>
        <Form form={form} layout="vertical" onFinish={(v) => add.mutate(v)} initialValues={{ points: 1 }}>
          <Form.Item name="points" label="Số điểm" rules={[{ required: true }]}><InputNumber min={1} max={20} /></Form.Item>
          <Form.Item name="reason" label="Lý do (gửi cho shop)" rules={[{ required: true, message: 'Nhập lý do.' }]}><Input.TextArea rows={2} /></Form.Item>
          <Form.Item name="expiresInDays" label="Hết hạn sau (ngày) — để trống dùng mặc định"><InputNumber min={1} max={730} /></Form.Item>
        </Form>
      </Modal>
      <Modal open={!!revoking} title="Gỡ điểm phạt" okText="Gỡ" okButtonProps={{ disabled: !reason.trim() }} confirmLoading={revoke.isPending}
        onCancel={() => setRevoking(null)} onOk={() => revoke.mutate()}>
        <Input.TextArea rows={2} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Lý do gỡ" />
      </Modal>
    </Space>
  )
}

export default ShopPenalties
