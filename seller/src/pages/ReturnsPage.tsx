import { useState } from 'react'
import { App, Button, Card, Checkbox, Descriptions, Drawer, Image, Input, InputNumber, Space, Table, Tabs, Tag, Timeline, Typography } from 'antd'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { aftercareApi, RETURN_REASON_LABELS, type ReturnInfo, type ReturnStatus, type ShopReturnAction } from '../api/aftercare'
import { uploadMedia } from '../api/seller'
import { ApiError } from '../api/http'
import { formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'

const TABS: { key: ReturnStatus | 'All'; label: string }[] = [
  { key: 'All', label: 'Tất cả' },
  { key: 'Requested', label: 'Chờ phản hồi' },
  { key: 'PartialOffered', label: 'Đã đề nghị' },
  { key: 'AwaitingReturn', label: 'Chờ gửi hàng' },
  { key: 'Returning', label: 'Đang trả về' },
  { key: 'AwaitingShopCheck', label: 'Chờ kiểm hàng' },
  { key: 'Disputed', label: 'Khiếu nại' },
  { key: 'Refunded', label: 'Đã hoàn tiền' },
  { key: 'Closed', label: 'Đã đóng' },
]

const statusColor: Partial<Record<ReturnStatus, string>> = {
  Requested: 'orange', PartialOffered: 'gold', Rejected: 'red', Disputed: 'volcano', Refunded: 'green', Closed: 'default', Cancelled: 'default',
}

const ReturnDrawer = ({ shopId, item, onClose }: { shopId: string; item: ReturnInfo; onClose: () => void }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [note, setNote] = useState('')
  const [amount, setAmount] = useState<number | null>(null)
  const [restock, setRestock] = useState(true)
  const [evidence, setEvidence] = useState<string[]>([])
  const act = useMutation({
    mutationFn: (action: ShopReturnAction) =>
      aftercareApi.act(shopId, item.id, { action, note: note.trim() || undefined, amount: amount ?? undefined, restock, evidenceAssetIds: evidence }),
    onSuccess: (r) => {
      message.success(r.message)
      void queryClient.invalidateQueries({ queryKey: ['returns', shopId] })
      onClose()
    },
    onError: (e) => message.error(e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : 'Không cập nhật được.'),
  })
  const responding = item.status === 'Requested'
  const checking = item.status === 'AwaitingShopCheck' || item.status === 'Returning'

  return (
    <Drawer open width={640} onClose={onClose} title={`Yêu cầu ${item.code}`} destroyOnClose>
      <Descriptions column={1} size="small" bordered>
        <Descriptions.Item label="Đơn hàng">{item.orderCode}</Descriptions.Item>
        <Descriptions.Item label="Trạng thái"><Tag color={statusColor[item.status]}>{item.statusLabel}</Tag></Descriptions.Item>
        <Descriptions.Item label="Hình thức">{item.type === 'RefundOnly' ? 'Chỉ hoàn tiền' : 'Trả hàng & hoàn tiền'}</Descriptions.Item>
        <Descriptions.Item label="Lý do">{RETURN_REASON_LABELS[item.reason] ?? item.reason}</Descriptions.Item>
        <Descriptions.Item label="Mô tả">{item.description}</Descriptions.Item>
        <Descriptions.Item label="Yêu cầu hoàn">{formatPrice(item.requestedAmount)}</Descriptions.Item>
        {item.respondBy && <Descriptions.Item label="Hạn phản hồi">{formatDateTime(item.respondBy)}</Descriptions.Item>}
        {item.returnTrackingNo && <Descriptions.Item label="Vận đơn trả">{item.returnTrackingNo}</Descriptions.Item>}
        {item.disputeReason && <Descriptions.Item label="Khiếu nại">{item.disputeReason}</Descriptions.Item>}
      </Descriptions>
      <Table
        style={{ marginTop: 16 }}
        size="small"
        rowKey="orderItemId"
        pagination={false}
        dataSource={item.items}
        columns={[
          { title: 'Sản phẩm', render: (_, i) => `${i.name}${i.variant ? ` (${i.variant})` : ''}` },
          { title: 'SL', dataIndex: 'quantity' },
          { title: 'Hoàn', dataIndex: 'refundAmount', render: (v: number) => formatPrice(v) },
        ]}
      />
      <Typography.Title level={5} style={{ marginTop: 16 }}>Bằng chứng</Typography.Title>
      <Image.PreviewGroup>
        <Space wrap>
          {item.evidence.map((e, i) => e.type === 'Image'
            ? <Image key={i} src={e.url} width={80} height={80} style={{ objectFit: 'cover' }} title={e.party} />
            : <video key={i} src={e.url} width={160} controls preload="metadata" />)}
        </Space>
      </Image.PreviewGroup>

      {(responding || checking) && (
        <Space direction="vertical" style={{ width: '100%', marginTop: 16 }} data-testid="return-actions">
          <Input.TextArea rows={2} placeholder="Ghi chú cho người mua (bắt buộc khi từ chối)" value={note} onChange={(e) => setNote(e.target.value)} data-testid="return-note" />
          <input type="file" accept="image/*,video/mp4" onChange={async (e) => {
            const file = e.target.files?.[0]
            if (!file) return
            try {
              const asset = await uploadMedia('evidence', file)
              setEvidence([...evidence, asset.id])
            } catch (err) {
              void message.error(err instanceof ApiError ? err.message : 'Tải tệp thất bại.')
            }
          }} />
          {responding && (
            <>
              <Space wrap>
                <Button type="primary" loading={act.isPending} onClick={() => act.mutate('Approve')} data-testid="return-approve">Chấp nhận</Button>
                <Button danger loading={act.isPending} onClick={() => act.mutate('Reject')} data-testid="return-reject">Từ chối</Button>
              </Space>
              <Space.Compact>
                <InputNumber min={1000} max={item.requestedAmount - 1} step={1000} placeholder="Số tiền đề nghị" value={amount}
                  onChange={(v) => setAmount(v)} style={{ width: 200 }} data-testid="offer-amount" />
                <Button loading={act.isPending} disabled={!amount} onClick={() => act.mutate('OfferPartial')} data-testid="return-offer">Đề nghị hoàn một phần</Button>
              </Space.Compact>
            </>
          )}
          {checking && (
            <Space>
              <Checkbox checked={restock} onChange={(e) => setRestock(e.target.checked)}>Nhập lại kho</Checkbox>
              <Button type="primary" loading={act.isPending} onClick={() => act.mutate('ConfirmReceived')} data-testid="return-confirm-received">Đã nhận hàng, hoàn tiền</Button>
            </Space>
          )}
        </Space>
      )}

      <Timeline style={{ marginTop: 24 }} items={item.history.map((h) => ({
        children: <><strong>{h.label}</strong> · {formatDateTime(h.occurredAt)}{h.note && <div>{h.note}</div>}</>,
      }))} />
    </Drawer>
  )
}

/** Trả hàng / Hoàn tiền: respond within RETURN.SHOP_RESPONSE_DAYS or the request is approved automatically. */
const ReturnsPage = ({ shopId }: { shopId: string }) => {
  const [tab, setTab] = useState<ReturnStatus | 'All'>('All')
  const [page, setPage] = useState(1)
  const [openId, setOpenId] = useState<string | null>(null)
  const list = useQuery({
    queryKey: ['returns', shopId, tab, page],
    queryFn: () => aftercareApi.returns(shopId, tab === 'All' ? undefined : tab, page),
    placeholderData: keepPreviousData,
  })
  const open = list.data?.items.find((r) => r.id === openId)

  return (
    <Card title="Trả hàng / Hoàn tiền">
      <Tabs activeKey={tab} onChange={(k) => { setTab(k as ReturnStatus | 'All'); setPage(1) }} items={TABS.map((t) => ({ key: t.key, label: t.label }))} />
      <Table<ReturnInfo>
        rowKey="id"
        loading={list.isLoading}
        dataSource={list.data?.items ?? []}
        onRow={(r) => ({ onClick: () => setOpenId(r.id), style: { cursor: 'pointer' }, 'data-testid': 'return-row' } as React.HTMLAttributes<HTMLElement>)}
        pagination={list.data && list.data.totalCount > list.data.pageSize
          ? { current: page, pageSize: list.data.pageSize, total: list.data.totalCount, onChange: setPage } : false}
        columns={[
          { title: 'Mã', dataIndex: 'code' },
          { title: 'Đơn', dataIndex: 'orderCode' },
          { title: 'Sản phẩm', render: (_, r) => r.items.map((i) => `${i.name} ×${i.quantity}`).join(', ') },
          { title: 'Lý do', dataIndex: 'reason', render: (v: string) => RETURN_REASON_LABELS[v] ?? v },
          { title: 'Yêu cầu hoàn', dataIndex: 'requestedAmount', render: (v: number) => formatPrice(v) },
          { title: 'Trạng thái', render: (_, r) => <Tag color={statusColor[r.status]}>{r.statusLabel}</Tag> },
          { title: 'Hạn', dataIndex: 'respondBy', render: (v: string | null) => (v ? formatDateTime(v) : '') },
        ]}
      />
      {open && <ReturnDrawer shopId={shopId} item={open} onClose={() => setOpenId(null)} />}
    </Card>
  )
}

export default ReturnsPage
