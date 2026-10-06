import { useState } from 'react'
import { App, Button, Card, DatePicker, Form, Input, InputNumber, Modal, Radio, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { marketingApi, xtraApi, type FlashItemInput, type FlashSlot, type PickSku, type Promotion, type PromotionType, type XtraProgram } from '../api/marketing'
import { ApiError } from '../api/http'
import { formatPercentBp, formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : fallback)

const TYPES: { value: PromotionType; label: string }[] = [
  { value: 'Discount', label: 'Chương trình giảm giá' },
  { value: 'Combo', label: 'Combo khuyến mãi' },
  { value: 'AddOn', label: 'Mua kèm deal sốc' },
  { value: 'Gift', label: 'Quà tặng kèm' },
]

const skuLabel = (s: PickSku) => `${s.productName}${s.variant ? ` - ${s.variant}` : ''} (${formatPrice(s.price)}, còn ${s.available})`

/** Searchable SKU picker over the shop's active SKUs. */
const useSkus = (shopId: string) => {
  const [q, setQ] = useState('')
  const skus = useQuery({ queryKey: ['marketing-skus', shopId, q], queryFn: () => marketingApi.skus(shopId, q) })
  return { skus: skus.data ?? [], search: setQ }
}

interface PromotionForm {
  type: PromotionType
  name: string
  period: [Dayjs, Dayjs]
  productIds?: string[]
  skus?: { skuId: string; price: number }[]
  minQuantity?: number
  discountPercent?: number
  maxAddOnQuantity?: number
  minSpend?: number
  giftSkuId?: string
  giftQuantity?: number
}

const PromotionsTab = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm<PromotionForm>()
  const { skus, search } = useSkus(shopId)
  const list = useQuery({ queryKey: ['promotions', shopId], queryFn: () => marketingApi.promotions(shopId) })
  const products = [...new Map(skus.map((s) => [s.productId, s.productName])).entries()].map(([value, label]) => ({ value, label }))
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['promotions', shopId] })

  const create = useMutation({
    mutationFn: (v: PromotionForm) => marketingApi.createPromotion(shopId, {
      type: v.type, name: v.name.trim(), startAt: v.period[0].toISOString(), endAt: v.period[1].toISOString(),
      productIds: v.productIds ?? [], skus: (v.skus ?? []).filter((s) => s?.skuId),
      minQuantity: v.minQuantity ?? 0, discountBp: Math.round((v.discountPercent ?? 0) * 100), discountAmount: 0,
      maxAddOnQuantity: v.maxAddOnQuantity ?? 0, minSpend: v.minSpend ?? 0, giftSkuId: v.giftSkuId ?? null, giftQuantity: v.giftQuantity ?? 0,
    }),
    onSuccess: (r) => {
      message.success(r.message)
      setOpen(false)
      form.resetFields()
      refresh()
    },
    onError: (e) => message.error(errorText(e, 'Không tạo được chương trình.')),
  })
  const stop = useMutation({
    mutationFn: (id: string) => marketingApi.stopPromotion(shopId, id),
    onSuccess: (r) => { message.success(r.message); refresh() },
    onError: (e) => message.error(errorText(e, 'Không dừng được.')),
  })
  const skuOptions = skus.map((s) => ({ value: s.skuId, label: skuLabel(s) }))

  return (
    <>
      <Button type="primary" onClick={() => setOpen(true)} style={{ marginBottom: 12 }} data-testid="promotion-new">Tạo chương trình</Button>
      <Table<Promotion> rowKey="id" loading={list.isLoading} dataSource={list.data ?? []} pagination={{ pageSize: 20 }}
        columns={[
          { title: 'Loại', dataIndex: 'typeLabel' },
          { title: 'Tên', dataIndex: 'name' },
          { title: 'Thời gian', render: (_, p) => `${formatDateTime(p.startAt)} – ${formatDateTime(p.endAt)}` },
          {
            title: 'Nội dung', render: (_, p) => p.type === 'Discount' ? p.skus.map((s) => `${s.productName}${s.variant ? ` (${s.variant})` : ''}: ${formatPrice(s.price)}`).join('; ')
              : p.type === 'Combo' ? `Mua ${p.minQuantity} giảm ${p.discountBp / 100}% — ${p.productNames.join(', ')}`
                : p.type === 'AddOn' ? `Kèm ${p.productNames.join(', ')}: ${p.skus.map((s) => `${s.productName} ${formatPrice(s.price)}`).join('; ')} (tối đa ${p.maxAddOnQuantity})`
                  : `Đơn từ ${formatPrice(p.minSpend)} tặng ${p.giftQuantity} × ${p.giftName}`,
          },
          { title: 'Trạng thái', dataIndex: 'state', render: (s: string) => <Tag color={s === 'Đang diễn ra' ? 'green' : s === 'Sắp diễn ra' ? 'blue' : 'default'}>{s}</Tag> },
          { title: '', render: (_, p) => p.status === 'Active' && p.state !== 'Đã kết thúc' && <Button size="small" danger onClick={() => stop.mutate(p.id)}>Dừng</Button> },
        ]} />
      <Modal title="Tạo chương trình" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} okText="Lưu" confirmLoading={create.isPending}
        width={720} destroyOnClose>
        <Form<PromotionForm> form={form} layout="vertical" onFinish={(v) => create.mutate(v)}
          initialValues={{ type: 'Discount', period: [dayjs(), dayjs().add(7, 'day')], skus: [{}], minQuantity: 3, discountPercent: 10, maxAddOnQuantity: 1, giftQuantity: 1 }}>
          <Form.Item name="type" label="Loại chương trình"><Radio.Group options={TYPES} /></Form.Item>
          <Form.Item name="name" label="Tên chương trình" rules={[{ required: true, message: 'Nhập tên.' }]}><Input maxLength={150} /></Form.Item>
          <Form.Item name="period" label="Thời gian" rules={[{ required: true, message: 'Chọn thời gian.' }]}>
            <DatePicker.RangePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(a, b) => a.type !== b.type}>
            {({ getFieldValue }) => {
              const type: PromotionType = getFieldValue('type')
              return (
                <>
                  {type !== 'Discount' && (
                    <Form.Item name="productIds" label={type === 'Combo' ? 'Sản phẩm trong combo' : 'Sản phẩm chính'} rules={[{ required: true, message: 'Chọn sản phẩm.' }]}>
                      <Select mode="multiple" options={products} showSearch filterOption={false} onSearch={search} />
                    </Form.Item>
                  )}
                  {(type === 'Discount' || type === 'AddOn') && (
                    <Form.List name="skus">
                      {(fields, { add, remove }) => (
                        <>
                          {fields.map((f) => (
                            <Space key={f.key} align="baseline">
                              <Form.Item name={[f.name, 'skuId']} rules={[{ required: true, message: 'Chọn phân loại.' }]}>
                                <Select style={{ width: 420 }} options={skuOptions} showSearch filterOption={false} onSearch={search} placeholder="Phân loại" />
                              </Form.Item>
                              <Form.Item name={[f.name, 'price']} rules={[{ required: true, message: 'Nhập giá.' }]}>
                                <InputNumber min={1000} step={1000} placeholder={type === 'AddOn' ? 'Giá mua kèm' : 'Giá giảm'} style={{ width: 150 }} />
                              </Form.Item>
                              <Button onClick={() => remove(f.name)}>Xoá</Button>
                            </Space>
                          ))}
                          <Button onClick={() => add({})}>+ Thêm phân loại</Button>
                        </>
                      )}
                    </Form.List>
                  )}
                  {type === 'Combo' && (
                    <Space>
                      <Form.Item name="minQuantity" label="Mua tối thiểu"><InputNumber min={2} max={100} /></Form.Item>
                      <Form.Item name="discountPercent" label="Giảm (%)"><InputNumber min={1} max={90} /></Form.Item>
                    </Space>
                  )}
                  {type === 'AddOn' && <Form.Item name="maxAddOnQuantity" label="Số sản phẩm mua kèm tối đa mỗi đơn"><InputNumber min={1} max={10} /></Form.Item>}
                  {type === 'Gift' && (
                    <Space wrap>
                      <Form.Item name="minSpend" label="Đơn tối thiểu (₫)" rules={[{ required: true, message: 'Nhập số tiền.' }]}><InputNumber min={1000} step={10000} /></Form.Item>
                      <Form.Item name="giftSkuId" label="Quà tặng" rules={[{ required: true, message: 'Chọn quà.' }]}>
                        <Select style={{ width: 360 }} options={skuOptions} showSearch filterOption={false} onSearch={search} />
                      </Form.Item>
                      <Form.Item name="giftQuantity" label="Số lượng"><InputNumber min={1} max={5} /></Form.Item>
                    </Space>
                  )}
                </>
              )
            }}
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}

const ItemsEditor = ({ shopId }: { shopId: string }) => {
  const { skus, search } = useSkus(shopId)
  return (
    <Form.List name="items">
      {(fields, { add, remove }) => (
        <>
          {fields.map((f) => (
            <Space key={f.key} align="baseline" wrap>
              <Form.Item name={[f.name, 'skuId']} rules={[{ required: true, message: 'Chọn phân loại.' }]}>
                <Select style={{ width: 360 }} options={skus.map((s) => ({ value: s.skuId, label: skuLabel(s) }))} showSearch filterOption={false}
                  onSearch={search} placeholder="Phân loại" />
              </Form.Item>
              <Form.Item name={[f.name, 'flashPrice']} rules={[{ required: true, message: 'Giá.' }]}><InputNumber min={1000} step={1000} placeholder="Giá Flash Sale" /></Form.Item>
              <Form.Item name={[f.name, 'quota']} rules={[{ required: true, message: 'Suất.' }]}><InputNumber min={1} placeholder="Số suất" /></Form.Item>
              <Form.Item name={[f.name, 'perUserLimit']} initialValue={1}><InputNumber min={1} max={100} placeholder="Mỗi người" /></Form.Item>
              <Button onClick={() => remove(f.name)}>Xoá</Button>
            </Space>
          ))}
          <Button onClick={() => add({ perUserLimit: 1 })}>+ Thêm phân loại</Button>
        </>
      )}
    </Form.List>
  )
}

const FlashTab = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [own, setOwn] = useState(false)
  const [registerSlot, setRegisterSlot] = useState<FlashSlot | null>(null)
  const [ownForm] = Form.useForm<{ period: [Dayjs, Dayjs]; items: FlashItemInput[] }>()
  const [regForm] = Form.useForm<{ items: FlashItemInput[] }>()
  const mine = useQuery({ queryKey: ['flash-mine', shopId], queryFn: () => marketingApi.flashSales(shopId) })
  const open = useQuery({ queryKey: ['flash-open', shopId], queryFn: () => marketingApi.platformSlots(shopId) })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['flash-mine', shopId] })

  const createOwn = useMutation({
    mutationFn: (v: { period: [Dayjs, Dayjs]; items: FlashItemInput[] }) =>
      marketingApi.createFlashSale(shopId, { startAt: v.period[0].toISOString(), endAt: v.period[1].toISOString(), items: v.items ?? [] }),
    onSuccess: (r) => { message.success(r.message); setOwn(false); ownForm.resetFields(); refresh() },
    onError: (e) => message.error(errorText(e, 'Không tạo được Flash Sale.')),
  })
  const register = useMutation({
    mutationFn: (v: { items: FlashItemInput[] }) => marketingApi.register(shopId, registerSlot!.id, v.items ?? []),
    onSuccess: (r) => { message.success(r.message); setRegisterSlot(null); regForm.resetFields(); refresh() },
    onError: (e) => message.error(errorText(e, 'Không đăng ký được.')),
  })

  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Space>
        <Button type="primary" onClick={() => setOwn(true)} data-testid="flash-own-new">Tạo Flash Sale của shop</Button>
      </Space>
      <Typography.Title level={5}>Khung Flash Sale của sàn đang mở đăng ký</Typography.Title>
      <Table<FlashSlot> rowKey="id" size="small" dataSource={open.data ?? []} pagination={false} locale={{ emptyText: 'Chưa có khung nào đang mở' }}
        columns={[
          { title: 'Khung giờ', render: (_, s) => `${formatDateTime(s.startAt)} – ${formatDateTime(s.endAt)}` },
          { title: 'Tiêu chí', render: (_, s) => `Giảm ≥ ${s.minDiscountBp / 100}% · đánh giá ≥ ${s.minRating}★` },
          { title: '', render: (_, s) => <Button size="small" onClick={() => setRegisterSlot(s)}>Đăng ký sản phẩm</Button> },
        ]} />
      <Typography.Title level={5}>Flash Sale của tôi & đăng ký</Typography.Title>
      <Table<FlashSlot> rowKey="id" loading={mine.isLoading} dataSource={mine.data ?? []} pagination={{ pageSize: 10 }}
        expandable={{
          expandedRowRender: (s) => (
            <Table size="small" rowKey="id" pagination={false} dataSource={s.items} columns={[
              { title: 'Phân loại', render: (_, i) => `${i.productName}${i.variant ? ` - ${i.variant}` : ''}` },
              { title: 'Giá', render: (_, i) => `${formatPrice(i.flashPrice)} (gốc ${formatPrice(i.basePrice)})` },
              { title: 'Đã bán', render: (_, i) => `${i.sold}/${i.quota}` },
              { title: 'Trạng thái', render: (_, i) => <Tag color={i.status === 'Approved' ? 'green' : i.status === 'Rejected' ? 'red' : 'gold'}>{i.status === 'Approved' ? 'Đã duyệt' : i.status === 'Rejected' ? `Từ chối: ${i.rejectReason}` : 'Chờ duyệt'}</Tag> },
            ]} />
          ),
        }}
        columns={[
          { title: 'Loại', render: (_, s) => (s.owner === 'Shop' ? 'Của shop' : 'Của sàn') },
          { title: 'Khung giờ', render: (_, s) => `${formatDateTime(s.startAt)} – ${formatDateTime(s.endAt)}` },
          { title: 'Sản phẩm', render: (_, s) => s.items.length },
          { title: 'Trạng thái', dataIndex: 'state' },
        ]} />

      <Modal title="Flash Sale của shop" open={own} onCancel={() => setOwn(false)} onOk={() => ownForm.submit()} okText="Tạo" width={860}
        confirmLoading={createOwn.isPending} destroyOnClose>
        <Form form={ownForm} layout="vertical" onFinish={(v) => createOwn.mutate(v)} initialValues={{ period: [dayjs(), dayjs().add(2, 'hour')], items: [{ perUserLimit: 1 }] }}>
          <Form.Item name="period" label="Khung giờ" rules={[{ required: true, message: 'Chọn khung giờ.' }]}>
            <DatePicker.RangePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} />
          </Form.Item>
          <ItemsEditor shopId={shopId} />
        </Form>
      </Modal>
      <Modal title="Đăng ký Flash Sale của sàn" open={!!registerSlot} onCancel={() => setRegisterSlot(null)} onOk={() => regForm.submit()} okText="Gửi đăng ký"
        width={860} confirmLoading={register.isPending} destroyOnClose>
        {registerSlot && <Typography.Paragraph>Khung {formatDateTime(registerSlot.startAt)} — giảm tối thiểu {registerSlot.minDiscountBp / 100}%, đánh giá từ {registerSlot.minRating}★.</Typography.Paragraph>}
        <Form form={regForm} layout="vertical" onFinish={(v) => register.mutate(v)} initialValues={{ items: [{ perUserLimit: 1 }] }}>
          <ItemsEditor shopId={shopId} />
        </Form>
      </Modal>
    </Space>
  )
}

const XTRA: Record<XtraProgram, { name: string; text: string }> = {
  FreeshipXtra: { name: 'Freeship Xtra', text: 'Mã miễn phí vận chuyển của sàn dùng được cho đơn của shop — sàn tài trợ phí ship, shop trả phí dịch vụ trên doanh thu.' },
  VoucherXtra: { name: 'Voucher Xtra', text: 'Voucher giảm giá Xtra của sàn dùng được cho sản phẩm của shop — sàn chịu phần giảm, shop trả phí dịch vụ.' },
}

/** Chương trình dịch vụ (spec 3.9): join / leave Freeship Xtra and Voucher Xtra; applies to orders placed from then on. */
const XtraTab = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: ['xtra', shopId], queryFn: () => xtraApi.list(shopId) })
  const toggle = useMutation({
    mutationFn: ({ program, join }: { program: XtraProgram; join: boolean }) => xtraApi.set(shopId, program, join),
    onSuccess: (r) => {
      message.success(r.message)
      void queryClient.invalidateQueries({ queryKey: ['xtra', shopId] })
    },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không thực hiện được.'),
  })
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      {(list.data ?? []).map((p) => (
        <Card key={p.program} size="small" title={XTRA[p.program].name} extra={
          <Switch checked={p.joined} loading={toggle.isPending} data-testid={`xtra-${p.program}`}
            onChange={(join) => toggle.mutate({ program: p.program, join })} />
        }>
          <Typography.Paragraph style={{ marginBottom: 4 }}>{XTRA[p.program].text}</Typography.Paragraph>
          <Typography.Text type="secondary">
            Phí dịch vụ {formatPercentBp(p.rateBp)} trên giá trị đơn sau giảm giá của shop
            {p.joined && p.since ? ` · tham gia từ ${formatDateTime(p.since)}` : ''}. Đơn đã đặt giữ nguyên chương trình lúc đặt.
          </Typography.Text>
        </Card>
      ))}
    </Space>
  )
}

/** Kênh Marketing (spec III.5): programmes, Flash Sale của shop, đăng ký Flash Sale của sàn. Vouchers stay on their own page. */
const MarketingPage = ({ shopId }: { shopId: string }) => (
  <Card title="Kênh Marketing">
    <Tabs items={[
      { key: 'promotions', label: 'Chương trình của shop', children: <PromotionsTab shopId={shopId} /> },
      { key: 'flash', label: 'Flash Sale', children: <FlashTab shopId={shopId} /> },
      { key: 'xtra', label: 'Chương trình dịch vụ', children: <XtraTab shopId={shopId} /> },
    ]} />
  </Card>
)

export default MarketingPage
