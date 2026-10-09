import { useState } from 'react'
import { Alert, App as AntApp, Button, Checkbox, Descriptions, Drawer, Image, Input, Modal, Select, Space, Typography } from 'antd'
import { CheckOutlined, CloseOutlined, LockOutlined, ProfileOutlined, UnlockOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { catalogApi, type ShopRow, type ShopStatus } from '../api/catalog'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { P, can } from '../permissions'
import { formatNumber } from '../lib/money'
import { SHOP_STATUS } from '../lib/status'
import ShopPenalties from '../components/ShopPenalties'
import DataTable from '../components/DataTable'
import RowActions from '../components/RowActions'
import StatusTag, { ToneTag } from '../components/StatusTag'

const STATUSES = Object.keys(SHOP_STATUS) as ShopStatus[]

const ShopsPage = ({ permissions }: { permissions: string[] }) => {
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<ShopStatus | undefined>('PendingReview')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [viewing, setViewing] = useState<string | null>(null)
  const [pending, setPending] = useState<{ action: 'reject' | 'lock'; id: string } | null>(null)
  const [reason, setReason] = useState('')

  const shops = useQuery({ queryKey: ['shops', status, search, page], queryFn: () => catalogApi.shops({ status, q: search, page, pageSize: 20 }) })
  // Signed KYC URLs expire after 5 minutes: never cache this query
  const detail = useQuery({ queryKey: ['shop', viewing], queryFn: () => catalogApi.shop(viewing!), enabled: !!viewing, staleTime: 0, gcTime: 0 })

  const refresh = (text: string) => {
    void message.success(text)
    setPending(null)
    setReason('')
    void queryClient.invalidateQueries({ queryKey: ['shops'] })
    void queryClient.invalidateQueries({ queryKey: ['shop'] })
  }
  const fail = (e: unknown) => void message.error(e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : 'Thao tác thất bại.')
  const act = useMutation({
    mutationFn: (v: { id: string; action: 'approve' | 'reject' | 'lock' | 'unlock'; reason?: string }) => catalogApi.moderateShop(v.id, v.action, v.reason),
    onSuccess: (r) => refresh(r.message),
    onError: fail,
  })
  const labels = useMutation({
    mutationFn: (v: { id: string; isMall: boolean; isPreferred: boolean }) => catalogApi.setLabels(v.id, v.isMall, v.isPreferred),
    onSuccess: (r) => refresh(r.message),
    onError: fail,
  })

  const d = detail.data
  const canReview = can(permissions, P.ShopReview)
  const canLock = can(permissions, P.ShopLock)
  return (
    <>
      <DataTable<ShopRow>
        header={{ title: 'Shop', description: 'Duyệt hồ sơ đăng ký bán hàng, gắn nhãn Mall / Yêu thích, khoá shop và quản lý điểm phạt' }}
        search={{ value: search, onSearch: (v) => { setSearch(v); setPage(1) }, placeholder: 'Tên shop', width: 260 }}
        filters={(
          <Select<ShopStatus> allowClear placeholder="Mọi trạng thái" style={{ width: 180 }} value={status} aria-label="Lọc theo trạng thái"
            onChange={(v) => { setStatus(v); setPage(1) }}
            options={STATUSES.map((v) => ({ value: v, label: SHOP_STATUS[v].label }))} />
        )}
        onReset={() => { setStatus('PendingReview'); setSearch(''); setPage(1) }}
        rowKey="id"
        loading={shops.isPending}
        fetching={shops.isFetching && !shops.isPending}
        error={shops.error}
        dataSource={shops.data?.items}
        emptyText={status === 'PendingReview' ? 'Không có hồ sơ nào chờ duyệt' : 'Không có shop phù hợp'}
        paging={{ page, pageSize: 20, total: shops.data?.totalCount, onChange: setPage }}
        columns={[
          {
            title: 'Shop',
            render: (_, s) => (
              <Space size={6} wrap>
                <Typography.Link onClick={() => setViewing(s.id)}>{s.name}</Typography.Link>
                {s.type === 'Mall' && <ToneTag tone="error">Mall</ToneTag>}
                {s.isPreferred && <ToneTag tone="warning">Yêu thích</ToneTag>}
              </Space>
            ),
          },
          { title: 'Chủ shop', render: (_, s) => <><span className="cell-main">{s.ownerName}</span>{s.ownerPhone && <span className="cell-sub">{s.ownerPhone}</span>}</> },
          { title: 'Sản phẩm', dataIndex: 'productCount', align: 'right', render: (v: number) => formatNumber(v) },
          { title: 'Trạng thái', dataIndex: 'status', render: (v: ShopStatus) => <StatusTag map={SHOP_STATUS} value={v} /> },
          { title: 'Đăng ký', dataIndex: 'createdAt', render: (v: string) => <span className="cell-nowrap">{formatDateTime(v)}</span> },
          {
            title: '', key: 'actions', align: 'right',
            render: (_, s) => (
              <RowActions name={s.name}
                primary={s.status === 'PendingReview' && canReview
                  ? <Button size="small" type="primary" icon={<ProfileOutlined aria-hidden />} onClick={() => setViewing(s.id)}>Xem hồ sơ</Button>
                  : <Button size="small" onClick={() => setViewing(s.id)}>Chi tiết</Button>}
                items={[
                  { key: 'approve', icon: <CheckOutlined aria-hidden />, label: 'Duyệt', hidden: !(s.status === 'PendingReview' && canReview),
                    confirm: { title: `Duyệt shop ${s.name}?`, content: 'Nên xem hồ sơ định danh trước khi duyệt.', okText: 'Duyệt' },
                    onClick: () => act.mutateAsync({ id: s.id, action: 'approve' }).catch(() => undefined) },
                  { key: 'unlock', icon: <UnlockOutlined aria-hidden />, label: 'Mở khoá', hidden: !(s.status === 'Locked' && canLock),
                    onClick: () => act.mutate({ id: s.id, action: 'unlock' }) },
                  // Both open the reason dialog: the reason is the confirmation
                  { key: 'reject', icon: <CloseOutlined aria-hidden />, label: 'Từ chối hồ sơ', danger: true, hidden: !(s.status === 'PendingReview' && canReview),
                    onClick: () => setPending({ action: 'reject', id: s.id }) },
                  { key: 'lock', icon: <LockOutlined aria-hidden />, label: 'Khoá shop', danger: true,
                    hidden: !(s.status !== 'Locked' && s.status !== 'PendingReview' && s.status !== 'Rejected' && canLock),
                    onClick: () => setPending({ action: 'lock', id: s.id }) },
                ]} />
            ),
          },
        ]}
      />

      <Drawer title={d?.name} open={!!viewing} onClose={() => setViewing(null)} width={720}>
        {d && (
          <Space direction="vertical" style={{ width: '100%' }}>
            {d.rejectReason && <Alert type="warning" showIcon message={`Lần trước bị từ chối: ${d.rejectReason}`} />}
            {d.lockReason && <Alert type="error" showIcon message={`Bị khoá: ${d.lockReason}`} />}
            <Descriptions column={1} size="small" bordered>
              <Descriptions.Item label="Trạng thái"><StatusTag map={SHOP_STATUS} value={d.status} /></Descriptions.Item>
              <Descriptions.Item label="Chủ shop">{d.ownerName} {d.ownerPhone}</Descriptions.Item>
              <Descriptions.Item label="Loại">{d.type}</Descriptions.Item>
              <Descriptions.Item label="Địa chỉ lấy hàng">{d.warehouse ? `${d.warehouse.contactName} · ${d.warehouse.phone} · ${d.warehouse.address}` : '—'}</Descriptions.Item>
              <Descriptions.Item label="Tài khoản nhận tiền">{d.bankName ? `${d.bankName} ****${d.bankAccountLast4}` : '—'}</Descriptions.Item>
              <Descriptions.Item label="Mô tả">{d.description || '—'}</Descriptions.Item>
            </Descriptions>
            {d.kyc && (
              <>
                <Typography.Title level={5}>Hồ sơ định danh</Typography.Title>
                <Descriptions column={1} size="small" bordered>
                  <Descriptions.Item label="Tên pháp lý">{d.kyc.legalName}</Descriptions.Item>
                  {d.kyc.idCardNumberMasked && <Descriptions.Item label="Số CCCD">{d.kyc.idCardNumberMasked}</Descriptions.Item>}
                  {d.kyc.taxCode && <Descriptions.Item label="Mã số thuế">{d.kyc.taxCode}</Descriptions.Item>}
                </Descriptions>
                <Typography.Text type="secondary">Ảnh giấy tờ mở qua liên kết ký có hạn 5 phút.</Typography.Text>
                <Image.PreviewGroup>
                  <Space wrap>
                    {[d.kyc.idCardFrontUrl, d.kyc.idCardBackUrl].filter((u): u is string => !!u).map((u) => (
                      <Image key={u} src={u} width={220} data-testid="kyc-image" />
                    ))}
                  </Space>
                </Image.PreviewGroup>
                {d.kyc.businessLicenseUrl && <a href={d.kyc.businessLicenseUrl} target="_blank" rel="noreferrer">Xem giấy phép kinh doanh</a>}
              </>
            )}
            <Space wrap>
              {d.status === 'PendingReview' && can(permissions, P.ShopReview) && (
                <>
                  <Button type="primary" onClick={() => act.mutate({ id: d.id, action: 'approve' })}>Duyệt</Button>
                  <Button onClick={() => setPending({ action: 'reject', id: d.id })}>Từ chối</Button>
                </>
              )}
              {d.status !== 'Locked' && d.status !== 'PendingReview' && can(permissions, P.ShopLock) && (
                <Button danger onClick={() => setPending({ action: 'lock', id: d.id })}>Khoá shop</Button>
              )}
              {d.status === 'Locked' && can(permissions, P.ShopLock) && <Button onClick={() => act.mutate({ id: d.id, action: 'unlock' })}>Mở khoá</Button>}
            </Space>
            {can(permissions, P.ShopLabel) && (d.status === 'Active' || d.status === 'Vacation') && (
              <Space>
                <Checkbox checked={d.type === 'Mall'} onChange={(e) => labels.mutate({ id: d.id, isMall: e.target.checked, isPreferred: d.isPreferred })}>ShopHub Mall</Checkbox>
                <Checkbox checked={d.isPreferred} onChange={(e) => labels.mutate({ id: d.id, isMall: d.type === 'Mall', isPreferred: e.target.checked })}>Shop yêu thích</Checkbox>
              </Space>
            )}
            {d.status !== 'PendingReview' && <ShopPenalties shopId={d.id} canEdit={can(permissions, P.ShopPenalty)} />}
          </Space>
        )}
      </Drawer>

      <Modal title={pending?.action === 'lock' ? 'Khoá shop' : 'Từ chối hồ sơ'} open={!!pending} onCancel={() => setPending(null)}
        okText="Xác nhận" cancelText="Huỷ" okButtonProps={{ danger: true, disabled: !reason.trim(), loading: act.isPending }}
        onOk={() => pending && act.mutate({ id: pending.id, action: pending.action, reason })}>
        <Input.TextArea rows={3} placeholder="Lý do (chủ shop sẽ nhận được)" value={reason} onChange={(e) => setReason(e.target.value)} />
      </Modal>
    </>
  )
}

export default ShopsPage
