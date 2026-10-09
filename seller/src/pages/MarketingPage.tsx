import { useState } from 'react'
import { App, Button, Card, DatePicker, Form, Input, InputNumber, Modal, Radio, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { marketingApi, xtraApi, type CampaignRegistration, type OpenCampaign, type FlashItemInput, type FlashSlot, type PickSku, type Promotion, type PromotionType, type XtraProgram } from '../api/marketing'
import { ApiError } from '../api/http'
import ProductPicker from '../components/ProductPicker'
import { formatPercentBp, formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'
import { sellerApi } from '../api/seller'
import { categoryNames, flashCriteriaText } from './flashCriteria'
import { flashFormValues, flashInput, promotionFormValues, promotionInput, type FlashForm, type PromotionForm } from './promotionForm'

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

type Option = { value: string; label: string }

/** Search results plus the options already chosen (an opened programme's SKUs may not be in the current search). */
const withKnown = (options: Option[], known: Option[]): Option[] => [...known.filter((k) => !options.some((o) => o.value === k.value)), ...options]

const NOT_STARTED = 'Sắp diễn ra'

const PromotionsTab = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  // The programme being edited (only before it starts); null = a new one
  const [editing, setEditing] = useState<Promotion | null>(null)
  const [form] = Form.useForm<PromotionForm>()
  const { skus, search } = useSkus(shopId)
  const list = useQuery({ queryKey: ['promotions', shopId], queryFn: () => marketingApi.promotions(shopId) })
  const knownProducts: Option[] = editing ? editing.productIds.map((id, i) => ({ value: id, label: editing.productNames[i] ?? id })) : []
  const products = withKnown([...new Map(skus.map((s) => [s.productId, s.productName])).entries()].map(([value, label]) => ({ value, label })), knownProducts)
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['promotions', shopId] })

  const openForm = (p: Promotion | null) => {
    setEditing(p)
    form.resetFields()
    if (p) form.setFieldsValue(promotionFormValues(p))
    setOpen(true)
  }
  const close = () => { setOpen(false); setEditing(null) }

  const create = useMutation({
    mutationFn: (v: PromotionForm): Promise<{ message: string }> => (editing ? marketingApi.updatePromotion(shopId, editing.id, promotionInput(v)) : marketingApi.createPromotion(shopId, promotionInput(v))),
    onSuccess: (r) => {
      message.success(r.message)
      close()
      form.resetFields()
      refresh()
    },
    onError: (e) => message.error(errorText(e, editing ? 'Không lưu được chương trình.' : 'Không tạo được chương trình.')),
  })
  const stop = useMutation({
    mutationFn: (id: string) => marketingApi.stopPromotion(shopId, id),
    onSuccess: (r) => { message.success(r.message); refresh() },
    onError: (e) => message.error(errorText(e, 'Không dừng được.')),
  })
  const knownSkus: Option[] = editing
    ? [...editing.skus.map((s) => ({ value: s.skuId, label: `${s.productName}${s.variant ? ` - ${s.variant}` : ''}` })),
      ...(editing.giftSkuId ? [{ value: editing.giftSkuId, label: editing.giftName ?? editing.giftSkuId }] : [])]
    : []
  const skuOptions = withKnown(skus.map((s) => ({ value: s.skuId, label: skuLabel(s) })), knownSkus)

  return (
    <>
      <Button type="primary" onClick={() => openForm(null)} style={{ marginBottom: 12 }} data-testid="promotion-new">Tạo chương trình</Button>
      <Table<Promotion> rowKey="id" loading={list.isLoading} dataSource={list.data ?? []} pagination={{ pageSize: 20 }}
        columns={[
          { title: 'Loại', dataIndex: 'typeLabel' },
          { title: 'Tên', dataIndex: 'name' },
          { title: 'Thời gian', render: (_, p) => `${formatDateTime(p.startAt)} – ${formatDateTime(p.endAt)}` },
          {
            title: 'Nội dung', render: (_, p) => p.type === 'Discount' ? p.skus.map((s) => `${s.productName}${s.variant ? ` (${s.variant})` : ''}: ${formatPrice(s.price)}`
              + `${s.perUserLimit ? ` · tối đa ${s.perUserLimit}/người` : ''}${s.quota ? ` · đã bán ${s.sold}/${s.quota} suất` : ''}`).join('; ')
              : p.type === 'Combo' ? `Mua ${p.minQuantity} giảm ${p.discountBp / 100}% — ${p.productNames.join(', ')}`
                : p.type === 'AddOn' ? `Kèm ${p.productNames.join(', ')}: ${p.skus.map((s) => `${s.productName} ${formatPrice(s.price)}`).join('; ')} (tối đa ${p.maxAddOnQuantity})`
                  : `Đơn từ ${formatPrice(p.minSpend)} tặng ${p.giftQuantity} × ${p.giftName}`,
          },
          { title: 'Trạng thái', dataIndex: 'state', render: (s: string) => <Tag color={s === 'Đang diễn ra' ? 'green' : s === 'Sắp diễn ra' ? 'blue' : 'default'}>{s}</Tag> },
          {
            title: '', render: (_, p) => p.status === 'Active' && p.state !== 'Đã kết thúc' && (
              <Space>
                {p.state === NOT_STARTED && <Button size="small" onClick={() => openForm(p)} data-testid="promotion-edit">Sửa</Button>}
                <Button size="small" danger onClick={() => stop.mutate(p.id)}>Dừng</Button>
              </Space>
            ),
          },
        ]} />
      <Modal title={editing ? `Sửa: ${editing.name}` : 'Tạo chương trình'} open={open} onCancel={close} onOk={() => form.submit()} okText="Lưu"
        confirmLoading={create.isPending} width={720} forceRender>
        <Form<PromotionForm> form={form} layout="vertical" onFinish={(v) => create.mutate(v)}
          initialValues={{ type: 'Discount', period: [dayjs(), dayjs().add(7, 'day')], skus: [{}], minQuantity: 3, discountPercent: 10, maxAddOnQuantity: 1, giftQuantity: 1 }}>
          <Form.Item name="type" label="Loại chương trình" extra={editing ? 'Không đổi được loại của chương trình đã tạo.' : undefined}>
            <Radio.Group options={TYPES} disabled={!!editing} />
          </Form.Item>
          <Form.Item name="name" label="Tên chương trình" rules={[{ required: true, message: 'Nhập tên.' }]}><Input maxLength={150} /></Form.Item>
          <Form.Item name="period" label="Thời gian (giờ Việt Nam)" rules={[{ required: true, message: 'Chọn thời gian.' }]}>
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
                              {type === 'Discount' && (
                                <>
                                  <Form.Item name={[f.name, 'perUserLimit']} tooltip="Để trống: không giới hạn">
                                    <InputNumber min={1} max={100} placeholder="Tối đa/người" style={{ width: 120 }} data-testid="promo-sku-limit" />
                                  </Form.Item>
                                  <Form.Item name={[f.name, 'quota']} tooltip="Để trống: không giới hạn">
                                    <InputNumber min={1} placeholder="Số suất" style={{ width: 110 }} data-testid="promo-sku-quota" />
                                  </Form.Item>
                                </>
                              )}
                              <Button onClick={() => remove(f.name)} data-confirm="local">Xoá</Button>
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

const ItemsEditor = ({ shopId, known = [] }: { shopId: string; known?: Option[] }) => {
  const { skus, search } = useSkus(shopId)
  return (
    <Form.List name="items">
      {(fields, { add, remove }) => (
        <>
          {fields.map((f) => (
            <Space key={f.key} align="baseline" wrap>
              <Form.Item name={[f.name, 'skuId']} rules={[{ required: true, message: 'Chọn phân loại.' }]}>
                <Select style={{ width: 360 }} options={withKnown(skus.map((s) => ({ value: s.skuId, label: skuLabel(s) })), known)} showSearch filterOption={false}
                  onSearch={search} placeholder="Phân loại" />
              </Form.Item>
              <Form.Item name={[f.name, 'flashPrice']} rules={[{ required: true, message: 'Giá.' }]}><InputNumber min={1000} step={1000} placeholder="Giá Flash Sale" /></Form.Item>
              <Form.Item name={[f.name, 'quota']} rules={[{ required: true, message: 'Suất.' }]}><InputNumber min={1} placeholder="Số suất" /></Form.Item>
              <Form.Item name={[f.name, 'perUserLimit']} initialValue={1}><InputNumber min={1} max={100} placeholder="Mỗi người" /></Form.Item>
              <Button onClick={() => remove(f.name)} data-confirm="local">Xoá</Button>
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
  // The shop's own slot being edited (before it starts); null = a new one
  const [editingSlot, setEditingSlot] = useState<FlashSlot | null>(null)
  const [registerSlot, setRegisterSlot] = useState<FlashSlot | null>(null)
  const [ownForm] = Form.useForm<FlashForm>()
  const [regForm] = Form.useForm<{ items: FlashItemInput[] }>()
  const mine = useQuery({ queryKey: ['flash-mine', shopId], queryFn: () => marketingApi.flashSales(shopId) })
  const open = useQuery({ queryKey: ['flash-open', shopId], queryFn: () => marketingApi.platformSlots(shopId) })
  const categories = useQuery({ queryKey: ['categories'], queryFn: sellerApi.categories, staleTime: 600_000 })
  const names = categoryNames(categories.data ?? [])
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['flash-mine', shopId] })

  const openOwn = (s: FlashSlot | null) => {
    setEditingSlot(s)
    ownForm.resetFields()
    if (s) ownForm.setFieldsValue(flashFormValues(s))
    setOwn(true)
  }
  const closeOwn = () => { setOwn(false); setEditingSlot(null) }
  const createOwn = useMutation({
    mutationFn: (v: FlashForm): Promise<{ message: string }> => (editingSlot ? marketingApi.updateFlashSale(shopId, editingSlot.id, flashInput(v)) : marketingApi.createFlashSale(shopId, flashInput(v))),
    onSuccess: (r) => { message.success(r.message); closeOwn(); ownForm.resetFields(); refresh() },
    onError: (e) => message.error(errorText(e, editingSlot ? 'Không lưu được Flash Sale.' : 'Không tạo được Flash Sale.')),
  })
  const register = useMutation({
    mutationFn: (v: { items: FlashItemInput[] }) => marketingApi.register(shopId, registerSlot!.id, v.items ?? []),
    onSuccess: (r) => { message.success(r.message); setRegisterSlot(null); regForm.resetFields(); refresh() },
    onError: (e) => message.error(errorText(e, 'Không đăng ký được.')),
  })

  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Space>
        <Button type="primary" onClick={() => openOwn(null)} data-testid="flash-own-new">Tạo Flash Sale của shop</Button>
      </Space>
      <Typography.Title level={5}>Khung Flash Sale của sàn đang mở đăng ký</Typography.Title>
      <Table<FlashSlot> rowKey="id" size="small" dataSource={open.data ?? []} pagination={false} locale={{ emptyText: 'Chưa có khung nào đang mở' }}
        columns={[
          { title: 'Khung giờ', render: (_, s) => `${formatDateTime(s.startAt)} – ${formatDateTime(s.endAt)}` },
          { title: 'Tiêu chí', render: (_, s) => <span data-testid="flash-criteria">{flashCriteriaText(s, names)}</span> },
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
          {
            title: '', render: (_, s) => s.owner === 'Shop' && s.state === NOT_STARTED && (
              <Button size="small" onClick={() => openOwn(s)} data-testid="flash-own-edit">Sửa</Button>
            ),
          },
        ]} />

      <Modal title={editingSlot ? 'Sửa Flash Sale của shop' : 'Flash Sale của shop'} open={own} onCancel={closeOwn} onOk={() => ownForm.submit()}
        okText={editingSlot ? 'Lưu' : 'Tạo'} width={860} confirmLoading={createOwn.isPending} forceRender>
        <Form form={ownForm} layout="vertical" onFinish={(v) => createOwn.mutate(v)} initialValues={{ period: [dayjs(), dayjs().add(2, 'hour')], items: [{ perUserLimit: 1 }] }}>
          <Form.Item name="period" label="Khung giờ (giờ Việt Nam)" rules={[{ required: true, message: 'Chọn khung giờ.' }]}>
            <DatePicker.RangePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} />
          </Form.Item>
          <ItemsEditor shopId={shopId}
            known={(editingSlot?.items ?? []).map((i) => ({ value: i.skuId, label: `${i.productName}${i.variant ? ` - ${i.variant}` : ''}` }))} />
        </Form>
      </Modal>
      <Modal title="Đăng ký Flash Sale của sàn" open={!!registerSlot} onCancel={() => setRegisterSlot(null)} onOk={() => regForm.submit()} okText="Gửi đăng ký"
        width={860} confirmLoading={register.isPending} destroyOnClose>
        {registerSlot && <Typography.Paragraph>Khung {formatDateTime(registerSlot.startAt)} — {flashCriteriaText(registerSlot, names)}.</Typography.Paragraph>}
        <Form form={regForm} layout="vertical" onFinish={(v) => register.mutate(v)} initialValues={{ items: [{ perUserLimit: 1 }] }}>
          <ItemsEditor shopId={shopId} />
        </Form>
      </Modal>
    </Space>
  )
}

const XTRA: Record<XtraProgram, { name: string; text: string }> = {
  FreeshipXtra: { name: 'Freeship+', text: 'Mã miễn phí vận chuyển của sàn dùng được cho đơn của shop — sàn tài trợ phí ship, shop trả phí dịch vụ trên doanh thu.' },
  VoucherXtra: { name: 'Voucher Plus', text: 'Voucher giảm giá Voucher Plus của sàn dùng được cho sản phẩm của shop — sàn chịu phần giảm, shop trả phí dịch vụ.' },
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
const REG_STATUS: Record<CampaignRegistration['status'], { text: string; color: string }> = {
  Pending: { text: 'Chờ duyệt', color: 'gold' },
  Approved: { text: 'Đã duyệt', color: 'green' },
  Rejected: { text: 'Từ chối', color: 'red' },
}

/** Chiến dịch của sàn (III.5): campaigns open to shops, put products forward, follow the decisions. */
const CampaignsTab = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const campaigns = useQuery({ queryKey: ['open-campaigns', shopId], queryFn: () => marketingApi.campaigns(shopId) })
  const [current, setCurrent] = useState<OpenCampaign | null>(null)
  const [picking, setPicking] = useState(false)
  const regs = useQuery({
    queryKey: ['campaign-regs', shopId, current?.id],
    queryFn: () => marketingApi.campaignRegistrations(shopId, current!.id),
    enabled: !!current,
  })
  const done = (r: { message: string }) => {
    message.success(r.message)
    void queryClient.invalidateQueries({ queryKey: ['campaign-regs', shopId] })
    void queryClient.invalidateQueries({ queryKey: ['open-campaigns', shopId] })
  }
  const fail = (e: unknown) => message.error(e instanceof ApiError ? e.message : 'Không thực hiện được.')
  const register = useMutation({
    mutationFn: (ids: string[]) => marketingApi.registerCampaign(shopId, current!.id, ids),
    onSuccess: (r) => { setPicking(false); done(r) },
    onError: fail,
  })
  const withdraw = useMutation({ mutationFn: (id: string) => marketingApi.withdrawCampaign(shopId, id), onSuccess: done, onError: fail })

  return (
    <>
      <Table<OpenCampaign> rowKey="id" loading={campaigns.isLoading} dataSource={campaigns.data ?? []} pagination={false}
        locale={{ emptyText: 'Hiện chưa có chiến dịch nào nhận đăng ký.' }}
        columns={[
          { title: 'Chiến dịch', render: (_, c) => <a href={`/su-kien/${c.slug}`} target="_blank" rel="noreferrer">{c.name}</a> },
          { title: 'Thời gian', render: (_, c) => `${formatDateTime(c.startAt)} – ${formatDateTime(c.endAt)}` },
          { title: 'Đã gửi', render: (_, c) => `${c.pending} chờ · ${c.approved} duyệt · ${c.rejected} từ chối` },
          { title: '', render: (_, c) => <Button size="small" onClick={() => setCurrent(c)} data-testid="campaign-open">Đăng ký sản phẩm</Button> },
        ]} />
      {current && (
        <Card title={`Sản phẩm đăng ký — ${current.name}`} style={{ marginTop: 16 }}
          extra={<Button type="primary" onClick={() => setPicking(true)} data-testid="campaign-pick">Chọn sản phẩm</Button>}>
          <Table<CampaignRegistration> rowKey="id" size="small" loading={regs.isLoading} dataSource={regs.data ?? []} pagination={false}
            locale={{ emptyText: 'Chưa đăng ký sản phẩm nào.' }}
            columns={[
              { title: 'Sản phẩm', dataIndex: 'productName' },
              { title: 'Giá từ', dataIndex: 'minPrice', render: (v: number) => formatPrice(v) },
              { title: 'Trạng thái', render: (_, r) => <Tag color={REG_STATUS[r.status].color} data-testid="campaign-reg-status">{REG_STATUS[r.status].text}</Tag> },
              { title: 'Lý do', dataIndex: 'rejectReason' },
              { title: '', render: (_, r) => <Button size="small" danger onClick={() => withdraw.mutate(r.id)}>Rút</Button> },
            ]} />
          <ProductPicker shopId={shopId} open={picking} title="Chọn sản phẩm đăng ký chiến dịch" value={[]} max={50}
            onClose={() => setPicking(false)} onPick={(ps) => register.mutate(ps.map((p) => p.id))} />
        </Card>
      )}
    </>
  )
}

const MarketingPage = ({ shopId }: { shopId: string }) => (
  <Card title="Kênh Marketing">
    <Tabs items={[
      { key: 'promotions', label: 'Chương trình của shop', children: <PromotionsTab shopId={shopId} /> },
      { key: 'flash', label: 'Flash Sale', children: <FlashTab shopId={shopId} /> },
      { key: 'xtra', label: 'Chương trình dịch vụ', children: <XtraTab shopId={shopId} /> },
      { key: 'campaigns', label: 'Chiến dịch của sàn', children: <CampaignsTab shopId={shopId} /> },
    ]} />
  </Card>
)

export default MarketingPage
