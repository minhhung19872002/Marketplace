import { useState } from 'react'
import { Alert, App as AntApp, Button, Card, DatePicker, Descriptions, Input, InputNumber, Space, Tag, Typography } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import type { Dayjs } from 'dayjs'
import { sellerApi, type MediaAsset, type MyShop } from '../api/seller'
import { ApiError } from '../api/http'
import { ROLE_LABELS, type StaffRole } from '../api/staff'
import UploadBox from '../components/UploadBox'
import { formatDateTime, vnDayBoundsIso } from '../lib/datetime'

const STATUS: Record<MyShop['status'], { text: string; color: string }> = {
  PendingReview: { text: 'Chờ duyệt', color: 'gold' },
  Active: { text: 'Đang hoạt động', color: 'green' },
  Vacation: { text: 'Tạm nghỉ', color: 'blue' },
  Locked: { text: 'Bị khoá', color: 'red' },
  Rejected: { text: 'Bị từ chối', color: 'red' },
}

const ShopSettingsPage = ({ shop }: { shop: MyShop }) => {
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  // Start from what is saved: saving the profile replaces the description
  const [description, setDescription] = useState(shop.description)
  const [logo, setLogo] = useState<MediaAsset | null>(null)
  const [cover, setCover] = useState<MediaAsset | null>(null)
  const [until, setUntil] = useState<Dayjs | null>(null)
  const [lowStock, setLowStock] = useState<number | null>(shop.lowStockThreshold)

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['my-shops'] })
  const fail = (e: unknown) => void message.error(e instanceof ApiError ? e.message : 'Thao tác thất bại.')

  const saveProfile = useMutation({
    mutationFn: () => sellerApi.updateShopProfile(shop.id, { description, logoAssetId: logo?.id ?? null, coverAssetId: cover?.id ?? null }),
    onSuccess: (r) => { void message.success(r.message); setLogo(null); setCover(null); void refresh() },
    onError: fail,
  })
  const saveLowStock = useMutation({
    mutationFn: () => sellerApi.setLowStock(shop.id, lowStock),
    onSuccess: (r) => { void message.success(r.message); void refresh() },
    onError: fail,
  })
  const vacation = useMutation({
    mutationFn: (on: boolean) => sellerApi.setVacation(shop.id, on ? vnDayBoundsIso(undefined, until?.format('YYYY-MM-DD')).to ?? null : null),
    onSuccess: (r) => { void message.success(r.message); void refresh() },
    onError: fail,
  })

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%', maxWidth: 820 }}>
      <Typography.Title level={3} style={{ margin: 0 }}>Thiết lập shop</Typography.Title>
      {shop.status === 'PendingReview' && <Alert type="info" showIcon message="Hồ sơ shop đang chờ sàn duyệt. Bạn vẫn có thể soạn sản phẩm nháp." />}
      {shop.status === 'Rejected' && <Alert type="error" showIcon message={`Hồ sơ bị từ chối: ${shop.rejectReason}`} />}
      {shop.status === 'Locked' && <Alert type="error" showIcon message={`Shop bị khoá: ${shop.lockReason}`} />}

      <Card>
        <Descriptions column={1} size="small">
          <Descriptions.Item label="Tên shop">{shop.name}</Descriptions.Item>
          <Descriptions.Item label="Trạng thái"><Tag color={STATUS[shop.status].color}>{STATUS[shop.status].text}</Tag></Descriptions.Item>
          <Descriptions.Item label="Loại">{shop.type === 'Mall' ? 'ShopHub Mall' : shop.type === 'Business' ? 'Doanh nghiệp' : 'Cá nhân'}</Descriptions.Item>
          <Descriptions.Item label="Vai trò của bạn">{ROLE_LABELS[shop.role as StaffRole] ?? shop.role}</Descriptions.Item>
          <Descriptions.Item label="Tạo lúc">{formatDateTime(shop.createdAt)}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card title="Hồ sơ shop">
        <Space direction="vertical" style={{ width: '100%' }}>
          <Space align="start" wrap>
            <Space direction="vertical" size={4}>
              <Typography.Text type="secondary">Logo (vuông)</Typography.Text>
              {shop.logoUrl && !logo && <img src={shop.logoUrl} alt="Logo hiện tại" className="upload-preview" />}
              <UploadBox purpose="shop" label={shop.logoUrl ? 'Đổi logo' : 'Logo'} value={logo} onChange={setLogo} testId="logo-upload" />
            </Space>
            <Space direction="vertical" size={4}>
              <Typography.Text type="secondary">Ảnh bìa (ngang, ≥ 1200 px)</Typography.Text>
              {shop.coverUrl && !cover && <img src={shop.coverUrl} alt="Ảnh bìa hiện tại" className="upload-preview" />}
              <UploadBox purpose="shop" label={shop.coverUrl ? 'Đổi ảnh bìa' : 'Ảnh bìa'} value={cover} onChange={setCover} testId="cover-upload" />
            </Space>
          </Space>
          <Input.TextArea rows={4} placeholder="Giới thiệu shop" data-testid="shop-description" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={2000} />
          <Button type="primary" onClick={() => saveProfile.mutate()} loading={saveProfile.isPending}>Lưu hồ sơ</Button>
        </Space>
      </Card>

      <Card title="Cảnh báo sắp hết hàng">
        <Typography.Paragraph type="secondary">
          Sản phẩm còn từ bấy nhiêu đơn vị trở xuống hiện ở mục "Sắp hết hàng". Để trống để dùng mức chung của sàn.
        </Typography.Paragraph>
        <Space>
          <InputNumber<number> min={0} max={100000} value={lowStock} onChange={setLowStock} placeholder="Mức của sàn" aria-label="Ngưỡng sắp hết hàng"
            data-testid="low-stock-threshold" />
          <Button onClick={() => saveLowStock.mutate()} loading={saveLowStock.isPending}>Lưu</Button>
        </Space>
      </Card>

      <Card title="Chế độ tạm nghỉ">
        <Typography.Paragraph type="secondary">Khi tạm nghỉ, người mua vẫn xem được shop nhưng không đặt hàng được.</Typography.Paragraph>
        {shop.status === 'Vacation' ? (
          <Button onClick={() => vacation.mutate(false)} loading={vacation.isPending}>Tắt tạm nghỉ</Button>
        ) : (
          <Space>
            <DatePicker format="DD/MM/YYYY" placeholder="Nghỉ đến ngày" value={until} onChange={setUntil} />
            <Button onClick={() => vacation.mutate(true)} disabled={!until || shop.status !== 'Active'} loading={vacation.isPending}>Bật tạm nghỉ</Button>
          </Space>
        )}
      </Card>
    </Space>
  )
}

export default ShopSettingsPage
