import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { App as AntApp, Button, Cascader, Drawer, Image, Input, InputNumber, Popconfirm, Space, Table, Tabs, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { sellerApi, type CategoryNode, type ProductRow, type ProductStatus, type ProductTab, type Sku } from '../api/seller'
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
  const [movementPage, setMovementPage] = useState(1)
  const movements = useQuery({
    queryKey: ['movements', stockSku?.id, movementPage],
    queryFn: () => sellerApi.movements(shopId, stockSku!.id, movementPage, MOVEMENT_PAGE_SIZE),
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
          { title: '', render: (_, s) => <Button size="small" onClick={() => { setStockSku(s); setMovementPage(1) }}>Nhập / xuất kho</Button> },
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
            pagination={{ current: movementPage, pageSize: MOVEMENT_PAGE_SIZE, total: movements.data?.totalCount, onChange: setMovementPage, size: 'small' }}
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

const MOVEMENT_PAGE_SIZE = 20

interface CategoryOption { value: string; label: string; children?: CategoryOption[] }

const toOptions = (nodes: CategoryNode[]): CategoryOption[] =>
  nodes.map((n) => ({ value: n.id, label: n.name, children: n.children.length > 0 ? toOptions(n.children) : undefined }))

const ProductsPage = ({ shopId }: { shopId: string }) => {
  const navigate = useNavigate()
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  // The tab lives in the URL (?tab=…), so the dashboard's links open the right one (D1)
  const [params, setParams] = useSearchParams()
  const tab: ProductTab = TABS.find((t) => t.key === params.get('tab'))?.key ?? 'All'
  const setTab = (next: ProductTab) => setParams((p) => { const n = new URLSearchParams(p); if (next === 'All') n.delete('tab'); else n.set('tab', next); return n })
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  // Lọc theo tồn kho và giá (III.3); chọn nhiều để ẩn / hiện / xoá / gửi duyệt một lần
  const [range, setRange] = useState<{ minStock: number | null; maxStock: number | null; minPrice: number | null; maxPrice: number | null }>(
    { minStock: null, maxStock: null, minPrice: null, maxPrice: null })
  const [selected, setSelected] = useState<string[]>([])
  // Leaf category (products sit on leaves only)
  const [categoryId, setCategoryId] = useState<string | null>(null)
  const categories = useQuery({ queryKey: ['categories'], queryFn: sellerApi.categories, staleTime: 600_000 })

  const products = useQuery({
    queryKey: ['products', shopId, tab, q, page, pageSize, range, categoryId],
    queryFn: () => sellerApi.products(shopId, { tab, q, page, pageSize, categoryId, ...range }),
  })
  const bulk = useMutation({
    mutationFn: (op: 'Submit' | 'Hide' | 'Show' | 'Delete') => sellerApi.bulkProducts(shopId, selected, op),
    onSuccess: (r) => {
      const failed = r.data.filter((x) => !x.ok)
      if (failed.length) void message.warning(`${r.message} Chưa làm được: ${failed.map((f) => f.error).filter(Boolean).slice(0, 3).join('; ')}`)
      else void message.success(r.message)
      setSelected([])
      void queryClient.invalidateQueries({ queryKey: ['products', shopId] })
    },
    onError: (e) => void message.error(errorText(e)),
  })
  const bulkCopy = useMutation({
    mutationFn: () => sellerApi.bulkCopyProducts(shopId, selected),
    onSuccess: (r) => {
      const failed = r.data.filter((x) => !x.ok)
      if (failed.length) void message.warning(`${r.message} Chưa làm được: ${failed.map((f) => f.error).filter(Boolean).slice(0, 3).join('; ')}`)
      else void message.success(r.message)
      setSelected([])
      void queryClient.invalidateQueries({ queryKey: ['products', shopId] })
    },
    onError: (e) => void message.error(errorText(e)),
  })
  const copy = useMutation({
    mutationFn: (id: string) => sellerApi.copyProduct(shopId, id),
    onSuccess: (r) => { void message.success(r.message); navigate(`/san-pham/${r.data}`) },
    onError: (e) => void message.error(errorText(e)),
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
      <Space wrap>
        <Input.Search placeholder="Tên sản phẩm hoặc mã SKU" allowClear style={{ width: 300 }} onSearch={(v) => { setQ(v); setPage(1) }} />
        <Cascader<CategoryOption> options={toOptions(categories.data ?? [])} placeholder="Danh mục" style={{ width: 240 }} showSearch
          onChange={(v) => { setCategoryId(v && v.length > 0 ? String(v[v.length - 1]) : null); setPage(1) }} aria-label="Lọc theo danh mục"
          data-testid="product-category-filter" />
        <InputNumber<number> min={0} placeholder="Tồn từ" value={range.minStock} onChange={(v) => { setRange({ ...range, minStock: v }); setPage(1) }}
          aria-label="Tồn kho từ" />
        <InputNumber<number> min={0} placeholder="Tồn đến" value={range.maxStock} onChange={(v) => { setRange({ ...range, maxStock: v }); setPage(1) }}
          aria-label="Tồn kho đến" />
        <InputNumber<number> min={0} placeholder="Giá từ" value={range.minPrice} onChange={(v) => { setRange({ ...range, minPrice: v }); setPage(1) }}
          aria-label="Giá từ" style={{ width: 130 }} />
        <InputNumber<number> min={0} placeholder="Giá đến" value={range.maxPrice} onChange={(v) => { setRange({ ...range, maxPrice: v }); setPage(1) }}
          aria-label="Giá đến" style={{ width: 130 }} />
      </Space>
      {selected.length > 0 && (
        <Space data-testid="bulk-bar">
          <Typography.Text>Đã chọn {selected.length} sản phẩm</Typography.Text>
          <Button size="small" loading={bulk.isPending} onClick={() => bulk.mutate('Hide')} data-testid="bulk-hide">Ẩn</Button>
          <Button size="small" loading={bulk.isPending} onClick={() => bulk.mutate('Show')}>Hiện</Button>
          <Button size="small" loading={bulk.isPending} onClick={() => bulk.mutate('Submit')}>Gửi duyệt</Button>
          <Button size="small" loading={bulkCopy.isPending} disabled={selected.length > 20} title={selected.length > 20 ? 'Mỗi lần sao chép tối đa 20 sản phẩm' : undefined}
            onClick={() => bulkCopy.mutate()} data-testid="bulk-copy">Sao chép</Button>
          <Popconfirm title={`Xoá ${selected.length} sản phẩm?`} okText="Xoá" cancelText="Huỷ" onConfirm={() => bulk.mutate('Delete')}>
            <Button size="small" danger loading={bulk.isPending}>Xoá</Button>
          </Popconfirm>
        </Space>
      )}
      <Table<ProductRow>
        rowKey="id"
        rowSelection={{ selectedRowKeys: selected, onChange: (keys) => setSelected(keys as string[]), preserveSelectedRowKeys: true }}
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
                <Button size="small" loading={copy.isPending && copy.variables === r.id} onClick={() => copy.mutate(r.id)} data-testid="copy-product">Sao chép</Button>
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
