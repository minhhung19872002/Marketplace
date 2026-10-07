import { useState } from 'react'
import { App, Button, Card, DatePicker, Form, Input, InputNumber, Modal, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { POSITION_LABEL, SEGMENT_LABEL, marketingApi, type Broadcast, type BroadcastSegment, type Banner, type BannerPosition, type Campaign, type CampaignBlock, type CampaignRegistration, type RegistrationStatus, type FlashItem, type FlashSlot } from '../api/marketing'
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
  const [reviewing, setReviewing] = useState<Campaign | null>(null)
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
          {
            title: '',
            render: (_, c) => (
              <Space>
                <Button size="small" onClick={() => edit(c)}>Sửa</Button>
                {c.blocks.some((b) => b.type === 'Registered') && (
                  <Button size="small" onClick={() => setReviewing(c)} data-testid="campaign-registrations">Đăng ký của shop</Button>
                )}
              </Space>
            ),
          },
        ]} />
      {reviewing && <RegistrationsModal campaign={reviewing} onClose={() => setReviewing(null)} />}
      <Modal title="Chiến dịch" open={!!editing} onCancel={() => setEditing(null)} onOk={() => form.submit()} okText="Lưu" width={760} confirmLoading={save.isPending} destroyOnClose>
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)}>
          <Form.Item name="name" label="Tên" rules={[{ required: true, message: 'Nhập tên.' }]}><Input /></Form.Item>
          <Form.Item name="slug" label="Đường dẫn (/su-kien/…)" rules={[{ required: true, pattern: /^[a-z0-9]+(-[a-z0-9]+)*$/, message: 'Chữ thường không dấu, số, gạch ngang.' }]}><Input /></Form.Item>
          <Form.Item name="period" label="Thời gian"><DatePicker.RangePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="isActive" label="Bật" valuePropName="checked"><Switch /></Form.Item>
          <Typography.Text type="secondary">
            Khối (JSON): Banner (title, imageUrl, link) · Vouchers (voucherCodes) · FlashSale · Products (keyword, categoryId, maxPrice, limit) ·
            Registered (title, limit — sản phẩm shop đăng ký và được duyệt; có khối này thì shop đăng ký được)
          </Typography.Text>
          <Input.TextArea rows={12} value={blocks} onChange={(e) => setBlocks(e.target.value)} style={{ fontFamily: 'monospace' }} />
        </Form>
      </Modal>
    </>
  )
}

/** Đăng ký của shop cho một chiến dịch: duyệt / từ chối (kèm lý do) từng sản phẩm hoặc nhiều cái một lần. */
const RegistrationsModal = ({ campaign, onClose }: { campaign: Campaign; onClose: () => void }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<RegistrationStatus>('Pending')
  const [selected, setSelected] = useState<string[]>([])
  const [reason, setReason] = useState('')
  const list = useQuery({ queryKey: ['registrations', campaign.id, status], queryFn: () => marketingApi.registrations(campaign.id, status) })
  const decide = useMutation({
    mutationFn: (approve: boolean) => marketingApi.decideRegistrations(campaign.id, { registrationIds: selected, approve, reason: approve ? null : reason }),
    onSuccess: (r) => { message.success(r.message); setSelected([]); void queryClient.invalidateQueries({ queryKey: ['registrations', campaign.id] }) },
    onError: (e) => message.error(errorText(e, 'Không lưu được.')),
  })
  return (
    <Modal title={`Đăng ký sản phẩm — ${campaign.name}`} open onCancel={onClose} footer={null} width={900}>
      <Space style={{ marginBottom: 12 }} wrap>
        <Select value={status} onChange={(v) => { setStatus(v); setSelected([]) }} style={{ width: 160 }}
          options={[{ value: 'Pending', label: 'Chờ duyệt' }, { value: 'Approved', label: 'Đã duyệt' }, { value: 'Rejected', label: 'Từ chối' }]} />
        <Button type="primary" disabled={!selected.length} loading={decide.isPending} onClick={() => decide.mutate(true)} data-testid="registrations-approve">
          Duyệt ({selected.length})
        </Button>
        <Input placeholder="Lý do từ chối" value={reason} onChange={(e) => setReason(e.target.value)} style={{ width: 240 }} />
        <Button danger disabled={!selected.length || !reason.trim()} loading={decide.isPending} onClick={() => decide.mutate(false)}>Từ chối</Button>
      </Space>
      <Table<CampaignRegistration> rowKey="id" size="small" loading={list.isLoading} dataSource={list.data?.items ?? []} pagination={false}
        rowSelection={{ selectedRowKeys: selected, onChange: (k) => setSelected(k as string[]) }}
        columns={[
          { title: 'Sản phẩm', render: (_, r) => <Space>{r.imageUrl && <img src={r.imageUrl} alt="" width={40} height={40} style={{ objectFit: 'cover' }} />}{r.productName}</Space> },
          { title: 'Shop', dataIndex: 'shopName' },
          { title: 'Giá từ', dataIndex: 'minPrice', render: (v: number) => formatPrice(v) },
          { title: 'Gửi lúc', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
          { title: 'Lý do', dataIndex: 'rejectReason' },
        ]} />
    </Modal>
  )
}

const BroadcastsTab = () => {
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<{ title: string; body: string; link?: string; segment: BroadcastSegment }>()
  const list = useQuery({ queryKey: ['broadcasts'], queryFn: marketingApi.broadcasts })
  const send = useMutation({
    mutationFn: (v: { title: string; body: string; link?: string; segment: BroadcastSegment }) =>
      marketingApi.sendBroadcast({ title: v.title, body: v.body, link: v.link || null, segment: v.segment }),
    onSuccess: (r) => {
      message.success(`${r.message} ${r.data.recipients} người nhận, ${r.data.skippedToday} người bỏ qua (đã nhận đủ trong ngày).`)
      form.resetFields()
      void queryClient.invalidateQueries({ queryKey: ['broadcasts'] })
    },
    onError: (e) => message.error(errorText(e, 'Không gửi được thông báo.')),
  })
  return (
    <>
      <Form form={form} layout="vertical" style={{ maxWidth: 640 }} initialValues={{ segment: 'Everyone' }}
        onFinish={(v) => modal.confirm({ title: `Gửi tới "${SEGMENT_LABEL[v.segment]}"?`, content: 'Thông báo khuyến mãi chỉ gửi cho người đã bật loại thông báo này.', onOk: () => send.mutateAsync(v) })}>
        <Form.Item name="segment" label="Nhóm người nhận">
          <Select options={(Object.keys(SEGMENT_LABEL) as BroadcastSegment[]).map((k) => ({ value: k, label: SEGMENT_LABEL[k] }))} />
        </Form.Item>
        <Form.Item name="title" label="Tiêu đề" rules={[{ required: true, max: 200, message: 'Nhập tiêu đề (tối đa 200 ký tự).' }]}><Input /></Form.Item>
        <Form.Item name="body" label="Nội dung" rules={[{ required: true, max: 1000, message: 'Nhập nội dung (tối đa 1000 ký tự).' }]}><Input.TextArea rows={3} /></Form.Item>
        <Form.Item name="link" label="Liên kết (tùy chọn)"><Input placeholder="/su-kien/sale-10-10" /></Form.Item>
        <Button type="primary" htmlType="submit" loading={send.isPending}>Gửi thông báo</Button>
      </Form>
      <Table<Broadcast> rowKey="id" style={{ marginTop: 16 }} loading={list.isLoading} dataSource={list.data ?? []} pagination={false}
        columns={[
          { title: 'Thời gian', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
          { title: 'Tiêu đề', dataIndex: 'title' },
          { title: 'Nhóm', dataIndex: 'segment', render: (v: BroadcastSegment) => <Tag>{SEGMENT_LABEL[v]}</Tag> },
          { title: 'Người nhận', dataIndex: 'recipients' },
          { title: 'Bỏ qua (giới hạn ngày)', dataIndex: 'skippedToday' },
        ]} />
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
      { key: 'broadcasts', label: 'Thông báo đẩy', children: <BroadcastsTab /> },
    ]} />
  </Card>
)

export default MarketingPage
