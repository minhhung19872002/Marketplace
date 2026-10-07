import { useState } from 'react'
import { App, Button, Card, Cascader, DatePicker, Form, Input, InputNumber, Modal, Radio, Select, Space, Switch, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { voucherApi, type Voucher, type VoucherInput } from '../api/vouchers'
import { sellerApi, type CategoryNode } from '../api/seller'
import { ApiError } from '../api/http'
import { formatPrice } from '../lib/money'
import { addDaysIso, formatDateTime, vnTodayIso, vnWallTime } from '../lib/datetime'
import { toFormValues, toVoucherInput, type VoucherFormValues as FormValues } from './voucherForm'

const PAGE_SIZE = 20

interface CategoryOption { value: string; label: string; children?: CategoryOption[] }

const toOptions = (nodes: CategoryNode[]): CategoryOption[] =>
  nodes.map((n) => ({ value: n.id, label: n.name, children: n.children.length > 0 ? toOptions(n.children) : undefined }))

/** Root → leaf id path of each leaf, so a chosen leaf shows in the cascader. */
const leafPaths = (nodes: CategoryNode[], trail: string[] = [], out = new Map<string, string[]>()): Map<string, string[]> => {
  for (const n of nodes) {
    if (n.children.length === 0) out.set(n.id, [...trail, n.id])
    else leafPaths(n.children, [...trail, n.id], out)
  }
  return out
}

/** Inline multi-select of the shop's products, searched by name; the already chosen ones keep their names. */
const VoucherProductSelect = ({ shopId, value, onChange }: { shopId: string; value?: string[]; onChange?: (v: string[]) => void }) => {
  const [q, setQ] = useState('')
  const [names, setNames] = useState<Record<string, string>>({})
  const found = useQuery({
    queryKey: ['voucher-products', shopId, q],
    queryFn: () => sellerApi.products(shopId, { tab: 'All', q, page: 1, pageSize: 20 }),
  })
  const missing = (value ?? []).filter((id) => !names[id])
  const chosen = useQuery({
    queryKey: ['voucher-products-chosen', shopId, missing],
    queryFn: () => Promise.all(missing.map((id) => sellerApi.product(shopId, id))),
    enabled: missing.length > 0,
  })
  const known: Record<string, string> = { ...names }
  for (const p of found.data?.items ?? []) known[p.id] = p.name
  for (const p of chosen.data ?? []) known[p.id] = p.name
  return (
    <Select
      mode="multiple"
      allowClear
      showSearch
      filterOption={false}
      onSearch={setQ}
      placeholder="Để trống = mọi sản phẩm của shop"
      value={value}
      onChange={(v: string[]) => { setNames(known); onChange?.(v) }}
      options={Object.entries(known).map(([id, name]) => ({ value: id, label: name }))}
      loading={found.isFetching}
      data-testid="voucher-products"
    />
  )
}

const describe = (v: Voucher) =>
  v.type === 'Amount' ? `Giảm ${formatPrice(v.discountValue)}` : `Giảm ${v.discountPercentBp / 100}% tối đa ${formatPrice(v.maxDiscount ?? 0)}`

const stateColor: Record<string, string> = { 'Đang diễn ra': 'green', 'Sắp diễn ra': 'blue', 'Hết lượt': 'orange' }

/** Kênh Marketing → Mã giảm giá của shop (≤ 1 mã mỗi đơn, shop chịu phần giảm). */
const VouchersPage = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  // The voucher being edited; null = a new one
  const [editing, setEditing] = useState<Voucher | null>(null)
  const [page, setPage] = useState(1)
  const [form] = Form.useForm<FormValues>()
  const list = useQuery({ queryKey: ['vouchers', shopId, page], queryFn: () => voucherApi.list(shopId, page, PAGE_SIZE) })
  const categories = useQuery({ queryKey: ['categories'], queryFn: sellerApi.categories, staleTime: 600_000 })
  const paths = leafPaths(categories.data ?? [])

  const openForm = (v: Voucher | null) => {
    setEditing(v)
    form.setFieldsValue(v ? toFormValues(v) : {
      code: '', name: '', type: 'Amount', discountValue: undefined, percent: undefined, maxDiscount: undefined, minOrder: 0, totalQuota: undefined,
      perUserLimit: 1, isPublic: true, followersOnly: false, productIds: [], categoryIds: [],
      period: [dayjs(vnWallTime(new Date().toISOString())), dayjs(`${addDaysIso(vnTodayIso(), 30)}T23:59`)],
    })
    setOpen(true)
  }

  const save = useMutation({
    mutationFn: (input: VoucherInput) => (editing ? voucherApi.update(shopId, editing.id, input) : voucherApi.create(shopId, input)),
    onSuccess: (r) => {
      message.success(r.message)
      setOpen(false)
      setEditing(null)
      form.resetFields()
      void queryClient.invalidateQueries({ queryKey: ['vouchers', shopId] })
    },
    onError: (e) => {
      if (e instanceof ApiError && e.fieldErrors.length > 0)
        form.setFields(e.fieldErrors.map((f) => ({ name: f.field.replace(/^input\./, '') as keyof FormValues, errors: [f.message] })))
      message.error(e instanceof ApiError ? e.message : 'Không lưu được voucher.')
    },
  })
  const stop = useMutation({
    mutationFn: (id: string) => voucherApi.stop(shopId, id),
    onSuccess: (r) => {
      message.success(r.message)
      void queryClient.invalidateQueries({ queryKey: ['vouchers', shopId] })
    },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không dừng được voucher.'),
  })

  const submit = (v: FormValues) => save.mutate(toVoucherInput(v))

  return (
    <Card
      title="Mã giảm giá của shop"
      extra={<Button type="primary" onClick={() => openForm(null)} data-testid="voucher-new">Tạo mã giảm giá</Button>}
    >
      <Table<Voucher>
        rowKey="id"
        loading={list.isLoading}
        dataSource={list.data?.items ?? []}
        pagination={{ current: page, pageSize: PAGE_SIZE, total: list.data?.totalCount ?? 0, onChange: setPage, showSizeChanger: false }}
        columns={[
          { title: 'Mã', dataIndex: 'code', render: (c: string) => <Typography.Text strong>{c}</Typography.Text> },
          { title: 'Tên', dataIndex: 'name' },
          { title: 'Mức giảm', render: (_, v) => describe(v) },
          { title: 'Đơn tối thiểu', dataIndex: 'minOrder', render: (m: number) => formatPrice(m) },
          { title: 'Thời gian', render: (_, v) => `${formatDateTime(v.startAt)} – ${formatDateTime(v.endAt)}` },
          { title: 'Đã dùng', render: (_, v) => `${v.usedCount}${v.totalQuota ? ` / ${v.totalQuota}` : ''}` },
          {
            title: 'Hiệu quả',
            render: (_, v) => v.stats && (
              <span data-testid="voucher-stats">
                {v.stats.claims} lượt lưu · {v.stats.orders} đơn<br />{formatPrice(v.stats.sales)} doanh số
              </span>
            ),
          },
          { title: 'Trạng thái', dataIndex: 'state', render: (s: string) => <Tag color={stateColor[s] ?? 'default'}>{s}</Tag> },
          {
            title: '',
            render: (_, v) => v.isActive && v.state !== 'Đã kết thúc' && (
              <Space>
                <Button size="small" onClick={() => openForm(v)} data-testid="voucher-edit">Sửa</Button>
                <Button size="small" danger loading={stop.isPending} onClick={() => stop.mutate(v.id)}>Dừng</Button>
              </Space>
            ),
          },
        ]}
      />

      <Modal title={editing ? `Sửa mã ${editing.code}` : 'Tạo mã giảm giá'} open={open} onCancel={() => { setOpen(false); setEditing(null) }}
        onOk={() => form.submit()} okText="Lưu" confirmLoading={save.isPending} forceRender>
        <Form<FormValues> form={form} layout="vertical" onFinish={submit}>
          <Form.Item name="code" label="Mã voucher" extra={editing ? 'Mã đã tạo không đổi được.' : undefined}
            rules={[{ required: true, message: 'Vui lòng nhập mã.' }, { pattern: /^[A-Za-z0-9]{3,20}$/, message: '3–20 chữ cái hoặc số, không dấu.' }]}>
            <Input placeholder="VD: SHOPGIAM20K" disabled={!!editing} />
          </Form.Item>
          <Form.Item name="name" label="Tên chương trình" rules={[{ required: true, message: 'Vui lòng nhập tên.' }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="type" label="Loại giảm giá">
            <Radio.Group options={[{ value: 'Amount', label: 'Theo số tiền' }, { value: 'Percent', label: 'Theo phần trăm' }]} />
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(a, b) => a.type !== b.type}>
            {({ getFieldValue }) => getFieldValue('type') === 'Amount' ? (
              <Form.Item name="discountValue" label="Số tiền giảm (₫)" rules={[{ required: true, message: 'Vui lòng nhập số tiền giảm.' }]}>
                <InputNumber min={1000} step={1000} style={{ width: '100%' }} />
              </Form.Item>
            ) : (
              <Space>
                <Form.Item name="percent" label="Giảm (%)" rules={[{ required: true, message: 'Nhập %.' }]}>
                  <InputNumber min={1} max={100} />
                </Form.Item>
                <Form.Item name="maxDiscount" label="Giảm tối đa (₫)" rules={[{ required: true, message: 'Nhập mức tối đa.' }]}>
                  <InputNumber min={1000} step={1000} />
                </Form.Item>
              </Space>
            )}
          </Form.Item>
          <Form.Item name="minOrder" label="Giá trị đơn tối thiểu (₫)">
            <InputNumber min={0} step={1000} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="productIds" label="Áp dụng cho sản phẩm">
            <VoucherProductSelect shopId={shopId} />
          </Form.Item>
          <Form.Item name="categoryIds" label="Áp dụng cho danh mục" extra="Để trống = mọi danh mục. Chọn cả sản phẩm và danh mục thì sản phẩm phải thoả cả hai."
            getValueProps={(ids: string[] | undefined) => ({ value: (ids ?? []).map((id) => paths.get(id) ?? [id]) })}
            getValueFromEvent={(v: string[][] | undefined) => (v ?? []).map((path) => path[path.length - 1])}>
            <Cascader multiple options={toOptions(categories.data ?? [])} showCheckedStrategy={Cascader.SHOW_CHILD}
              placeholder="Để trống = mọi danh mục" maxTagCount="responsive" data-testid="voucher-categories" />
          </Form.Item>
          <Form.Item name="period" label="Thời gian sử dụng (giờ Việt Nam)" rules={[{ required: true, message: 'Chọn thời gian.' }]}>
            <DatePicker.RangePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} />
          </Form.Item>
          <Space>
            <Form.Item name="totalQuota" label="Tổng lượt sử dụng">
              <InputNumber min={1} placeholder="Không giới hạn" />
            </Form.Item>
            <Form.Item name="perUserLimit" label="Lượt mỗi người">
              <InputNumber min={1} max={100} />
            </Form.Item>
          </Space>
          <Space size="large">
            <Form.Item name="isPublic" label="Hiển thị công khai" valuePropName="checked"><Switch /></Form.Item>
            <Form.Item name="followersOnly" label="Chỉ người theo dõi shop" valuePropName="checked"><Switch /></Form.Item>
          </Space>
        </Form>
      </Modal>
    </Card>
  )
}

export default VouchersPage
