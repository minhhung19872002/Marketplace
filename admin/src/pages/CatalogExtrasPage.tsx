import { useState } from 'react'
import { App, Button, Card, Form, Input, Modal, Segmented, Space, Switch, Table, Tabs } from 'antd'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { REPORT_REASON_LABEL, platformApi, type Brand, type ProductReport } from '../api/platform'
import { P, can } from '../permissions'
import { formatDateTime } from '../lib/datetime'
import { ToneTag } from '../components/StatusTag'

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : fallback)

const BrandsTab = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<Brand | null>(null)
  const [form] = Form.useForm<{ name: string; logoUrl?: string; isVerified: boolean }>()
  const list = useQuery({ queryKey: ['brands', q, page], queryFn: () => platformApi.brands(q, page), placeholderData: keepPreviousData })
  const save = useMutation({
    mutationFn: (v: { name: string; logoUrl?: string; isVerified: boolean }) => platformApi.updateBrand(editing!.id, { ...v, logoUrl: v.logoUrl || null }),
    onSuccess: (r) => { message.success(r.message); setEditing(null); void queryClient.invalidateQueries({ queryKey: ['brands'] }) },
    onError: (e) => message.error(errorText(e, 'Không lưu được thương hiệu.')),
  })
  return (
    <>
      <Input.Search allowClear placeholder="Tìm thương hiệu" style={{ width: 300, marginBottom: 12 }} onSearch={(v) => { setQ(v); setPage(1) }} />
      <Table<Brand> rowKey="id" loading={list.isLoading} dataSource={list.data?.items ?? []}
        pagination={{ current: page, pageSize: 50, total: list.data?.totalCount ?? 0, onChange: setPage }}
        columns={[
          { title: 'Thương hiệu', dataIndex: 'name' },
          { title: 'Chính hãng', dataIndex: 'isVerified', render: (v: boolean) => (v ? <ToneTag tone="success">Đã xác minh</ToneTag> : null) },
          { title: 'Số sản phẩm', dataIndex: 'productCount', align: 'right' },
          {
            title: '', key: 'x',
            render: (_, b) => <Button size="small" onClick={() => { setEditing(b); form.setFieldsValue({ name: b.name, logoUrl: b.logoUrl ?? undefined, isVerified: b.isVerified }) }}>Sửa</Button>,
          },
        ]} />
      <Modal open={!!editing} title="Thương hiệu" okText="Lưu" confirmLoading={save.isPending} onCancel={() => setEditing(null)} onOk={() => form.submit()} destroyOnClose>
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)}>
          <Form.Item name="name" label="Tên" rules={[{ required: true, message: 'Nhập tên.' }]}><Input /></Form.Item>
          <Form.Item name="logoUrl" label="Logo (https:// hoặc /...)"><Input /></Form.Item>
          <Form.Item name="isVerified" label="Đã xác minh chính hãng" valuePropName="checked"><Switch /></Form.Item>
        </Form>
      </Modal>
    </>
  )
}

const ReportsTab = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<string>('Open')
  const [page, setPage] = useState(1)
  const [banning, setBanning] = useState<ProductReport | null>(null)
  const [reason, setReason] = useState('')
  const list = useQuery({ queryKey: ['product-reports', status, page], queryFn: () => platformApi.productReports(status, page), placeholderData: keepPreviousData })
  const resolve = useMutation({
    mutationFn: (v: { id: string; ban: boolean; resolution: string | null }) => platformApi.resolveReport(v.id, v.ban, v.resolution),
    onSuccess: (r) => { message.success(r.message); setBanning(null); setReason(''); void queryClient.invalidateQueries({ queryKey: ['product-reports'] }) },
    onError: (e) => message.error(errorText(e, 'Không xử lý được.')),
  })
  return (
    <>
      <Segmented value={status} onChange={(v) => { setStatus(String(v)); setPage(1) }} style={{ marginBottom: 12 }}
        options={[{ value: 'Open', label: 'Chờ xử lý' }, { value: 'Banned', label: 'Đã khoá' }, { value: 'Dismissed', label: 'Đã bỏ qua' }]} />
      <Table<ProductReport> rowKey="id" loading={list.isLoading} dataSource={list.data?.items ?? []}
        pagination={{ current: page, pageSize: 20, total: list.data?.totalCount ?? 0, onChange: setPage }}
        columns={[
          { title: 'Sản phẩm', render: (_, r) => <a href={`/san-pham/${r.productId}`} target="_blank" rel="noreferrer">{r.productName}</a> },
          { title: 'Shop', dataIndex: 'shopName' },
          { title: 'Lý do', render: (_, r) => <span>{REPORT_REASON_LABEL[r.reason]}{r.details ? ` — ${r.details}` : ''}</span> },
          { title: 'Người báo', dataIndex: 'reporterName' },
          { title: 'Báo cáo đang mở', dataIndex: 'openReportsOnProduct', align: 'right' },
          { title: 'Lúc', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
          {
            title: '', key: 'x',
            render: (_, r) => r.status === 'Open' ? (
              <Space>
                <Button size="small" danger onClick={() => setBanning(r)}>Khoá sản phẩm</Button>
                <Button size="small" onClick={() => resolve.mutate({ id: r.id, ban: false, resolution: null })}>Bỏ qua</Button>
              </Space>
            ) : r.resolution,
          },
        ]} />
      <Modal open={!!banning} title={`Khoá "${banning?.productName}"`} okText="Khoá" okButtonProps={{ danger: true, disabled: !reason.trim() }}
        confirmLoading={resolve.isPending} onCancel={() => setBanning(null)} onOk={() => resolve.mutate({ id: banning!.id, ban: true, resolution: reason })}>
        <Input.TextArea rows={3} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Lý do khoá (gửi cho shop)" />
      </Modal>
    </>
  )
}

/** VI.4: thương hiệu và báo cáo sản phẩm vi phạm của người mua. */
const CatalogExtrasPage = ({ permissions }: { permissions: string[] }) => (
  <Card title="Thương hiệu & báo cáo vi phạm">
    <Tabs items={[
      ...(can(permissions, P.ProductBan) ? [{ key: 'reports', label: 'Báo cáo sản phẩm', children: <ReportsTab /> }] : []),
      ...(can(permissions, P.BrandManage) ? [{ key: 'brands', label: 'Thương hiệu', children: <BrandsTab /> }] : []),
    ]} />
  </Card>
)

export default CatalogExtrasPage
