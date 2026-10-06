import { useState } from 'react'
import { App, Button, Card, DatePicker, Form, Input, InputNumber, Modal, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { POSITION_LABEL, marketingApi, type Banner, type BannerPosition, type Campaign, type CampaignBlock, type FlashItem, type FlashSlot } from '../api/marketing'
import { ApiError } from '../api/http'
import { formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : fallback)

const FlashTab = () => {
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm<{ date: Dayjs; hour: number; minDiscount: number; minRating: number }>()
  const slots = useQuery({ queryKey: ['flash-slots'], queryFn: marketingApi.slots })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['flash-slots'] })
  const create = useMutation({
    mutationFn: (v: { date: Dayjs; hour: number; minDiscount: number; minRating: number }) => marketingApi.createSlot({
      date: v.date.format('YYYY-MM-DD'), hour: v.hour, minDiscountBp: Math.round((v.minDiscount ?? 0) * 100), minRating: v.minRating ?? 0, categoryIds: [],
    }),
    onSuccess: (r) => { message.success(r.message); setOpen(false); refresh() },
    onError: (e) => message.error(errorText(e, 'Không mở được khung.')),
  })
  const approve = useMutation({ mutationFn: marketingApi.approve, onSuccess: (r) => { message.success(r.message); refresh() }, onError: (e) => message.error(errorText(e, 'Không duyệt được.')) })
  const reject = (item: FlashItem) => {
    let reason = ''
    modal.confirm({
      title: `Từ chối ${item.productName}`,
      content: <Input.TextArea rows={3} onChange={(e) => { reason = e.target.value }} placeholder="Lý do" />,
      okText: 'Từ chối',
      okButtonProps: { danger: true },
      onOk: async () => {
        try {
          message.success((await marketingApi.reject(item.id, reason)).message)
          refresh()
        } catch (e) {
          message.error(errorText(e, 'Không từ chối được.'))
        }
      },
    })
  }
  return (
    <>
      <Button type="primary" onClick={() => setOpen(true)} style={{ marginBottom: 12 }}>Mở khung Flash Sale</Button>
      <Table<FlashSlot> rowKey="id" loading={slots.isLoading} dataSource={slots.data ?? []} pagination={false}
        expandable={{
          expandedRowRender: (s) => (
            <Table<FlashItem> size="small" rowKey="id" pagination={false} dataSource={s.items} columns={[
              { title: 'Phân loại', render: (_, i) => `${i.productName}${i.variant ? ` - ${i.variant}` : ''}` },
              { title: 'Giá', render: (_, i) => `${formatPrice(i.flashPrice)} (gốc ${formatPrice(i.basePrice)})` },
              { title: 'Suất', render: (_, i) => `${i.sold}/${i.quota} · ${i.perUserLimit}/người` },
              { title: 'Trạng thái', render: (_, i) => <Tag color={i.status === 'Approved' ? 'green' : i.status === 'Rejected' ? 'red' : 'gold'}>{i.status}</Tag> },
              {
                title: '', render: (_, i) => i.status === 'Pending' && (
                  <Space>
                    <Button size="small" type="primary" onClick={() => approve.mutate(i.id)}>Duyệt</Button>
                    <Button size="small" danger onClick={() => reject(i)}>Từ chối</Button>
                  </Space>
                ),
              },
            ]} />
          ),
        }}
        columns={[
          { title: 'Khung giờ', render: (_, s) => `${formatDateTime(s.startAt)} – ${formatDateTime(s.endAt)}` },
          { title: 'Tiêu chí', render: (_, s) => `Giảm ≥ ${s.minDiscountBp / 100}% · ≥ ${s.minRating}★` },
          { title: 'Đăng ký', render: (_, s) => `${s.items.filter((i) => i.status === 'Pending').length} chờ / ${s.items.length}` },
          { title: 'Trạng thái', dataIndex: 'state' },
        ]} />
      <Modal title="Mở khung Flash Sale" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} okText="Mở khung" confirmLoading={create.isPending} destroyOnClose>
        <Form form={form} layout="vertical" onFinish={(v) => create.mutate(v)} initialValues={{ date: dayjs().add(1, 'day'), hour: 12, minDiscount: 10, minRating: 0 }}>
          <Form.Item name="date" label="Ngày"><DatePicker format="DD/MM/YYYY" /></Form.Item>
          <Form.Item name="hour" label="Giờ bắt đầu (theo FLASH.SLOT_HOURS)">
            <Select options={[0, 9, 12, 15, 21].map((h) => ({ value: h, label: `${String(h).padStart(2, '0')}:00` }))} />
          </Form.Item>
          <Space>
            <Form.Item name="minDiscount" label="Giảm tối thiểu (%)"><InputNumber min={0} max={90} /></Form.Item>
            <Form.Item name="minRating" label="Đánh giá tối thiểu"><InputNumber min={0} max={5} step={0.5} /></Form.Item>
          </Space>
        </Form>
      </Modal>
    </>
  )
}

interface BannerForm {
  id: string | null
  position: BannerPosition
  title: string
  imageUrl: string
  link: string
  period: [Dayjs, Dayjs]
  sortOrder: number
  isActive: boolean
}

const BannersTab = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<BannerForm | null>(null)
  const [form] = Form.useForm<BannerForm>()
  const list = useQuery({ queryKey: ['banners'], queryFn: marketingApi.banners })
  const save = useMutation({
    mutationFn: (v: BannerForm) => marketingApi.saveBanner({
      id: editing?.id ?? null, position: v.position, title: v.title, imageUrl: v.imageUrl, link: v.link, categoryId: null,
      startAt: v.period[0].toISOString(), endAt: v.period[1].toISOString(), sortOrder: v.sortOrder ?? 0, isActive: v.isActive,
    }),
    onSuccess: (r) => { message.success(r.message); setEditing(null); void queryClient.invalidateQueries({ queryKey: ['banners'] }) },
    onError: (e) => message.error(errorText(e, 'Không lưu được banner.')),
  })
  const edit = (b?: Banner) => {
    const value: BannerForm = b
      ? { id: b.id, position: b.position, title: b.title, imageUrl: b.imageUrl, link: b.link, period: [dayjs(b.startAt), dayjs(b.endAt)], sortOrder: b.sortOrder, isActive: b.isActive }
      : { id: null, position: 'HomeMain', title: '', imageUrl: '', link: '/', period: [dayjs(), dayjs().add(30, 'day')], sortOrder: 0, isActive: true }
    setEditing(value)
    form.setFieldsValue(value)
  }
  return (
    <>
      <Button type="primary" onClick={() => edit()} style={{ marginBottom: 12 }}>Thêm banner</Button>
      <Table<Banner> rowKey="id" loading={list.isLoading} dataSource={list.data ?? []} pagination={{ pageSize: 20 }}
        columns={[
          { title: 'Vị trí', dataIndex: 'position', render: (p: BannerPosition) => POSITION_LABEL[p] },
          { title: 'Tiêu đề', dataIndex: 'title' },
          { title: 'Liên kết', dataIndex: 'link' },
          { title: 'Thời gian', render: (_, b) => `${formatDateTime(b.startAt)} – ${formatDateTime(b.endAt)}` },
          { title: '', dataIndex: 'isActive', render: (a: boolean) => (a ? <Tag color="green">Bật</Tag> : <Tag>Tắt</Tag>) },
          { title: '', render: (_, b) => <Button size="small" onClick={() => edit(b)}>Sửa</Button> },
        ]} />
      <Modal title={editing?.id ? 'Sửa banner' : 'Thêm banner'} open={!!editing} onCancel={() => setEditing(null)} onOk={() => form.submit()} okText="Lưu"
        confirmLoading={save.isPending} destroyOnClose>
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)}>
          <Form.Item name="position" label="Vị trí">
            <Select disabled={!!editing?.id} options={(Object.keys(POSITION_LABEL) as BannerPosition[]).map((p) => ({ value: p, label: POSITION_LABEL[p] }))} />
          </Form.Item>
          <Form.Item name="title" label="Tiêu đề" rules={[{ required: true, message: 'Nhập tiêu đề.' }]}><Input maxLength={120} /></Form.Item>
          <Form.Item name="imageUrl" label="Ảnh (URL) hoặc biểu tượng emoji cho lối tắt" rules={[{ required: true, message: 'Nhập ảnh.' }]}><Input /></Form.Item>
          <Form.Item name="link" label="Liên kết (/… hoặc https://…)" rules={[{ required: true, message: 'Nhập liên kết.' }]}><Input /></Form.Item>
          <Form.Item name="period" label="Thời gian"><DatePicker.RangePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} /></Form.Item>
          <Space>
            <Form.Item name="sortOrder" label="Thứ tự"><InputNumber /></Form.Item>
            <Form.Item name="isActive" label="Bật" valuePropName="checked"><Switch /></Form.Item>
          </Space>
        </Form>
      </Modal>
    </>
  )
}

const CampaignsTab = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<Campaign | null>(null)
  const [blocks, setBlocks] = useState('')
  const [form] = Form.useForm<{ name: string; slug: string; period: [Dayjs, Dayjs]; isActive: boolean }>()
  const list = useQuery({ queryKey: ['campaigns'], queryFn: marketingApi.campaigns })
  const save = useMutation({
    mutationFn: (v: { name: string; slug: string; period: [Dayjs, Dayjs]; isActive: boolean }) => {
      let parsed: CampaignBlock[]
      try {
        parsed = JSON.parse(blocks) as CampaignBlock[]
      } catch {
        throw new ApiError(400, 'Danh sách khối không phải JSON hợp lệ.')
      }
      return marketingApi.saveCampaign({ id: editing?.id || null, name: v.name, slug: v.slug, startAt: v.period[0].toISOString(), endAt: v.period[1].toISOString(), blocks: parsed, isActive: v.isActive })
    },
    onSuccess: (r) => { message.success(r.message); setEditing(null); void queryClient.invalidateQueries({ queryKey: ['campaigns'] }) },
    onError: (e) => message.error(errorText(e, 'Không lưu được chiến dịch.')),
  })
  const edit = (c?: Campaign) => {
    const value = c ?? { id: '', name: '', slug: '', startAt: dayjs().toISOString(), endAt: dayjs().add(14, 'day').toISOString(), isActive: true,
      blocks: [{ type: 'FlashSale', title: 'Flash Sale', imageUrl: null, link: null, voucherCodes: null, keyword: null, categoryId: null, maxPrice: null, limit: null }] as CampaignBlock[] }
    setEditing(value)
    setBlocks(JSON.stringify(value.blocks, null, 2))
    form.setFieldsValue({ name: value.name, slug: value.slug, period: [dayjs(value.startAt), dayjs(value.endAt)], isActive: value.isActive })
  }
  return (
    <>
      <Button type="primary" onClick={() => edit()} style={{ marginBottom: 12 }}>Tạo chiến dịch</Button>
      <Table<Campaign> rowKey="id" loading={list.isLoading} dataSource={list.data ?? []} pagination={false}
        columns={[
          { title: 'Tên', dataIndex: 'name' },
          { title: 'Trang', dataIndex: 'slug', render: (s: string) => <a href={`/su-kien/${s}`} target="_blank" rel="noreferrer">/su-kien/{s}</a> },
          { title: 'Thời gian', render: (_, c) => `${formatDateTime(c.startAt)} – ${formatDateTime(c.endAt)}` },
          { title: 'Khối', render: (_, c) => c.blocks.length },
          { title: '', render: (_, c) => <Button size="small" onClick={() => edit(c)}>Sửa</Button> },
        ]} />
      <Modal title="Chiến dịch" open={!!editing} onCancel={() => setEditing(null)} onOk={() => form.submit()} okText="Lưu" width={760} confirmLoading={save.isPending} destroyOnClose>
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)}>
          <Form.Item name="name" label="Tên" rules={[{ required: true, message: 'Nhập tên.' }]}><Input /></Form.Item>
          <Form.Item name="slug" label="Đường dẫn (/su-kien/…)" rules={[{ required: true, pattern: /^[a-z0-9]+(-[a-z0-9]+)*$/, message: 'Chữ thường không dấu, số, gạch ngang.' }]}><Input /></Form.Item>
          <Form.Item name="period" label="Thời gian"><DatePicker.RangePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="isActive" label="Bật" valuePropName="checked"><Switch /></Form.Item>
          <Typography.Text type="secondary">Khối (JSON): Banner (title, imageUrl, link) · Vouchers (voucherCodes) · FlashSale · Products (keyword, categoryId, maxPrice, limit)</Typography.Text>
          <Input.TextArea rows={12} value={blocks} onChange={(e) => setBlocks(e.target.value)} style={{ fontFamily: 'monospace' }} />
        </Form>
      </Modal>
    </>
  )
}

/** VI.6 Marketing của sàn: khung Flash Sale & duyệt đăng ký, banner / lối tắt / popup, chiến dịch. */
const MarketingPage = () => (
  <Card title="Marketing của sàn">
    <Tabs destroyInactiveTabPane items={[
      { key: 'flash', label: 'Flash Sale', children: <FlashTab /> },
      { key: 'banners', label: 'Banner & lối tắt', children: <BannersTab /> },
      { key: 'campaigns', label: 'Chiến dịch', children: <CampaignsTab /> },
    ]} />
  </Card>
)

export default MarketingPage
