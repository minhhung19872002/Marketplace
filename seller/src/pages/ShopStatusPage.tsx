import { useState } from 'react'
import { App as AntApp, Alert, Button, Card, Form, Input, Result, Space, Typography } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { sellerApi, type MediaAsset, type MyShop } from '../api/seller'
import { ApiError } from '../api/http'
import UploadBox from '../components/UploadBox'

interface KycValues {
  legalName: string
  idCardNumber: string
  taxCode: string
}

/**
 * A shop that is not selling yet / any more (III.1, D3): waiting for review, rejected — the reason and a form to send
 * the identity papers again — or locked. The selling menu stays closed until the shop is active.
 */
const ShopStatusPage = ({ shop }: { shop: MyShop }) => {
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<KycValues>()
  const [front, setFront] = useState<MediaAsset | null>(null)
  const [back, setBack] = useState<MediaAsset | null>(null)
  const [license, setLicense] = useState<MediaAsset | null>(null)
  const [error, setError] = useState('')
  const personal = shop.type === 'Personal'

  const resubmit = useMutation({
    mutationFn: (v: KycValues) => sellerApi.resubmitShop(shop.id, {
      personal: personal ? { legalName: v.legalName, idCardNumber: v.idCardNumber, frontAssetId: front?.id ?? '', backAssetId: back?.id ?? '' } : null,
      business: personal ? null : { legalName: v.legalName, taxCode: v.taxCode, licenseAssetId: license?.id ?? '' },
    }),
    onSuccess: (r) => {
      message.success(r.message)
      void queryClient.invalidateQueries({ queryKey: ['my-shops'] })
    },
    onError: (e) => setError(e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : 'Không gửi lại được hồ sơ.'),
  })

  if (shop.status === 'PendingReview')
    return <div data-testid="shop-pending"><Result status="info" title="Hồ sơ bán hàng đang chờ duyệt" subTitle="Sàn sẽ duyệt trong 1–2 ngày làm việc. Bạn sẽ nhận thông báo khi có kết quả." /></div>
  if (shop.status === 'Locked')
    return <div data-testid="shop-locked"><Result status="error" title="Shop đang bị khoá" subTitle={shop.lockReason ?? 'Liên hệ bộ phận hỗ trợ người bán để biết thêm.'} /></div>

  const docsReady = personal ? !!front && !!back : !!license
  return (
    <Card title="Hồ sơ bán hàng bị từ chối" style={{ maxWidth: 720 }} data-testid="shop-rejected">
      <Alert type="error" showIcon message="Lý do từ chối" description={shop.rejectReason ?? 'Sàn không ghi lý do.'} data-testid="reject-reason" style={{ marginBottom: 16 }} />
      <Typography.Paragraph>Sửa giấy tờ định danh theo lý do trên rồi gửi lại — sàn sẽ duyệt lại hồ sơ.</Typography.Paragraph>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />}
      <Form form={form} layout="vertical" onFinish={(v) => { setError(''); resubmit.mutate(v) }}>
        {personal ? (
          <>
            <Form.Item label="Họ tên trên CCCD" name="legalName" rules={[{ required: true, message: 'Nhập họ tên.' }]}><Input /></Form.Item>
            <Form.Item label="Số CCCD (12 số)" name="idCardNumber" rules={[{ required: true, pattern: /^\d{12}$/, message: 'Số CCCD gồm 12 chữ số.' }]}>
              <Input inputMode="numeric" maxLength={12} />
            </Form.Item>
            <Space>
              <UploadBox purpose="kyc" label="Mặt trước CCCD" value={front} onChange={setFront} testId="kyc-front" />
              <UploadBox purpose="kyc" label="Mặt sau CCCD" value={back} onChange={setBack} testId="kyc-back" />
            </Space>
          </>
        ) : (
          <>
            <Form.Item label="Tên doanh nghiệp / hộ kinh doanh" name="legalName" rules={[{ required: true, message: 'Nhập tên doanh nghiệp.' }]}><Input /></Form.Item>
            <Form.Item label="Mã số thuế" name="taxCode" rules={[{ required: true, pattern: /^\d{10}(-\d{3})?$/, message: 'Mã số thuế gồm 10 số.' }]}><Input /></Form.Item>
            <UploadBox purpose="kyc" label="Giấy phép kinh doanh" accept="image/*,application/pdf" value={license} onChange={setLicense} />
          </>
        )}
        <Button type="primary" htmlType="submit" loading={resubmit.isPending} disabled={!docsReady} style={{ marginTop: 16 }} data-testid="resubmit-shop">
          Gửi lại hồ sơ
        </Button>
      </Form>
    </Card>
  )
}

export default ShopStatusPage
