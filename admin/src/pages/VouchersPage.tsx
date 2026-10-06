import { useState } from 'react'
import { App, Button, Card, DatePicker, Form, Input, InputNumber, Modal, Select, Space, Switch, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { promoApi, type PlatformVoucher, type VoucherType } from '../api/promo'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'

const vnd = (v: number) => `₫${new Intl.NumberFormat('vi-VN').format(v)}`

const TYPES: { value: VoucherType; label: string }[] = [
  { value: 'Amount', label: 'Giảm tiền' },
  { value: 'Percent', label: 'Giảm % (có trần)' },
  { value: 'FreeShipping', label: 'Miễn phí vận chuyển' },
  { value: 'CoinCashback', label: 'Hoàn xu (% có trần)' },
]

const describe = (v: PlatformVoucher) =>
  v.type === 'Amount' ? `Giảm ${vnd(v.discountValue)}`
    : v.type === 'FreeShipping' ? `Freeship tối đa ${vnd(v.maxDiscount ?? 0)}`
    : `${v.type === 'Percent' ? 'Giảm' : 'Hoàn xu'} ${v.discountPercentBp / 100}% tối đa ${vnd(v.maxDiscount ?? 0)}`

interface FormValues {
  code: string
  name: string
  type: VoucherType
  discountValue?: number
  percent?: number
  maxDiscount?: number
  minOrder: number
  period: [Dayjs, Dayjs]
  totalQuota?: number
  perUserLimit: number
  isPublic: boolean
  newBuyer: boolean
  xtraOnly: boolean
}

/** Voucher của sàn — sàn chịu phần giảm (spec 3.6). */
const VouchersPage = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [q, setQ] = useState('')
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm<FormValues>()
  const list = useQuery({ queryKey: ['admin-vouchers', q], queryFn: () => promoApi.vouchers(q) })

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['admin-vouchers'] })
  const save = useMutation({
    mutationFn: promoApi.createVoucher,
    onSuccess: (r) => {
      message.success(r.message)
      setOpen(false)
      form.resetFields()
      refresh()
    },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không lưu được voucher.'),
  })
  const stop = useMutation({
    mutationFn: promoApi.stopVoucher,
    onSuccess: (r) => {
      message.success(r.message)
      refresh()
    },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không dừng được voucher.'),
  })

  const submit = (v: FormValues) =>
    save.mutate({
      code: v.code.trim().toUpperCase(),
      name: v.name.trim(),
      type: v.type,
      discountValue: v.type === 'Amount' ? v.discountValue ?? 0 : 0,
      discountPercentBp: v.type === 'Percent' || v.type === 'CoinCashback' ? Math.round((v.percent ?? 0) * 100) : 0,
      maxDiscount: v.type === 'Amount' ? null : v.maxDiscount ?? null,
      minOrder: v.minOrder ?? 0,
      audience: v.newBuyer ? 'NewBuyer' : 'Everyone',
      categoryIds: [],
      productIds: [],
      startAt: v.period[0].toISOString(),
      endAt: v.period[1].toISOString(),
      totalQuota: v.totalQuota ?? null,
      perUserLimit: v.perUserLimit,
      isPublic: v.isPublic,
      channel: 'All',
      xtraOnly: v.xtraOnly,
    })

  return (
    <Card title="Voucher của sàn" extra={
      <Space>
        <Input.Search placeholder="Tìm mã / tên" allowClear onSearch={setQ} style={{ width: 220 }} />
        <Button type="primary" onClick={() => setOpen(true)}>Tạo voucher</Button>
      </Space>
    }>
      <Table<PlatformVoucher>
        rowKey="id"
        loading={list.isLoading}
        dataSource={list.data?.items ?? []}
        pagination={false}
        columns={[
          { title: 'Mã', dataIndex: 'code', render: (c: string) => <Typography.Text strong>{c}</Typography.Text> },
          { title: 'Tên', dataIndex: 'name' },
          { title: 'Ưu đãi', render: (_, v) => describe(v) },
          { title: 'Đơn tối thiểu', dataIndex: 'minOrder', render: (m: number) => vnd(m) },
          { title: 'Thời gian', render: (_, v) => `${formatDateTime(v.startAt)} – ${formatDateTime(v.endAt)}` },
          { title: 'Đã dùng', render: (_, v) => `${v.usedCount}${v.totalQuota ? ` / ${v.totalQuota}` : ''}` },
          { title: 'Trạng thái', dataIndex: 'state', render: (s: string) => <Tag color={s === 'Đang diễn ra' ? 'green' : 'default'}>{s}</Tag> },
          {
            title: '',
            render: (_, v) => v.isActive && v.state !== 'Đã kết thúc' && (
              <Button size="small" danger onClick={() => stop.mutate(v.id)}>Dừng</Button>
            ),
          },
        ]}
      />
      <Modal title="Tạo voucher của sàn" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} okText="Lưu" confirmLoading={save.isPending} destroyOnClose>
        <Form<FormValues> form={form} layout="vertical" onFinish={submit}
          initialValues={{ type: 'Amount', minOrder: 0, perUserLimit: 1, isPublic: true, newBuyer: false, xtraOnly: false, period: [dayjs(), dayjs().add(30, 'day')] }}>
          <Form.Item name="code" label="Mã" rules={[{ required: true, message: 'Vui lòng nhập mã.' }, { pattern: /^[A-Za-z0-9]{3,20}$/, message: '3–20 chữ cái hoặc số.' }]}>
            <Input />
          </Form.Item>
          <Form.Item name="name" label="Tên" rules={[{ required: true, message: 'Vui lòng nhập tên.' }]}><Input maxLength={100} /></Form.Item>
          <Form.Item name="type" label="Loại"><Select options={TYPES} /></Form.Item>
          <Form.Item noStyle shouldUpdate={(a, b) => a.type !== b.type}>
            {({ getFieldValue }) => {
              const type = getFieldValue('type') as VoucherType
              return (
                <Space>
                  {type === 'Amount' && <Form.Item name="discountValue" label="Số tiền giảm (₫)" rules={[{ required: true, message: 'Bắt buộc.' }]}><InputNumber min={1000} step={1000} /></Form.Item>}
                  {(type === 'Percent' || type === 'CoinCashback') && <Form.Item name="percent" label="%" rules={[{ required: true, message: 'Bắt buộc.' }]}><InputNumber min={1} max={100} /></Form.Item>}
                  {type !== 'Amount' && <Form.Item name="maxDiscount" label={type === 'CoinCashback' ? 'Tối đa (xu)' : 'Tối đa (₫)'} rules={[{ required: true, message: 'Bắt buộc.' }]}><InputNumber min={1000} step={1000} /></Form.Item>}
                </Space>
              )
            }}
          </Form.Item>
          <Form.Item name="minOrder" label="Đơn tối thiểu (₫)"><InputNumber min={0} step={1000} style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="period" label="Thời gian" rules={[{ required: true, message: 'Chọn thời gian.' }]}>
            <DatePicker.RangePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} />
          </Form.Item>
          <Space>
            <Form.Item name="totalQuota" label="Tổng lượt"><InputNumber min={1} placeholder="Không giới hạn" /></Form.Item>
            <Form.Item name="perUserLimit" label="Lượt mỗi người"><InputNumber min={1} max={100} /></Form.Item>
          </Space>
          <Space size="large">
            <Form.Item name="isPublic" label="Công khai" valuePropName="checked"><Switch /></Form.Item>
            <Form.Item name="xtraOnly" label="Chỉ shop tham gia Xtra" valuePropName="checked"
              tooltip="Miễn phí vận chuyển → chỉ shop Freeship Xtra; loại khác → chỉ shop Voucher Xtra">
              <Switch data-testid="voucher-xtra" />
            </Form.Item>
            <Form.Item name="newBuyer" label="Chỉ khách mới" valuePropName="checked"><Switch /></Form.Item>
          </Space>
        </Form>
      </Modal>
    </Card>
  )
}

export default VouchersPage
