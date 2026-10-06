import { useState } from 'react'
import { Alert, App as AntApp, Button, Checkbox, Descriptions, Drawer, Image, Input, Modal, Select, Space, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { catalogApi, type ShopRow, type ShopStatus } from '../api/catalog'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { P, can } from '../permissions'

const STATUS: Record<ShopStatus, { text: string; color: string }> = {
  PendingReview: { text: 'Chờ duyệt', color: 'gold' },
  Active: { text: 'Hoạt động', color: 'green' },
  Vacation: { text: 'Tạm nghỉ', color: 'blue' },
  Locked: { text: 'Bị khoá', color: 'red' },
  Rejected: { text: 'Bị từ chối', color: 'default' },
}

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
  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Title level={3} style={{ margin: 0 }}>Shop</Typography.Title>
      <Space wrap>
        <Select<ShopStatus> allowClear placeholder="Trạng thái" style={{ width: 180 }} value={status}
          onChange={(v) => { setStatus(v); setPage(1) }}
          options={(Object.keys(STATUS) as ShopStatus[]).map((s) => ({ value: s, label: STATUS[s].text }))} />
        <Input.Search placeholder="Tên shop" allowClear style={{ width: 260 }} onSearch={(v) => { setSearch(v); setPage(1) }} />
      </Space>
      <Table<ShopRow>
        rowKey="id"
        loading={shops.isPending}
        dataSource={shops.data?.items}
        pagination={{ current: page, pageSize: 20, total: shops.data?.totalCount, onChange: setPage, showTotal: (t) => `${t} shop` }}
        columns={[
          {
            title: 'Shop',
            render: (_, s) => (
              <Space>
                <Typography.Link onClick={() => setViewing(s.id)}>{s.name}</Typography.Link>
                {s.type === 'Mall' && <Tag color="red">Mall</Tag>}
                {s.isPreferred && <Tag color="orange">Yêu thích</Tag>}
              </Space>
            ),
          },
          { title: 'Chủ shop', render: (_, s) => `${s.ownerName}${s.ownerPhone ? ` · ${s.ownerPhone}` : ''}` },
          { title: 'Sản phẩm', dataIndex: 'productCount' },
          { title: 'Trạng thái', dataIndex: 'status', render: (v: ShopStatus) => <Tag color={STATUS[v].color}>{STATUS[v].text}</Tag> },
          { title: 'Đăng ký', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
        ]}
      />

      <Drawer title={d?.name} open={!!viewing} onClose={() => setViewing(null)} width={720}>
        {d && (
          <Space direction="vertical" style={{ width: '100%' }}>
            {d.rejectReason && <Alert type="warning" showIcon message={`Lần trước bị từ chối: ${d.rejectReason}`} />}
            {d.lockReason && <Alert type="error" showIcon message={`Bị khoá: ${d.lockReason}`} />}
            <Descriptions column={1} size="small" bordered>
              <Descriptions.Item label="Trạng thái"><Tag color={STATUS[d.status].color}>{STATUS[d.status].text}</Tag></Descriptions.Item>
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
                <Checkbox checked={d.isPreferred} onChange={(e) => labels.mutate({ id: d.id, isMall: d.type === 'Mall', isPreferred: e.target.checked })}>Shop Yêu thích</Checkbox>
              </Space>
            )}
          </Space>
        )}
      </Drawer>

      <Modal title={pending?.action === 'lock' ? 'Khoá shop' : 'Từ chối hồ sơ'} open={!!pending} onCancel={() => setPending(null)}
        okText="Xác nhận" cancelText="Huỷ" okButtonProps={{ danger: true, disabled: !reason.trim(), loading: act.isPending }}
        onOk={() => pending && act.mutate({ id: pending.id, action: pending.action, reason })}>
        <Input.TextArea rows={3} placeholder="Lý do (chủ shop sẽ nhận được)" value={reason} onChange={(e) => setReason(e.target.value)} />
      </Modal>
    </Space>
  )
}

export default ShopsPage
