import { useState } from 'react'
import { App, Button, Card, Checkbox, DatePicker, Form, Input, InputNumber, Modal, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import { CloseOutlined, EditOutlined, ExportOutlined, PlusOutlined, TeamOutlined } from '@ant-design/icons'
import { useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { POSITION_LABEL, SEGMENT_LABEL, marketingApi, type Broadcast, type BroadcastSegment, type Banner, type BannerPosition, type Campaign, type CampaignBlock, type CampaignRegistration, type RegistrationStatus, type FlashItem, type FlashSlot } from '../api/marketing'
import { ApiError } from '../api/http'
import { formatNumber, formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'
import CategoryPicker from '../components/CategoryPicker'
import DataTable from '../components/DataTable'
import PageHeader from '../components/PageHeader'
import RowActions from '../components/RowActions'
import StatusTag from '../components/StatusTag'
import { APPROVAL_STATUS, ON_OFF, SCHEDULE_STATE } from '../lib/status'

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : fallback)

const FlashTab = () => {
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm<{ date: Dayjs; hour: number; minDiscount: number; minRating: number; categoryIds: string[] }>()
  const slots = useQuery({ queryKey: ['flash-slots'], queryFn: marketingApi.slots })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['flash-slots'] })
  const create = useMutation({
    mutationFn: (v: { date: Dayjs; hour: number; minDiscount: number; minRating: number; categoryIds: string[] }) => marketingApi.createSlot({
      date: v.date.format('YYYY-MM-DD'), hour: v.hour, minDiscountBp: Math.round((v.minDiscount ?? 0) * 100), minRating: v.minRating ?? 0,
      // Ngành hàng tiêu chí (3.10, L138): a product of a sub-category of a chosen one qualifies
      categoryIds: v.categoryIds ?? [],
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
      <DataTable<FlashSlot> rowKey="id" loading={slots.isPending} error={slots.error} dataSource={slots.data ?? []} paging="client"
        actions={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => setOpen(true)}>Mở khung Flash Sale</Button>}
        emptyText="Chưa có khung Flash Sale nào"
        expandable={{
          expandedRowRender: (s) => (
            <Table<FlashItem> size="small" rowKey="id" pagination={false} dataSource={s.items} columns={[
              { title: 'Phân loại', render: (_, i) => `${i.productName}${i.variant ? ` - ${i.variant}` : ''}` },
              { title: 'Giá', render: (_, i) => `${formatPrice(i.flashPrice)} (gốc ${formatPrice(i.basePrice)})` },
              { title: 'Suất', render: (_, i) => `${i.sold}/${i.quota} · ${i.perUserLimit}/người` },
              { title: 'Trạng thái', render: (_, i) => <StatusTag map={APPROVAL_STATUS} value={i.status} /> },
              {
                title: '', align: 'right', render: (_, i) => i.status === 'Pending' && (
                  <RowActions name={i.productName}
                    primary={<Button size="small" type="primary" onClick={() => approve.mutate(i.id)}>Duyệt</Button>}
                    // Opens a dialog with the reason box: that is the confirmation
                    items={[{ key: 'reject', icon: <CloseOutlined aria-hidden />, label: 'Từ chối', danger: true, onClick: () => reject(i) }]} />
                ),
              },
            ]} />
          ),
        }}
        columns={[
          { title: 'Khung giờ', render: (_, s) => `${formatDateTime(s.startAt)} – ${formatDateTime(s.endAt)}` },
          { title: 'Tiêu chí', render: (_, s) => `Giảm ≥ ${s.minDiscountBp / 100}% · ≥ ${s.minRating} sao${s.categoryIds.length > 0 ? ` · ${s.categoryIds.length} ngành hàng` : ''}` },
          { title: 'Đăng ký', render: (_, s) => `${s.items.filter((i) => i.status === 'Pending').length} chờ / ${s.items.length}` },
          { title: 'Trạng thái', dataIndex: 'state', render: (v: string) => <StatusTag map={SCHEDULE_STATE} value={v} /> },
        ]} />
      <Modal title="Mở khung Flash Sale" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} okText="Mở khung" confirmLoading={create.isPending} destroyOnClose>
        <Form form={form} layout="vertical" onFinish={(v) => create.mutate(v)} initialValues={{ date: dayjs().add(1, 'day'), hour: 12, minDiscount: 10, minRating: 0, categoryIds: [] }}>
          <Form.Item name="date" label="Ngày"><DatePicker format="DD/MM/YYYY" /></Form.Item>
          <Form.Item name="hour" label="Giờ bắt đầu (theo FLASH.SLOT_HOURS)">
            <Select options={[0, 9, 12, 15, 21].map((h) => ({ value: h, label: `${String(h).padStart(2, '0')}:00` }))} />
          </Form.Item>
          <Space>
            <Form.Item name="minDiscount" label="Giảm tối thiểu (%)"><InputNumber min={0} max={90} /></Form.Item>
            <Form.Item name="minRating" label="Đánh giá tối thiểu"><InputNumber min={0} max={5} step={0.5} /></Form.Item>
          </Space>
          <Form.Item name="categoryIds" label="Ngành hàng" tooltip="Để trống: mọi ngành hàng"><CategoryPicker /></Form.Item>
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
  hasTextInImage: boolean
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
      hasTextInImage: v.hasTextInImage ?? false,
    }),
    onSuccess: (r) => { message.success(r.message); setEditing(null); void queryClient.invalidateQueries({ queryKey: ['banners'] }) },
    onError: (e) => message.error(errorText(e, 'Không lưu được banner.')),
  })
  const edit = (b?: Banner) => {
    const value: BannerForm = b
      ? { id: b.id, position: b.position, title: b.title, imageUrl: b.imageUrl, link: b.link, period: [dayjs(b.startAt), dayjs(b.endAt)], sortOrder: b.sortOrder, isActive: b.isActive, hasTextInImage: b.hasTextInImage ?? false }
      : { id: null, position: 'HomeMain', title: '', imageUrl: '', link: '/', period: [dayjs(), dayjs().add(30, 'day')], sortOrder: 0, isActive: true, hasTextInImage: false }
    setEditing(value)
    form.setFieldsValue(value)
  }
  return (
    <>
      <DataTable<Banner> rowKey="id" loading={list.isPending} error={list.error} dataSource={list.data ?? []} paging="client"
        actions={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => edit()}>Thêm banner</Button>}
        emptyText="Chưa có banner nào"
        columns={[
          { title: 'Vị trí', dataIndex: 'position', render: (p: BannerPosition) => POSITION_LABEL[p] },
          { title: 'Tiêu đề', dataIndex: 'title' },
          { title: 'Liên kết', dataIndex: 'link' },
          { title: 'Thời gian', render: (_, b) => `${formatDateTime(b.startAt)} – ${formatDateTime(b.endAt)}` },
          { title: 'Trạng thái', dataIndex: 'isActive', render: (a: boolean) => <StatusTag map={ON_OFF} value={a ? 'on' : 'off'} /> },
          { title: '', key: 'actions', align: 'right', render: (_, b) => <RowActions name={b.title} primary={<Button size="small" icon={<EditOutlined aria-hidden />} onClick={() => edit(b)}>Sửa</Button>} /> },
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
          <Form.Item name="hasTextInImage" valuePropName="checked" extra="Bật khi ảnh là banner thiết kế đã có chữ: trang người mua không vẽ tiêu đề đè lên ảnh.">
            <Checkbox>Ảnh đã có chữ</Checkbox>
          </Form.Item>
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
  const [form] = Form.useForm<{ name: string; slug: string; period: [Dayjs, Dayjs]; isActive: boolean; frameImageUrl: string }>()
  const list = useQuery({ queryKey: ['campaigns'], queryFn: marketingApi.campaigns })
  const save = useMutation({
    mutationFn: (v: { name: string; slug: string; period: [Dayjs, Dayjs]; isActive: boolean; frameImageUrl: string }) => {
      let parsed: CampaignBlock[]
      try {
        parsed = JSON.parse(blocks) as CampaignBlock[]
      } catch {
        throw new ApiError(400, 'Danh sách khối không phải JSON hợp lệ.')
      }
      return marketingApi.saveCampaign({ id: editing?.id || null, name: v.name, slug: v.slug, startAt: v.period[0].toISOString(), endAt: v.period[1].toISOString(), blocks: parsed, isActive: v.isActive,
        frameImageUrl: v.frameImageUrl?.trim() || null })
    },
    onSuccess: (r) => { message.success(r.message); setEditing(null); void queryClient.invalidateQueries({ queryKey: ['campaigns'] }) },
    onError: (e) => message.error(errorText(e, 'Không lưu được chiến dịch.')),
  })
  const edit = (c?: Campaign) => {
    const value = c ?? { id: '', name: '', slug: '', startAt: dayjs().toISOString(), endAt: dayjs().add(14, 'day').toISOString(), isActive: true, frameImageUrl: null,
      blocks: [{ type: 'FlashSale', title: 'Flash Sale', imageUrl: null, link: null, voucherCodes: null, keyword: null, categoryId: null, maxPrice: null, limit: null }] as CampaignBlock[] }
    setEditing(value)
    setBlocks(JSON.stringify(value.blocks, null, 2))
    form.setFieldsValue({ name: value.name, slug: value.slug, period: [dayjs(value.startAt), dayjs(value.endAt)], isActive: value.isActive,
      frameImageUrl: value.frameImageUrl ?? '' })
  }
  return (
    <>
      <DataTable<Campaign> rowKey="id" loading={list.isPending} error={list.error} dataSource={list.data ?? []} paging="client"
        actions={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => edit()}>Tạo chiến dịch</Button>}
        emptyText="Chưa có chiến dịch nào"
        columns={[
          { title: 'Tên', dataIndex: 'name', render: (v: string) => <span className="cell-main">{v}</span> },
          { title: 'Trạng thái', dataIndex: 'isActive', render: (a: boolean) => <StatusTag map={ON_OFF} value={a ? 'on' : 'off'} /> },
          { title: 'Trang', dataIndex: 'slug', render: (s: string) => <a href={`/su-kien/${s}`} target="_blank" rel="noreferrer">/su-kien/{s}</a> },
          { title: 'Thời gian', render: (_, c) => `${formatDateTime(c.startAt)} – ${formatDateTime(c.endAt)}` },
          { title: 'Khối', render: (_, c) => c.blocks.length },
          {
            title: '', key: 'actions', align: 'right',
            render: (_, c) => (
              <RowActions name={c.name}
                primary={<Button size="small" icon={<EditOutlined aria-hidden />} onClick={() => edit(c)}>Sửa</Button>}
                items={[
                  { key: 'registrations', icon: <TeamOutlined aria-hidden />, label: 'Đăng ký của shop', testId: 'campaign-registrations',
                    hidden: !c.blocks.some((b) => b.type === 'Registered'), onClick: () => setReviewing(c) },
                  { key: 'open', icon: <ExportOutlined aria-hidden />, label: 'Mở trang sự kiện', onClick: () => window.open(`/su-kien/${c.slug}`, '_blank', 'noopener') },
                ]} />
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
          <Form.Item name="frameImageUrl" label="Ảnh khung sản phẩm (tuỳ chọn)" extra="Ảnh PNG trong suốt phủ lên ảnh các sản phẩm được duyệt tham gia, chỉ khi chiến dịch đang chạy. Để trống = không khung."><Input placeholder="https://…/khung.png" /></Form.Item>
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
      <Form form={form} layout="vertical" style={{ maxWidth: 640, paddingTop: 4 }} initialValues={{ segment: 'Everyone' }}
        onFinish={(v) => modal.confirm({ title: `Gửi tới "${SEGMENT_LABEL[v.segment]}"?`, content: 'Thông báo khuyến mãi chỉ gửi cho người đã bật loại thông báo này.', onOk: () => send.mutateAsync(v) })}>
        <Form.Item name="segment" label="Nhóm người nhận">
          <Select options={(Object.keys(SEGMENT_LABEL) as BroadcastSegment[]).map((k) => ({ value: k, label: SEGMENT_LABEL[k] }))} />
        </Form.Item>
        <Form.Item name="title" label="Tiêu đề" rules={[{ required: true, max: 200, message: 'Nhập tiêu đề (tối đa 200 ký tự).' }]}><Input /></Form.Item>
        <Form.Item name="body" label="Nội dung" rules={[{ required: true, max: 1000, message: 'Nhập nội dung (tối đa 1000 ký tự).' }]}><Input.TextArea rows={3} /></Form.Item>
        <Form.Item name="link" label="Liên kết (tùy chọn)"><Input placeholder="/su-kien/sale-10-10" /></Form.Item>
        <Button type="primary" htmlType="submit" loading={send.isPending}>Gửi thông báo</Button>
      </Form>
      <Typography.Title level={5} style={{ marginTop: 24 }}>Đã gửi</Typography.Title>
      <DataTable<Broadcast> rowKey="id" loading={list.isPending} error={list.error} dataSource={list.data ?? []} paging="client"
        emptyText="Chưa gửi thông báo nào"
        columns={[
          { title: 'Thời gian', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
          { title: 'Tiêu đề', dataIndex: 'title' },
          { title: 'Nhóm', dataIndex: 'segment', render: (v: BroadcastSegment) => <Tag bordered={false}>{SEGMENT_LABEL[v]}</Tag> },
          { title: 'Người nhận', dataIndex: 'recipients', align: 'right', render: (v: number) => formatNumber(v) },
          { title: 'Bỏ qua (giới hạn ngày)', dataIndex: 'skippedToday', align: 'right', render: (v: number) => formatNumber(v) },
        ]} />
    </>
  )
}

const TABS = [
  { key: 'flash', label: 'Flash Sale', children: <FlashTab /> },
  { key: 'banners', label: 'Banner & lối tắt', children: <BannersTab /> },
  { key: 'campaigns', label: 'Chiến dịch', children: <CampaignsTab /> },
  { key: 'broadcasts', label: 'Thông báo đẩy', children: <BroadcastsTab /> },
]

/** VI.6 Marketing của sàn: khung Flash Sale & duyệt đăng ký, banner / lối tắt / popup, chiến dịch. The tab lives in the URL (?tab=). */
const MarketingPage = () => {
  const [params, setParams] = useSearchParams()
  const tab = TABS.find((t) => t.key === params.get('tab'))?.key ?? 'flash'
  return (
    <>
      <PageHeader title="Marketing" description="Khung Flash Sale và duyệt đăng ký của shop, banner và lối tắt trang chủ, chiến dịch, thông báo đẩy hàng loạt" />
      <Card className="tabs-card">
        <Tabs destroyInactiveTabPane activeKey={tab} onChange={(k) => setParams(k === 'flash' ? {} : { tab: k })} items={TABS} />
      </Card>
    </>
  )
}

export default MarketingPage
