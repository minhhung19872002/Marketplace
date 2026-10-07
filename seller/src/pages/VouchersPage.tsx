import { useState } from 'react'
import { App, Button, Card, DatePicker, Form, Input, InputNumber, Modal, Radio, Space, Switch, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { voucherApi, type Voucher, type VoucherInput } from '../api/vouchers'
import { ApiError } from '../api/http'
import { formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'

interface FormValues {
  code: string
  name: string
  type: 'Amount' | 'Percent'
  discountValue?: number
  percent?: number
  maxDiscount?: number
  minOrder: number
  period: [Dayjs, Dayjs]
  totalQuota?: number
  perUserLimit: number
  isPublic: boolean
  followersOnly: boolean
}

const describe = (v: Voucher) =>
  v.type === 'Amount' ? `Giảm ${formatPrice(v.discountValue)}` : `Giảm ${v.discountPercentBp / 100}% tối đa ${formatPrice(v.maxDiscount ?? 0)}`

const stateColor: Record<string, string> = { 'Đang diễn ra': 'green', 'Sắp diễn ra': 'blue', 'Hết lượt': 'orange' }

/** Kênh Marketing → Mã giảm giá của shop (≤ 1 mã mỗi đơn, shop chịu phần giảm). */
const VouchersPage = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm<FormValues>()
  const list = useQuery({ queryKey: ['vouchers', shopId], queryFn: () => voucherApi.list(shopId) })

  const save = useMutation({
    mutationFn: (input: VoucherInput) => voucherApi.create(shopId, input),
    onSuccess: (r) => {
      message.success(r.message)
      setOpen(false)
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

  const submit = (v: FormValues) =>
    save.mutate({
      code: v.code.trim().toUpperCase(),
      name: v.name.trim(),
      type: v.type,
      discountValue: v.type === 'Amount' ? v.discountValue ?? 0 : 0,
      discountPercentBp: v.type === 'Percent' ? Math.round((v.percent ?? 0) * 100) : 0,
      maxDiscount: v.type === 'Percent' ? v.maxDiscount ?? null : null,
      minOrder: v.minOrder ?? 0,
      audience: v.followersOnly ? 'ShopFollowers' : 'Everyone',
      categoryIds: [],
      productIds: [],
      startAt: v.period[0].toISOString(),
      endAt: v.period[1].toISOString(),
      totalQuota: v.totalQuota ?? null,
      perUserLimit: v.perUserLimit,
      isPublic: v.isPublic,
      channel: 'All',
    })

  return (
    <Card
      title="Mã giảm giá của shop"
      extra={<Button type="primary" onClick={() => setOpen(true)} data-testid="voucher-new">Tạo mã giảm giá</Button>}
    >
      <Table<Voucher>
        rowKey="id"
        loading={list.isLoading}
        dataSource={list.data?.items ?? []}
        pagination={false}
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
              <Button size="small" danger loading={stop.isPending} onClick={() => stop.mutate(v.id)}>Dừng</Button>
            ),
          },
        ]}
      />

      <Modal title="Tạo mã giảm giá" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} okText="Lưu" confirmLoading={save.isPending} destroyOnClose>
        <Form<FormValues>
          form={form}
          layout="vertical"
          onFinish={submit}
          initialValues={{ type: 'Amount', minOrder: 0, perUserLimit: 1, isPublic: true, followersOnly: false, period: [dayjs(), dayjs().add(30, 'day')] }}
        >
          <Form.Item name="code" label="Mã voucher" rules={[{ required: true, message: 'Vui lòng nhập mã.' }, { pattern: /^[A-Za-z0-9]{3,20}$/, message: '3–20 chữ cái hoặc số, không dấu.' }]}>
            <Input placeholder="VD: SHOPGIAM20K" />
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
          <Form.Item name="period" label="Thời gian sử dụng" rules={[{ required: true, message: 'Chọn thời gian.' }]}>
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
