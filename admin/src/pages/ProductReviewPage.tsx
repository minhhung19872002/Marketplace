import { useState } from 'react'
import { Alert, App as AntApp, Button, Checkbox, Descriptions, Drawer, Image, Input, Modal, Segmented, Space, Table, Typography } from 'antd'
import { CheckOutlined, EditOutlined, EyeOutlined, LockOutlined, StopOutlined, UnlockOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { catalogApi, type ProductStatus, type ReviewRow } from '../api/catalog'
import { ApiError } from '../api/http'
import { platformApi } from '../api/platform'
import { formatDateTime } from '../lib/datetime'
import { formatNumber, formatPrice, formatRange } from '../lib/money'
import { PRODUCT_STATUS } from '../lib/status'
import { P, can } from '../permissions'
import DataTable from '../components/DataTable'
import RowActions from '../components/RowActions'
import StatusTag, { ToneTag } from '../components/StatusTag'

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
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [bulkReason, setBulkReason] = useState<string | null>(null)
  const bulk = useMutation({
    mutationFn: () => platformApi.bulkBan(selectedIds, bulkReason ?? ''),
    onSuccess: (r) => {
      void message.success(r.message)
      setBulkReason(null)
      setSelectedIds([])
      void queryClient.invalidateQueries({ queryKey: ['review'] })
    },
    onError: (e) => void message.error(e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : 'Thao tác thất bại.'),
  })

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

  // Drawer header: every action as a button
  const actions = (id: string, s: ProductStatus) => (
    <Space wrap>
      {s === 'PendingReview' && can(permissions, P.ProductReview) && (
        <>
          <Button size="small" type="primary" onClick={() => act.mutate({ id, action: 'approve' })}>Duyệt</Button>
          <Button size="small" onClick={() => setPending({ action: 'reject', id })}>Yêu cầu sửa</Button>
        </>
      )}
      {s !== 'Banned' && can(permissions, P.ProductBan) && <Button size="small" danger onClick={() => setPending({ action: 'ban', id })}>Khoá</Button>}
      {s === 'Banned' && can(permissions, P.ProductBan) && <Button size="small" onClick={() => act.mutate({ id, action: 'unban' })}>Mở khoá</Button>}
    </Space>
  )
  const canReview = can(permissions, P.ProductReview)
  const canBan = can(permissions, P.ProductBan)

  return (
    <>
      <DataTable<ReviewRow>
        header={{ title: 'Duyệt sản phẩm', description: 'Hàng đợi sản phẩm người bán gửi duyệt; khoá sản phẩm vi phạm, kể cả hàng loạt' }}
        search={{ value: search, onSearch: (v) => { setSearch(v); setPage(1) }, placeholder: 'Tên sản phẩm', width: 260 }}
        filters={(
          <>
            <Segmented options={STATUSES} value={status} onChange={(v) => { setStatus(v as ProductStatus); setPage(1); setSelectedIds([]) }} />
            <Checkbox checked={flaggedOnly} onChange={(e) => { setFlaggedOnly(e.target.checked); setPage(1) }}>Chỉ sản phẩm bị gắn cờ</Checkbox>
          </>
        )}
        onReset={() => { setStatus('PendingReview'); setFlaggedOnly(false); setSearch(''); setPage(1); setSelectedIds([]) }}
        toolbar={canBan && selectedIds.length > 0 && (
          <>
            <Typography.Text>Đã chọn {formatNumber(selectedIds.length)} sản phẩm</Typography.Text>
            <Button size="small" danger icon={<LockOutlined aria-hidden />} onClick={() => setBulkReason('')} data-testid="bulk-ban">
              Khoá {selectedIds.length} sản phẩm đã chọn
            </Button>
            <Button size="small" type="text" onClick={() => setSelectedIds([])}>Bỏ chọn</Button>
          </>
        )}
        rowKey="id"
        rowSelection={canBan
          ? { selectedRowKeys: selectedIds, onChange: (keys) => setSelectedIds((keys as string[]).slice(0, 100)), getCheckboxProps: (r) => ({ disabled: r.status === 'Banned' }) }
          : undefined}
        loading={queue.isPending}
        fetching={queue.isFetching && !queue.isPending}
        error={queue.error}
        dataSource={queue.data?.items}
        emptyText={status === 'PendingReview' ? 'Không còn sản phẩm nào chờ duyệt' : 'Không có sản phẩm nào'}
        paging={{ page, pageSize: 20, total: queue.data?.totalCount, onChange: setPage }}
        columns={[
          {
            title: 'Sản phẩm',
            render: (_, r) => (
              <Space align="start">
                {r.imageUrl && <Image src={r.imageUrl} width={48} height={48} className="thumb" preview={false} />}
                <div style={{ maxWidth: 420 }}>
                  <Typography.Link onClick={() => setViewing(r.id)}>{r.name}</Typography.Link>
                  <span className="cell-sub">{r.categoryPath.join(' › ')}</span>
                  {r.flags && <ToneTag tone="error">{r.flags}</ToneTag>}
                </div>
              </Space>
            ),
          },
          { title: 'Shop', dataIndex: 'shopName' },
          { title: 'Giá', render: (_, r) => <span className="cell-money">{formatRange(r.minPrice, r.maxPrice)}</span> },
          { title: 'Trạng thái', dataIndex: 'status', render: (v: ProductStatus) => <StatusTag map={PRODUCT_STATUS} value={v} /> },
          { title: 'Gửi lúc', dataIndex: 'submittedAt', render: (v: string | null) => <span className="cell-nowrap">{v ? formatDateTime(v) : '—'}</span> },
          {
            title: '', key: 'actions', align: 'right',
            render: (_, r) => (
              <RowActions name={r.name}
                primary={r.status === 'PendingReview' && canReview
                  ? <Button size="small" type="primary" icon={<CheckOutlined aria-hidden />} onClick={() => act.mutate({ id: r.id, action: 'approve' })} data-testid="approve">Duyệt</Button>
                  : <Button size="small" icon={<EyeOutlined aria-hidden />} onClick={() => setViewing(r.id)}>Xem</Button>}
                items={[
                  { key: 'view', icon: <EyeOutlined aria-hidden />, label: 'Xem chi tiết', onClick: () => setViewing(r.id), hidden: !(r.status === 'PendingReview' && canReview) },
                  { key: 'reject', icon: <EditOutlined aria-hidden />, label: 'Yêu cầu sửa', onClick: () => setPending({ action: 'reject', id: r.id }), hidden: !(r.status === 'PendingReview' && canReview) },
                  { key: 'unban', icon: <UnlockOutlined aria-hidden />, label: 'Mở khoá', onClick: () => act.mutate({ id: r.id, action: 'unban' }), hidden: !(r.status === 'Banned' && canBan) },
                  // Opens the reason dialog: the reason is the confirmation
                  { key: 'ban', icon: <StopOutlined aria-hidden />, label: 'Khoá sản phẩm', danger: true, onClick: () => setPending({ action: 'ban', id: r.id }), hidden: !(r.status !== 'Banned' && canBan) },
                ]} />
            ),
          },
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

      <Modal title={`Khoá ${selectedIds.length} sản phẩm (tối đa 100 mỗi lần)`} open={bulkReason !== null} onCancel={() => setBulkReason(null)}
        okText="Khoá" cancelText="Huỷ" okButtonProps={{ danger: true, disabled: !(bulkReason ?? '').trim(), loading: bulk.isPending }} onOk={() => bulk.mutate()}>
        <Input.TextArea rows={3} placeholder="Lý do (người bán sẽ nhận được)" value={bulkReason ?? ''} onChange={(e) => setBulkReason(e.target.value)} />
      </Modal>

      <Modal title={pending?.action === 'ban' ? 'Khoá sản phẩm vi phạm' : 'Yêu cầu người bán sửa'} open={!!pending}
        onCancel={() => setPending(null)} okText="Xác nhận" cancelText="Huỷ" okButtonProps={{ disabled: !reason.trim(), loading: act.isPending, danger: pending?.action === 'ban' }}
        onOk={() => pending && act.mutate({ id: pending.id, action: pending.action, reason })}>
        <Input.TextArea rows={3} placeholder="Lý do (người bán sẽ nhận được)" value={reason} onChange={(e) => setReason(e.target.value)} />
      </Modal>
    </>
  )
}

export default ProductReviewPage
