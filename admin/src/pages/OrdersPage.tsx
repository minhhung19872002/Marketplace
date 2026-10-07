import { useState } from 'react'
import { App, Button, Card, Checkbox, Descriptions, Drawer, Input, InputNumber, Modal, Select, Space, Table, Tag, Timeline, Typography } from 'antd'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { platformApi, type AdminOrderRow } from '../api/platform'
import { P, can } from '../permissions'
import { formatDateTime } from '../lib/datetime'
import { formatPrice } from '../lib/money'

const STATUS: Record<string, string> = {
  PendingPayment: 'Chờ thanh toán', PendingConfirmation: 'Chờ xác nhận', ReadyToShip: 'Chờ lấy hàng', Shipping: 'Đang giao', Delivered: 'Đã giao',
  Completed: 'Hoàn thành', Cancelled: 'Đã huỷ', DeliveryFailed: 'Giao thất bại', Returning: 'Đang hoàn về', Returned: 'Đã hoàn về',
}

const ACTOR: Record<string, string> = { Buyer: 'Người mua', Seller: 'Shop', Admin: 'Sàn', System: 'Hệ thống', Carrier: 'Vận chuyển', Gateway: 'Cổng thanh toán' }

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.message : fallback)

/** VI.5: tra mọi đơn, full history; cancelling or fixing a refund needs its own permission and a reason. */
const OrdersPage = ({ permissions }: { permissions: string[] }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [q, setQ] = useState('')
  const [status, setStatus] = useState<string | undefined>()
  const [page, setPage] = useState(1)
  const [code, setCode] = useState<string | null>(null)
  const [cancelReason, setCancelReason] = useState<string | null>(null)
  const [refund, setRefund] = useState<{ id: string; toWallet: boolean; reason: string } | null>(null)
  // Manual refund (VI.5): units per line, amount, who bears it, reason
  const [manual, setManual] = useState<{ units: Record<string, number>; amount: number; platformBorne: boolean; reason: string } | null>(null)
  const intervene = can(permissions, P.OrderIntervene)

  const list = useQuery({ queryKey: ['admin-orders', q, status, page], queryFn: () => platformApi.orders({ q, status, page }), placeholderData: keepPreviousData })
  const detail = useQuery({ queryKey: ['admin-order', code], queryFn: () => platformApi.order(code!), enabled: !!code })
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-orders'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-order', code] })
  }
  const cancel = useMutation({
    mutationFn: () => platformApi.cancelOrder(code!, cancelReason ?? ''),
    onSuccess: (r) => { message.success(r.message); setCancelReason(null); refresh() },
    onError: (e) => message.error(errorText(e, 'Không huỷ được đơn.')),
  })
  const resolve = useMutation({
    mutationFn: () => platformApi.resolveRefund(refund!.id, refund!.toWallet, refund!.reason),
    onSuccess: (r) => { message.success(r.message); setRefund(null); refresh() },
    onError: (e) => message.error(errorText(e, 'Không xử lý được hoàn tiền.')),
  })
  const manualRefund = useMutation({
    mutationFn: () => platformApi.manualRefund(code!, {
      lines: Object.entries(manual!.units).filter(([, n]) => n > 0).map(([orderItemId, quantity]) => ({ orderItemId, quantity })),
      amount: manual!.amount, platformBorne: manual!.platformBorne, reason: manual!.reason,
    }),
    onSuccess: (r) => { message.success(r.message); setManual(null); refresh() },
    onError: (e) => message.error(errorText(e, 'Không hoàn tiền được.')),
  })
  const d = detail.data
  // About the most that can be refunded for the chosen units (rounded up; the server checks it to the đồng)
  const manualCeiling = d && manual
    ? d.lines.reduce((sum, l) => sum + Math.ceil((l.paid * Math.min(manual.units[l.id] ?? 0, l.refundable)) / Math.max(l.quantity, 1)), 0)
    : 0

  return (
    <Card title="Đơn hàng toàn sàn">
      <Space wrap style={{ marginBottom: 12 }}>
        <Input.Search allowClear placeholder="Mã đơn, mã vận đơn, SĐT / tên người mua, tên shop" style={{ width: 380 }}
          onSearch={(v) => { setQ(v); setPage(1) }} data-testid="order-search" />
        <Select allowClear placeholder="Trạng thái" style={{ width: 180 }} value={status} onChange={(v) => { setStatus(v); setPage(1) }}
          options={Object.entries(STATUS).map(([value, label]) => ({ value, label }))} />
      </Space>
      <Table<AdminOrderRow> rowKey="id" loading={list.isLoading} dataSource={list.data?.items ?? []}
        pagination={{ current: page, pageSize: 20, total: list.data?.totalCount ?? 0, onChange: setPage }}
        onRow={(r) => ({ onClick: () => setCode(r.code), style: { cursor: 'pointer' } })}
        columns={[
          { title: 'Mã đơn', dataIndex: 'code' },
          { title: 'Shop', dataIndex: 'shopName' },
          { title: 'Người mua', dataIndex: 'buyerName' },
          { title: 'Trạng thái', dataIndex: 'status', render: (s: string) => <Tag>{STATUS[s] ?? s}</Tag> },
          { title: 'Thanh toán', render: (_, r) => `${r.paymentMethod} · ${r.paymentStatus}` },
          { title: 'Tổng', dataIndex: 'grandTotal', align: 'right', render: (v: number) => formatPrice(v) },
          { title: 'Đặt lúc', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
        ]} />

      <Drawer open={!!code} onClose={() => setCode(null)} width={760} title={`Đơn ${code ?? ''}`} destroyOnClose
        extra={intervene && d && (
          <Space>
            {['PendingConfirmation', 'ReadyToShip'].includes(d.order.status) && (
              <Button danger onClick={() => setCancelReason('')} data-testid="admin-cancel-order">Huỷ đơn (can thiệp)</Button>
            )}
            {['Delivered', 'Completed'].includes(d.order.status) && d.lines.some((l) => l.refundable > 0) && (
              <Button onClick={() => setManual({ units: {}, amount: 0, platformBorne: false, reason: '' })} data-testid="admin-manual-refund">
                Hoàn tiền thủ công
              </Button>
            )}
          </Space>
        )}>
        {d && (
          <Space direction="vertical" style={{ width: '100%' }} size="large">
            <Descriptions column={2} size="small" bordered>
              <Descriptions.Item label="Shop">{d.order.shopName}</Descriptions.Item>
              <Descriptions.Item label="Người mua">{d.order.buyerName}</Descriptions.Item>
              <Descriptions.Item label="Trạng thái">{STATUS[d.order.status] ?? d.order.status}</Descriptions.Item>
              <Descriptions.Item label="Thanh toán">{d.order.paymentMethod} · {d.order.paymentStatus}</Descriptions.Item>
              <Descriptions.Item label="Tiền hàng">{formatPrice(d.subtotal)}</Descriptions.Item>
              <Descriptions.Item label="Giảm giá">−{formatPrice(d.shopDiscount + d.platformDiscount + d.shippingDiscount + d.coinUsed)}</Descriptions.Item>
              <Descriptions.Item label="Phí vận chuyển">{formatPrice(d.shippingFee)}</Descriptions.Item>
              <Descriptions.Item label="Tổng thanh toán"><b>{formatPrice(d.order.grandTotal)}</b></Descriptions.Item>
              {d.cancelReason && <Descriptions.Item label="Lý do huỷ" span={2}>{d.cancelReason}</Descriptions.Item>}
            </Descriptions>
            <Table size="small" rowKey={(_, i) => String(i)} pagination={false} dataSource={d.lines}
              columns={[
                { title: 'Sản phẩm', render: (_, l) => `${l.name}${l.variant ? ` (${l.variant})` : ''}` },
                { title: 'Đơn giá', dataIndex: 'unitPrice', align: 'right', render: (v: number) => formatPrice(v) },
                { title: 'SL', dataIndex: 'quantity', align: 'right' },
                { title: 'Thành tiền', dataIndex: 'lineTotal', align: 'right', render: (v: number) => formatPrice(v) },
              ]} />
            <div>
              <Typography.Title level={5}>Lịch sử trạng thái</Typography.Title>
              <Timeline items={d.history.map((h) => ({
                children: <span>{formatDateTime(h.at)} — {h.from ? `${STATUS[h.from] ?? h.from} → ` : ''}{STATUS[h.to] ?? h.to} · {ACTOR[h.actor] ?? h.actor}
                  {h.actorName ? ` (${h.actorName})` : ''}{h.reason ? `: ${h.reason}` : ''}</span>,
              }))} />
            </div>
            <div>
              <Typography.Title level={5}>Thanh toán & hoàn tiền</Typography.Title>
              <Table size="small" rowKey="id" pagination={false} dataSource={d.payments}
                columns={[
                  { title: 'Cổng', dataIndex: 'method' },
                  { title: 'Trạng thái', dataIndex: 'status' },
                  { title: 'Số tiền', dataIndex: 'amount', align: 'right', render: (v: number) => formatPrice(v) },
                  { title: 'Mã giao dịch', dataIndex: 'providerTxnId' },
                  { title: 'Lúc', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
                ]} />
              <Table size="small" rowKey="id" pagination={false} dataSource={d.refunds} style={{ marginTop: 8 }} data-testid="admin-refunds"
                columns={[
                  { title: 'Hoàn về', dataIndex: 'destination', render: (v: string) => (v === 'Wallet' ? 'Ví ShopHub' : 'Cổng thanh toán') },
                  { title: 'Trạng thái', dataIndex: 'status', render: (v: string) => <Tag color={v === 'Failed' ? 'red' : v === 'Succeeded' ? 'green' : 'gold'}>{v}</Tag> },
                  { title: 'Số tiền', dataIndex: 'amount', align: 'right', render: (v: number) => formatPrice(v) },
                  { title: 'Lý do', dataIndex: 'reason' },
                  {
                    title: '', key: 'x',
                    render: (_, r) => intervene && r.status === 'Failed' && (
                      <Space>
                        <Button size="small" onClick={() => setRefund({ id: r.id, toWallet: false, reason: '' })}>Thử lại cổng</Button>
                        <Button size="small" type="primary" onClick={() => setRefund({ id: r.id, toWallet: true, reason: '' })}>Hoàn về ví</Button>
                      </Space>
                    ),
                  },
                ]} />
            </div>
            {d.shipments.map((s) => (
              <div key={s.trackingNo}>
                <Typography.Title level={5}>Vận đơn {s.trackingNo} ({s.carrierCode}, {s.direction === 'Return' ? 'chiều về' : 'chiều đi'}) — {s.status}</Typography.Title>
                <Timeline items={s.events.map((e) => ({ children: `${formatDateTime(e.at)} — ${e.description}${e.location ? ` (${e.location})` : ''}` }))} />
              </div>
            ))}
            {d.returns.length > 0 && <Typography.Text>Yêu cầu trả hàng: {d.returns.join(', ')}</Typography.Text>}
          </Space>
        )}
      </Drawer>

      <Modal open={cancelReason !== null} title={`Huỷ đơn ${code}`} okText="Huỷ đơn" okButtonProps={{ danger: true, disabled: (cancelReason ?? '').trim().length < 10 }}
        confirmLoading={cancel.isPending} onCancel={() => setCancelReason(null)} onOk={() => cancel.mutate()}>
        <Typography.Paragraph type="secondary">Kho được nhả và tiền đã trả được hoàn về nguồn. Thao tác được ghi vào lịch sử đơn với tên bạn.</Typography.Paragraph>
        <Input.TextArea rows={3} value={cancelReason ?? ''} onChange={(e) => setCancelReason(e.target.value)} placeholder="Lý do can thiệp (ít nhất 10 ký tự)"
          data-testid="admin-cancel-reason" />
      </Modal>
      <Modal open={!!manual} title={`Hoàn tiền thủ công — đơn ${code}`} okText="Hoàn tiền" width={640}
        okButtonProps={{ disabled: !manual || manual.amount <= 0 || manual.amount > manualCeiling || manual.reason.trim().length < 10 }}
        confirmLoading={manualRefund.isPending} onCancel={() => setManual(null)} onOk={() => manualRefund.mutate()}>
        {manual && d && (
          <Space direction="vertical" style={{ width: '100%' }}>
            <Table size="small" rowKey="id" pagination={false} dataSource={d.lines.filter((l) => l.refundable > 0)}
              columns={[
                { title: 'Sản phẩm', render: (_, l) => `${l.name}${l.variant ? ` (${l.variant})` : ''}` },
                { title: 'Đã trả', dataIndex: 'paid', align: 'right', render: (v: number) => formatPrice(v) },
                {
                  title: 'Số lượng hoàn', render: (_, l) => (
                    <InputNumber min={0} max={l.refundable} value={manual.units[l.id] ?? 0} data-testid={`manual-units-${l.id}`}
                      onChange={(v) => setManual({ ...manual, units: { ...manual.units, [l.id]: Number(v ?? 0) } })} />
                  ),
                },
              ]} />
            <Space>
              Số tiền hoàn
              <InputNumber min={0} max={manualCeiling} value={manual.amount} style={{ width: 180 }} data-testid="manual-amount"
                onChange={(v) => setManual({ ...manual, amount: Number(v ?? 0) })} />
              <Typography.Text type="secondary">tối đa {formatPrice(manualCeiling)}</Typography.Text>
            </Space>
            <Checkbox checked={manual.platformBorne} onChange={(e) => setManual({ ...manual, platformBorne: e.target.checked })} data-testid="manual-platform">
              Sàn chịu khoản hoàn này (không trừ vào doanh thu của shop)
            </Checkbox>
            <Input.TextArea rows={3} value={manual.reason} onChange={(e) => setManual({ ...manual, reason: e.target.value })}
              placeholder="Lý do (ít nhất 10 ký tự) — ghi vào nhật ký và lịch sử yêu cầu" data-testid="manual-reason" />
          </Space>
        )}
      </Modal>
      <Modal open={!!refund} title={refund?.toWallet ? 'Hoàn về Ví ShopHub' : 'Thử hoàn lại qua cổng'} okText="Xác nhận"
        okButtonProps={{ disabled: !refund?.reason.trim() }} confirmLoading={resolve.isPending} onCancel={() => setRefund(null)} onOk={() => resolve.mutate()}>
        <Input.TextArea rows={3} value={refund?.reason ?? ''} onChange={(e) => setRefund((r) => (r ? { ...r, reason: e.target.value } : r))} placeholder="Lý do" />
      </Modal>
    </Card>
  )
}

export default OrdersPage
