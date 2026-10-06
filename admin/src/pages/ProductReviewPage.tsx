import { useState } from 'react'
import { Alert, App as AntApp, Button, Checkbox, Descriptions, Drawer, Image, Input, Modal, Segmented, Space, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { catalogApi, type ProductStatus, type ReviewRow } from '../api/catalog'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { formatPrice } from '../lib/money'
import { P, can } from '../permissions'

const STATUSES: { label: string; value: ProductStatus }[] = [
  { label: 'Chờ duyệt', value: 'PendingReview' },
  { label: 'Đang bán', value: 'Active' },
  { label: 'Bị khoá', value: 'Banned' },
  { label: 'Đã ẩn', value: 'Hidden' },
]

type Pending = { action: 'reject' | 'ban'; id: string } | null

const ProductReviewPage = ({ permissions }: { permissions: string[] }) => {
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<ProductStatus>('PendingReview')
  const [flaggedOnly, setFlaggedOnly] = useState(false)
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [viewing, setViewing] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending>(null)
  const [reason, setReason] = useState('')

  const queue = useQuery({
    queryKey: ['review', status, flaggedOnly, search, page],
    queryFn: () => catalogApi.reviewQueue({ status, flaggedOnly, q: search, page, pageSize: 20 }),
  })
  const detail = useQuery({ queryKey: ['admin-product', viewing], queryFn: () => catalogApi.product(viewing!), enabled: !!viewing })

  const act = useMutation({
    mutationFn: (v: { id: string; action: 'approve' | 'reject' | 'ban' | 'unban'; reason?: string }) => catalogApi.moderate(v.id, v.action, v.reason),
    onSuccess: (r) => {
      void message.success(r.message)
      setPending(null)
      setReason('')
      setViewing(null)
      void queryClient.invalidateQueries({ queryKey: ['review'] })
    },
    onError: (e) => void message.error(e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : 'Thao tác thất bại.'),
  })

  const actions = (id: string, s: ProductStatus) => (
    <Space wrap>
      {s === 'PendingReview' && can(permissions, P.ProductReview) && (
        <>
          <Button size="small" type="primary" onClick={() => act.mutate({ id, action: 'approve' })} data-testid="approve">Duyệt</Button>
          <Button size="small" onClick={() => setPending({ action: 'reject', id })}>Yêu cầu sửa</Button>
        </>
      )}
      {s !== 'Banned' && can(permissions, P.ProductBan) && <Button size="small" danger onClick={() => setPending({ action: 'ban', id })}>Khoá</Button>}
      {s === 'Banned' && can(permissions, P.ProductBan) && <Button size="small" onClick={() => act.mutate({ id, action: 'unban' })}>Mở khoá</Button>}
    </Space>
  )

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Title level={3} style={{ margin: 0 }}>Duyệt sản phẩm</Typography.Title>
      <Space wrap>
        <Segmented options={STATUSES} value={status} onChange={(v) => { setStatus(v as ProductStatus); setPage(1) }} />
        <Checkbox checked={flaggedOnly} onChange={(e) => { setFlaggedOnly(e.target.checked); setPage(1) }}>Chỉ sản phẩm bị gắn cờ</Checkbox>
        <Input.Search placeholder="Tên sản phẩm" allowClear onSearch={(v) => { setSearch(v); setPage(1) }} style={{ width: 260 }} />
      </Space>
      <Table<ReviewRow>
        rowKey="id"
        loading={queue.isPending}
        dataSource={queue.data?.items}
        locale={{ emptyText: 'Không có sản phẩm nào.' }}
        pagination={{ current: page, pageSize: 20, total: queue.data?.totalCount, onChange: setPage, showTotal: (t) => `${t} sản phẩm` }}
        columns={[
          {
            title: 'Sản phẩm',
            render: (_, r) => (
              <Space>
                {r.imageUrl && <Image src={r.imageUrl} width={48} height={48} style={{ objectFit: 'cover' }} preview={false} />}
                <div>
                  <Typography.Link onClick={() => setViewing(r.id)}>{r.name}</Typography.Link>
                  <div><Typography.Text type="secondary">{r.categoryPath.join(' › ')}</Typography.Text></div>
                  {r.flags && <Tag color="red">{r.flags}</Tag>}
                </div>
              </Space>
            ),
          },
          { title: 'Shop', dataIndex: 'shopName' },
          { title: 'Giá', render: (_, r) => (r.minPrice === r.maxPrice ? formatPrice(r.minPrice) : `${formatPrice(r.minPrice)} – ${formatPrice(r.maxPrice)}`) },
          { title: 'Gửi lúc', dataIndex: 'submittedAt', render: (v: string | null) => (v ? formatDateTime(v) : '—') },
          { title: '', render: (_, r) => actions(r.id, r.status) },
        ]}
      />

      <Drawer title={detail.data?.name} open={!!viewing} onClose={() => setViewing(null)} width={720} extra={detail.data && actions(detail.data.id, detail.data.status)}>
        {detail.data && (
          <Space direction="vertical" style={{ width: '100%' }}>
            {detail.data.flags && <Alert type="error" showIcon message={detail.data.flags} />}
            <Image.PreviewGroup>
              <Space wrap>{detail.data.media.filter((m) => m.type === 'Image').map((m) => <Image key={m.url} src={m.url} width={120} height={120} style={{ objectFit: 'cover' }} />)}</Space>
            </Image.PreviewGroup>
            <Descriptions column={1} size="small" bordered>
              <Descriptions.Item label="Danh mục">{detail.data.categoryPath.join(' › ')}</Descriptions.Item>
              <Descriptions.Item label="Phân loại">{detail.data.tiers.map((t) => `${t.name}: ${t.options.map((o) => o.value).join(', ')}`).join(' | ') || 'Không'}</Descriptions.Item>
            </Descriptions>
            <Table size="small" rowKey="id" pagination={false} dataSource={detail.data.skus.filter((s) => s.isActive)}
              columns={[
                { title: 'Phân loại', render: (_, s) => [s.option1, s.option2].filter(Boolean).join(' / ') || 'Mặc định' },
                { title: 'Giá', render: (_, s) => formatPrice(s.price) },
                { title: 'Giá gốc', render: (_, s) => formatPrice(s.originalPrice) },
                { title: 'Tồn', dataIndex: 'stock' },
              ]} />
            <Typography.Title level={5}>Mô tả</Typography.Title>
            {/* Description HTML was sanitised by the API before storage */}
            <div className="product-description" dangerouslySetInnerHTML={{ __html: detail.data.description }} />
          </Space>
        )}
      </Drawer>

      <Modal title={pending?.action === 'ban' ? 'Khoá sản phẩm vi phạm' : 'Yêu cầu người bán sửa'} open={!!pending}
        onCancel={() => setPending(null)} okText="Xác nhận" cancelText="Huỷ" okButtonProps={{ disabled: !reason.trim(), loading: act.isPending, danger: pending?.action === 'ban' }}
        onOk={() => pending && act.mutate({ id: pending.id, action: pending.action, reason })}>
        <Input.TextArea rows={3} placeholder="Lý do (người bán sẽ nhận được)" value={reason} onChange={(e) => setReason(e.target.value)} />
      </Modal>
    </Space>
  )
}

export default ProductReviewPage
