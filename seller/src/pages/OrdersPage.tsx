import { useState } from 'react'
import { App, Button, Card, Descriptions, Drawer, Image, Input, Modal, Radio, Select, Space, Table, Tabs, Tag, Timeline, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { ordersApi, type PrepareResult, type ShopOrderRow, type ShopOrderTab } from '../api/orders'
import { ApiError } from '../api/http'
import { formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'

const TABS: { key: ShopOrderTab; label: string }[] = [
  { key: 'All', label: 'Tất cả' },
  { key: 'ToConfirm', label: 'Chờ xác nhận' },
  { key: 'ToShip', label: 'Chờ lấy hàng' },
  { key: 'Shipping', label: 'Đang giao' },
  { key: 'Delivered', label: 'Đã giao' },
  { key: 'CancelRequests', label: 'Yêu cầu huỷ' },
  { key: 'Cancelled', label: 'Đã huỷ' },
  { key: 'Failed', label: 'Giao thất bại / hoàn' },
  { key: 'Unpaid', label: 'Chờ thanh toán' },
]

const statusColor: Record<string, string> = {
  PendingConfirmation: 'gold', ReadyToShip: 'blue', Shipping: 'cyan', Delivered: 'green', Completed: 'green', Cancelled: 'default',
  DeliveryFailed: 'red', Returning: 'orange', Returned: 'orange', PendingPayment: 'purple',
}

/** Opens a downloaded PDF in a new tab (the user prints from the browser). */
const openBlob = (blob: Blob, name: string) => {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.target = '_blank'
  a.download = name
  a.click()
  setTimeout(() => URL.revokeObjectURL(url), 60_000)
}

const OrderDrawer = ({ shopId, orderId, onClose }: { shopId: string; orderId: string; onClose: () => void }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [note, setNote] = useState<string | null>(null)
  const [rejecting, setRejecting] = useState(false)
  const [rejectReason, setRejectReason] = useState('')
  const { data } = useQuery({ queryKey: ['shop-order', shopId, orderId], queryFn: () => ordersApi.get(shopId, orderId) })
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['shop-order', shopId, orderId] })
    void queryClient.invalidateQueries({ queryKey: ['shop-orders', shopId] })
  }
  const fail = (e: unknown) => message.error(e instanceof ApiError ? e.message : 'Thao tác thất bại.')
  const decide = useMutation({
    mutationFn: (approve: boolean) => ordersApi.decide(shopId, orderId, approve, approve ? null : rejectReason),
    onSuccess: (r) => { message.success(r.message); setRejecting(false); refresh() },
    onError: fail,
  })
  const saveNote = useMutation({
    mutationFn: () => ordersApi.note(shopId, orderId, note ?? ''),
    onSuccess: (r) => { message.success(r.message); setNote(null); refresh() },
    onError: fail,
  })

  const o = data?.order
  return (
    <Drawer open width={640} onClose={onClose} title={o ? `Đơn ${o.code}` : 'Đơn hàng'} destroyOnClose>
      {o && data && (
        <Space direction="vertical" style={{ width: '100%' }} size="middle">
          <Space>
            <Tag color={statusColor[o.status]} data-testid="drawer-status">{o.statusLabel}</Tag>
            {data.shipDeadline && <Typography.Text type="warning">Hạn chuẩn bị hàng: {data.shipDeadline.split('-').reverse().join('/')}</Typography.Text>}
          </Space>
          {data.order.cancelRequest?.status === 'Pending' && (
            <Card size="small" title="Người mua yêu cầu huỷ" data-testid="cancel-request">
              <p>Lý do: {data.order.cancelRequest.reason}</p>
              <p>Hạn phản hồi: {formatDateTime(data.order.cancelRequest.dueAt)} (quá hạn sẽ tự chấp thuận)</p>
              {rejecting ? (
                <Space.Compact style={{ width: '100%' }}>
                  <Input placeholder="Lý do từ chối" value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} data-testid="reject-reason" />
                  <Button danger disabled={!rejectReason.trim()} loading={decide.isPending} onClick={() => decide.mutate(false)} data-testid="reject-confirm">Từ chối</Button>
                </Space.Compact>
              ) : (
                <Space>
                  <Button type="primary" loading={decide.isPending} onClick={() => decide.mutate(true)} data-testid="approve-cancel">Đồng ý huỷ</Button>
                  <Button onClick={() => setRejecting(true)} data-testid="reject-cancel">Từ chối</Button>
                </Space>
              )}
            </Card>
          )}
          <Descriptions column={1} size="small" bordered>
            <Descriptions.Item label="Người mua">{data.buyerName}</Descriptions.Item>
            <Descriptions.Item label="Người nhận">{o.address.receiverName} · {o.address.phone}</Descriptions.Item>
            <Descriptions.Item label="Địa chỉ">{o.address.fullAddress}</Descriptions.Item>
            <Descriptions.Item label="Thanh toán">{o.paymentMethod === 'Cod' ? 'COD (thu hộ)' : 'Online'} · {o.paymentStatus}</Descriptions.Item>
            {o.buyerNote && <Descriptions.Item label="Lời nhắn">{o.buyerNote}</Descriptions.Item>}
            {o.shipment && <Descriptions.Item label="Vận đơn">{o.shipment.trackingNo} · {o.shipment.carrierName} · {o.shipment.statusLabel}</Descriptions.Item>}
          </Descriptions>
          <Table size="small" rowKey="id" pagination={false} dataSource={o.items} columns={[
            { title: 'Sản phẩm', render: (_, i) => <span>{i.name}{i.variant && <Typography.Text type="secondary"> · {i.variant}</Typography.Text>}</span> },
            { title: 'SL', dataIndex: 'quantity' },
            { title: 'Thành tiền', render: (_, i) => formatPrice(i.lineTotal) },
          ]} />
          <Descriptions column={1} size="small">
            <Descriptions.Item label="Tiền hàng">{formatPrice(o.subtotal)}</Descriptions.Item>
            <Descriptions.Item label="Voucher shop (shop chịu)">−{formatPrice(o.shopDiscount)}</Descriptions.Item>
            <Descriptions.Item label="Voucher sàn (sàn chịu)">−{formatPrice(o.platformDiscount)}</Descriptions.Item>
            <Descriptions.Item label="Phí vận chuyển">{formatPrice(o.shippingFee)}</Descriptions.Item>
            <Descriptions.Item label="Người mua trả">{formatPrice(o.grandTotal)}</Descriptions.Item>
          </Descriptions>
          <Card size="small" title="Hành trình">
            <Timeline items={[...(o.shipment?.events ?? []).map((e) => ({ children: `${formatDateTime(e.occurredAt)} · ${e.label} — ${e.description}` })),
              ...o.history.map((h) => ({ color: 'gray', children: `${formatDateTime(h.occurredAt)} · ${h.toLabel}${h.reason ? ` — ${h.reason}` : ''}` }))]} />
          </Card>
          <Card size="small" title="Ghi chú nội bộ (người mua không thấy)">
            <Input.TextArea rows={2} value={note ?? data.sellerNote ?? ''} onChange={(e) => setNote(e.target.value)} maxLength={500} />
            {note !== null && <Button style={{ marginTop: 8 }} onClick={() => saveNote.mutate()} loading={saveNote.isPending}>Lưu ghi chú</Button>}
          </Card>
        </Space>
      )}
    </Drawer>
  )
}

const OrdersPage = ({ shopId }: { shopId: string }) => {
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const [params, setParams] = useSearchParams()
  const tab = (params.get('tab') as ShopOrderTab) || 'All'
  const [q, setQ] = useState(params.get('ma') ?? '')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<string[]>([])
  const [opened, setOpened] = useState<string | null>(null)
  const [preparing, setPreparing] = useState<string[] | null>(null)
  const [pickup, setPickup] = useState<'Pickup' | 'DropOff'>('Pickup')
  const [slot, setSlot] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState<ShopOrderRow | null>(null)
  const [cancelReason, setCancelReason] = useState('Hết hàng')

  const list = useQuery({ queryKey: ['shop-orders', shopId, tab, q, page], queryFn: () => ordersApi.list(shopId, { tab, q, page, pageSize: 20 }) })
  const slots = useQuery({ queryKey: ['pickup-slots'], queryFn: ordersApi.pickupSlots })
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['shop-orders', shopId] })
    void queryClient.invalidateQueries({ queryKey: ['dashboard', shopId] })
  }
  const fail = (e: unknown) => message.error(e instanceof ApiError ? e.message : 'Thao tác thất bại.')

  const prepare = useMutation({
    mutationFn: () => ordersApi.prepare(shopId, preparing!, pickup, pickup === 'Pickup' ? slot : null),
    onSuccess: (r) => {
      setPreparing(null)
      setSelected([])
      refresh()
      const failed = r.data.filter((x: PrepareResult) => !x.ok)
      if (failed.length === 0) message.success(r.message)
      else modal.warning({ title: r.message, content: failed.map((f) => `${f.code}: ${f.error}`).join('\n') })
    },
    onError: fail,
  })
  const cancel = useMutation({
    mutationFn: () => ordersApi.cancel(shopId, cancelling!.id, cancelReason),
    onSuccess: (r) => { message.success(r.message); setCancelling(null); refresh() },
    onError: fail,
  })
  const print = async (kind: 'labels' | 'picking', ids: string[]) => {
    try {
      const blob = kind === 'labels' ? await ordersApi.labels(shopId, ids) : await ordersApi.pickingList(shopId, ids)
      openBlob(blob, kind === 'labels' ? 'phieu-giao-hang.pdf' : 'phieu-soan-hang.pdf')
      if (kind === 'labels') refresh()
    } catch (e) {
      fail(e)
    }
  }
  const exportExcel = async () => {
    try {
      openBlob(await ordersApi.export(shopId, tab), 'don-hang.xlsx')
    } catch (e) {
      fail(e)
    }
  }

  const rows = list.data?.items ?? []
  const selectable = (r: ShopOrderRow) => r.status === 'PendingConfirmation' || r.status === 'ReadyToShip'
  return (
    <Card title="Quản lý đơn hàng" extra={<Button onClick={exportExcel} data-testid="export-orders">Xuất Excel</Button>}>
      <Tabs activeKey={tab} onChange={(k) => { setParams({ tab: k }); setPage(1); setSelected([]) }} items={TABS.map((t) => ({ key: t.key, label: t.label }))} />
      <Space style={{ marginBottom: 12 }} wrap>
        <Input.Search placeholder="Mã đơn, mã vận đơn, tên người mua, tên sản phẩm" allowClear style={{ width: 360 }} defaultValue={q}
          onSearch={(v) => { setQ(v); setPage(1) }} data-testid="order-search" />
        <Button type="primary" disabled={selected.length === 0} onClick={() => setPreparing(selected)} data-testid="bulk-prepare">
          Chuẩn bị hàng ({selected.length})
        </Button>
        <Button disabled={selected.length === 0} onClick={() => print('labels', selected)} data-testid="bulk-labels">In phiếu giao</Button>
        <Button disabled={selected.length === 0} onClick={() => print('picking', selected)} data-testid="bulk-picking">In phiếu soạn hàng</Button>
      </Space>
      <Table<ShopOrderRow>
        rowKey="id"
        loading={list.isLoading}
        dataSource={rows}
        rowSelection={{ selectedRowKeys: selected, onChange: (k) => setSelected(k as string[]), getCheckboxProps: (r) => ({ disabled: !selectable(r) }) }}
        pagination={{ current: page, pageSize: 20, total: list.data?.totalCount ?? 0, onChange: setPage, showSizeChanger: false }}
        columns={[
          {
            title: 'Đơn hàng',
            render: (_, r) => (
              <Space>
                {r.firstItemImage && <Image src={r.firstItemImage} width={44} height={44} preview={false} style={{ objectFit: 'cover' }} />}
                <span>
                  <Typography.Link onClick={() => setOpened(r.id)} data-testid="order-code">{r.code}</Typography.Link>
                  <br />
                  <Typography.Text type="secondary">{r.firstItemName}{r.itemCount > 1 ? ` · ${r.itemCount} sản phẩm` : ''}</Typography.Text>
                </span>
              </Space>
            ),
          },
          { title: 'Người mua', dataIndex: 'buyerName' },
          { title: 'Tổng', render: (_, r) => <span>{formatPrice(r.grandTotal)}<br /><Typography.Text type="secondary">{r.paymentMethod === 'Cod' ? 'COD' : 'Online'}</Typography.Text></span> },
          {
            title: 'Trạng thái',
            render: (_, r) => (
              <Space direction="vertical" size={0}>
                <Tag color={statusColor[r.status]} data-testid="order-row-status">{r.statusLabel}</Tag>
                {r.hasCancelRequest && <Tag color="red">Yêu cầu huỷ</Tag>}
              </Space>
            ),
          },
          { title: 'Vận chuyển', render: (_, r) => r.trackingNo ? <span>{r.carrierCode}<br />{r.trackingNo}{r.labelPrinted ? ' · đã in' : ''}</span> : r.carrierCode },
          { title: 'Ngày đặt', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
          {
            title: '',
            render: (_, r) => (
              <Space>
                {r.status === 'PendingConfirmation' && (
                  <Button size="small" type="primary" onClick={() => setPreparing([r.id])} data-testid="prepare-order">Chuẩn bị hàng</Button>
                )}
                {r.status === 'ReadyToShip' && <Button size="small" onClick={() => print('labels', [r.id])} data-testid="print-label">In phiếu</Button>}
                {r.status === 'ReadyToShip' && r.carrierCode.startsWith('GHTK') && (
                  <Button size="small" onClick={async () => {
                    try {
                      openBlob(await ordersApi.carrierLabel(shopId, r.id), `phieu-ghtk-${r.code}.pdf`)
                    } catch (e) {
                      fail(e)
                    }
                  }}>Phiếu GHTK</Button>
                )}
                {selectable(r) && <Button size="small" danger onClick={() => setCancelling(r)}>Huỷ</Button>}
              </Space>
            ),
          },
        ]}
      />

      <Modal title={`Chuẩn bị hàng (${preparing?.length ?? 0} đơn)`} open={!!preparing} onCancel={() => setPreparing(null)} okText="Xác nhận"
        onOk={() => prepare.mutate()} confirmLoading={prepare.isPending} okButtonProps={{ disabled: pickup === 'Pickup' && !slot, 'data-testid': 'prepare-confirm' } as never}>
        <Radio.Group value={pickup} onChange={(e) => setPickup(e.target.value)} options={[
          { value: 'Pickup', label: 'Đơn vị vận chuyển đến lấy hàng' },
          { value: 'DropOff', label: 'Tự mang ra bưu cục' },
        ]} />
        {pickup === 'Pickup' && (
          <Select style={{ width: '100%', marginTop: 12 }} placeholder="Chọn khung giờ lấy hàng" value={slot ?? undefined} onChange={setSlot}
            options={(slots.data ?? []).map((s) => ({ value: s, label: s }))} data-testid="pickup-slot" />
        )}
      </Modal>

      <Modal title={`Huỷ đơn ${cancelling?.code ?? ''}`} open={!!cancelling} onCancel={() => setCancelling(null)} okText="Huỷ đơn"
        okButtonProps={{ danger: true }} onOk={() => cancel.mutate()} confirmLoading={cancel.isPending}>
        <Select style={{ width: '100%' }} value={cancelReason} onChange={setCancelReason}
          options={['Hết hàng', 'Không liên lạc được người mua', 'Người mua yêu cầu huỷ', 'Sai giá sản phẩm'].map((v) => ({ value: v, label: v }))} />
        <Typography.Paragraph type="secondary" style={{ marginTop: 8 }}>
          Hàng giữ cho đơn được trả về kho; đơn đã thanh toán online được hoàn tiền tự động.
        </Typography.Paragraph>
      </Modal>

      {opened && <OrderDrawer shopId={shopId} orderId={opened} onClose={() => setOpened(null)} />}
    </Card>
  )
}

export default OrdersPage
