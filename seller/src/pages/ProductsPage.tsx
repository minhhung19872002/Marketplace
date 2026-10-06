import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { App as AntApp, Button, Drawer, Image, Input, InputNumber, Popconfirm, Space, Table, Tabs, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { sellerApi, type ProductRow, type ProductStatus, type ProductTab, type Sku } from '../api/seller'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { formatPrice, formatRange } from '../lib/money'

const TABS: { key: ProductTab; label: string }[] = [
  { key: 'All', label: 'Tất cả' },
  { key: 'Active', label: 'Đang hoạt động' },
  { key: 'SoldOut', label: 'Hết hàng' },
  { key: 'LowStock', label: 'Sắp hết hàng' },
  { key: 'Pending', label: 'Chờ duyệt' },
  { key: 'Violation', label: 'Vi phạm' },
  { key: 'Hidden', label: 'Đã ẩn' },
  { key: 'Draft', label: 'Nháp' },
]

const STATUS: Record<ProductStatus, { text: string; color: string }> = {
  Draft: { text: 'Nháp', color: 'default' },
  PendingReview: { text: 'Chờ duyệt', color: 'gold' },
  Active: { text: 'Đang bán', color: 'green' },
  Hidden: { text: 'Đã ẩn', color: 'default' },
  Banned: { text: 'Bị khoá', color: 'red' },
  Deleted: { text: 'Đã xoá', color: 'default' },
}

const REASON: Record<string, string> = {
  SellerEdit: 'Sửa sản phẩm',
  SellerAdjust: 'Điều chỉnh',
  Import: 'Nhập Excel',
  OrderReserve: 'Giữ cho đơn',
  OrderRelease: 'Nhả giữ hàng',
  OrderShip: 'Giao cho vận chuyển',
  ReturnRestock: 'Hoàn kho',
  Seed: 'Khởi tạo',
}

const errorText = (err: unknown) => (err instanceof ApiError ? err.message : 'Thao tác thất bại.')

/** Inline SKU editor (price / stock) shown when a product row is expanded. */
const SkuEditor = ({ shopId, productId }: { shopId: string; productId: string }) => {
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const detail = useQuery({ queryKey: ['product', shopId, productId], queryFn: () => sellerApi.product(shopId, productId) })
  const [stockSku, setStockSku] = useState<Sku | null>(null)
  const [delta, setDelta] = useState<number | null>(null)
  const [note, setNote] = useState('')

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['product', shopId, productId] })
    void queryClient.invalidateQueries({ queryKey: ['products', shopId] })
  }
  const quick = useMutation({
    mutationFn: (v: { skuId: string; price?: number; stock?: number }) => sellerApi.quickSku(shopId, v.skuId, { price: v.price, stock: v.stock }),
    onSuccess: (r) => { void message.success(r.message); refresh() },
    onError: (e) => { void message.error(errorText(e)); refresh() },
  })
  const adjust = useMutation({
    mutationFn: () => sellerApi.adjustStock(shopId, stockSku!.id, delta ?? 0, note),
    onSuccess: (r) => { void message.success(r.message); setDelta(null); setNote(''); refresh(); void movements.refetch() },
    onError: (e) => void message.error(errorText(e)),
  })
  const movements = useQuery({
    queryKey: ['movements', stockSku?.id],
    queryFn: () => sellerApi.movements(shopId, stockSku!.id),
    enabled: !!stockSku,
  })

  const label = (s: Sku) => [s.option1, s.option2].filter(Boolean).join(' / ') || 'Mặc định'

  return (
    <>
      <Table<Sku>
        size="small"
        rowKey="id"
        loading={detail.isPending}
        dataSource={detail.data?.skus.filter((s) => s.isActive)}
        pagination={false}
        columns={[
          { title: 'Phân loại', render: (_, s) => label(s) },
          { title: 'Mã SKU', dataIndex: 'sellerSku', render: (v: string | null) => v ?? '—' },
          {
            title: 'Giá',
            render: (_, s) => (
              <InputNumber<number> min={1000} step={1000} defaultValue={s.price} style={{ width: 140 }} aria-label={`Giá ${label(s)}`}
                formatter={(v) => (v ? formatPrice(Number(v)) : '')}
                parser={(v) => Number((v ?? '').replace(/\D/g, ''))}
                onBlur={(e) => {
                  const v = Number(e.target.value.replace(/\D/g, ''))
                  if (v && v !== s.price) quick.mutate({ skuId: s.id, price: v })
                }} />
            ),
          },
          {
            title: 'Tồn kho',
            render: (_, s) => (
              <InputNumber<number> min={s.reserved} defaultValue={s.stock} style={{ width: 100 }} aria-label={`Tồn kho ${label(s)}`}
                onBlur={(e) => {
                  const v = Number(e.target.value)
                  if (!Number.isNaN(v) && v !== s.stock) quick.mutate({ skuId: s.id, stock: v })
                }} />
            ),
          },
          { title: 'Đang giữ', dataIndex: 'reserved' },
          { title: 'Khả dụng', dataIndex: 'available' },
          { title: '', render: (_, s) => <Button size="small" onClick={() => setStockSku(s)}>Nhập / xuất kho</Button> },
        ]}
      />
      <Drawer title={stockSku ? `Tồn kho: ${label(stockSku)}` : ''} open={!!stockSku} onClose={() => setStockSku(null)} width={520}>
        <Space direction="vertical" style={{ width: '100%' }}>
          <Typography.Text type="secondary">Số dương: nhập thêm hàng. Số âm: hàng hỏng/mất. Không thể giảm dưới số đang giữ cho đơn.</Typography.Text>
          <Space.Compact block>
            <InputNumber<number> placeholder="+ / − số lượng" value={delta} onChange={setDelta} style={{ width: 160 }} />
            <Input placeholder="Ghi chú" value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} />
            <Button type="primary" disabled={!delta} loading={adjust.isPending} onClick={() => adjust.mutate()}>Ghi</Button>
          </Space.Compact>
          <Table
            size="small"
            rowKey="id"
            loading={movements.isPending}
            dataSource={movements.data?.items}
            pagination={false}
            columns={[
              { title: 'Thời điểm', dataIndex: 'occurredAt', render: (v: string) => formatDateTime(v) },
              { title: 'Lý do', dataIndex: 'reason', render: (v: string) => REASON[v] ?? v },
              { title: 'Tồn', dataIndex: 'deltaStock', render: (v: number) => (v > 0 ? `+${v}` : v) },
              { title: 'Giữ', dataIndex: 'deltaReserved', render: (v: number) => (v > 0 ? `+${v}` : v) },
              { title: 'Ghi chú', dataIndex: 'note' },
            ]}
          />
        </Space>
      </Drawer>
    </>
  )
}

const ProductsPage = ({ shopId }: { shopId: string }) => {
  const navigate = useNavigate()
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<ProductTab>('All')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)

  const products = useQuery({
    queryKey: ['products', shopId, tab, q, page, pageSize],
    queryFn: () => sellerApi.products(shopId, { tab, q, page, pageSize }),
  })
  const action = useMutation({
    mutationFn: (v: { id: string; op: 'submit' | 'hide' | 'show' | 'delete' }) => sellerApi.productAction(shopId, v.id, v.op),
    onSuccess: (r) => { void message.success(r.message); void queryClient.invalidateQueries({ queryKey: ['products', shopId] }) },
    onError: (e) => void message.error(errorText(e)),
  })

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Space style={{ justifyContent: 'space-between', width: '100%' }}>
        <Typography.Title level={3} style={{ margin: 0 }}>Sản phẩm</Typography.Title>
        <Button type="primary" onClick={() => navigate('/san-pham/moi')} data-testid="add-product">+ Thêm sản phẩm</Button>
      </Space>
      <Tabs activeKey={tab} onChange={(k) => { setTab(k as ProductTab); setPage(1) }} items={TABS.map((t) => ({ key: t.key, label: t.label }))} />
      <Input.Search placeholder="Tên sản phẩm hoặc mã SKU" allowClear style={{ maxWidth: 360 }} onSearch={(v) => { setQ(v); setPage(1) }} />
      <Table<ProductRow>
        rowKey="id"
        loading={products.isPending}
        dataSource={products.data?.items}
        locale={{ emptyText: products.isError ? 'Không tải được danh sách.' : 'Chưa có sản phẩm.' }}
        expandable={{ expandedRowRender: (r) => <SkuEditor shopId={shopId} productId={r.id} /> }}
        pagination={{
          current: page,
          pageSize,
          total: products.data?.totalCount,
          showSizeChanger: true,
          showTotal: (t) => `${t} sản phẩm`,
          onChange: (p, s) => { setPage(p); setPageSize(s) },
        }}
        columns={[
          {
            title: 'Sản phẩm',
            render: (_, r) => (
              <Space>
                {r.imageUrl && <Image src={r.imageUrl} width={56} height={56} style={{ objectFit: 'cover' }} preview={false} />}
                <div>
                  <div data-testid="product-name">{r.name}</div>
                  {r.reviewNote && <Typography.Text type="warning">Cần sửa: {r.reviewNote}</Typography.Text>}
                  {r.banReason && <Typography.Text type="danger">Vi phạm: {r.banReason}</Typography.Text>}
                </div>
              </Space>
            ),
          },
          { title: 'Giá', render: (_, r) => formatRange(r.minPrice, r.maxPrice) },
          { title: 'Kho', render: (_, r) => `${r.totalAvailable} / ${r.totalStock}` },
          { title: 'Đã bán', dataIndex: 'soldCount' },
          { title: 'Trạng thái', dataIndex: 'status', render: (s: ProductStatus) => <Tag color={STATUS[s].color}>{STATUS[s].text}</Tag> },
          { title: 'Cập nhật', render: (_, r) => formatDateTime(r.updatedAt ?? r.createdAt) },
          {
            title: '',
            render: (_, r) => (
              <Space wrap>
                {r.status !== 'Banned' && <Button size="small" onClick={() => navigate(`/san-pham/${r.id}`)}>Sửa</Button>}
                {r.status === 'Draft' && <Button size="small" type="primary" onClick={() => action.mutate({ id: r.id, op: 'submit' })}>Gửi duyệt</Button>}
                {r.status === 'Active' && <Button size="small" onClick={() => action.mutate({ id: r.id, op: 'hide' })}>Ẩn</Button>}
                {r.status === 'Hidden' && <Button size="small" onClick={() => action.mutate({ id: r.id, op: 'show' })}>Hiện</Button>}
                {r.status !== 'Banned' && (
                  <Popconfirm title="Xoá sản phẩm này?" okText="Xoá" cancelText="Huỷ" onConfirm={() => action.mutate({ id: r.id, op: 'delete' })}>
                    <Button size="small" danger>Xoá</Button>
                  </Popconfirm>
                )}
              </Space>
            ),
          },
        ]}
      />
    </Space>
  )
}

export default ProductsPage
