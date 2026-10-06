import { useState } from 'react'
import { Alert, App as AntApp, Button, Card, Form, Input, Radio, Select, Space, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { sellerApi, type MediaAsset, type RegisterShopInput } from '../api/seller'
import { ApiError } from '../api/http'
import UploadBox from '../components/UploadBox'
import { useShopStore } from '../stores/shop'

const BANKS = [
  { value: 'VCB', label: 'Vietcombank' },
  { value: 'TCB', label: 'Techcombank' },
  { value: 'BIDV', label: 'BIDV' },
  { value: 'VTB', label: 'VietinBank' },
  { value: 'ACB', label: 'ACB' },
  { value: 'MB', label: 'MB Bank' },
  { value: 'TPB', label: 'TPBank' },
  { value: 'VPB', label: 'VPBank' },
]

interface FormValues {
  name: string
  type: 'Personal' | 'Business'
  description: string
  contactName: string
  phone: string
  provinceCode: string
  districtCode: string
  wardCode: string
  street: string
  legalName: string
  idCardNumber: string
  taxCode: string
  bankCode: string
  accountNo: string
  accountName: string
}

/** "Đăng ký bán hàng" — becomes a shop in review once submitted. */
const RegisterShopPage = () => {
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const select = useShopStore((s) => s.select)
  const [form] = Form.useForm<FormValues>()
  const type = Form.useWatch('type', form) ?? 'Personal'
  const province = Form.useWatch('provinceCode', form)
  const district = Form.useWatch('districtCode', form)
  const [front, setFront] = useState<MediaAsset | null>(null)
  const [back, setBack] = useState<MediaAsset | null>(null)
  const [license, setLicense] = useState<MediaAsset | null>(null)
  const [error, setError] = useState('')

  const provinces = useQuery({ queryKey: ['div', ''], queryFn: () => sellerApi.divisions(), staleTime: Infinity })
  const districts = useQuery({ queryKey: ['div', province], queryFn: () => sellerApi.divisions(province), enabled: !!province, staleTime: Infinity })
  const wards = useQuery({ queryKey: ['div', district], queryFn: () => sellerApi.divisions(district), enabled: !!district, staleTime: Infinity })

  const submit = useMutation({
    mutationFn: (v: FormValues) => {
      const body: RegisterShopInput = {
        name: v.name.trim(),
        type: v.type,
        description: v.description ?? '',
        warehouse: { contactName: v.contactName, phone: v.phone, provinceCode: v.provinceCode, districtCode: v.districtCode, wardCode: v.wardCode, street: v.street },
        personal: v.type === 'Personal' ? { legalName: v.legalName, idCardNumber: v.idCardNumber, frontAssetId: front?.id ?? '', backAssetId: back?.id ?? '' } : null,
        business: v.type === 'Business' ? { legalName: v.legalName, taxCode: v.taxCode, licenseAssetId: license?.id ?? '' } : null,
        bank: { bankCode: v.bankCode, accountNo: v.accountNo, accountName: v.accountName },
      }
      return sellerApi.registerShop(body)
    },
    onSuccess: async (res) => {
      void message.success(res.message)
      select(res.data)
      await queryClient.invalidateQueries({ queryKey: ['my-shops'] })
    },
    onError: (err) => setError(err instanceof ApiError ? err.fieldErrors.map((f) => f.message).join(' ') || err.message : 'Gửi hồ sơ thất bại.'),
  })

  const docsReady = type === 'Personal' ? !!front && !!back : !!license

  return (
    <Card style={{ maxWidth: 760, margin: '0 auto' }}>
      <Typography.Title level={3}>Đăng ký bán hàng</Typography.Title>
      <Typography.Paragraph type="secondary">Điền thông tin shop. Sàn sẽ duyệt hồ sơ trong 1–2 ngày làm việc.</Typography.Paragraph>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}
      <Form<FormValues> form={form} layout="vertical" initialValues={{ type: 'Personal', bankCode: 'VCB' }}
        onFinish={(v) => { setError(''); submit.mutate(v) }} requiredMark={false}>
        <Typography.Title level={5}>Thông tin shop</Typography.Title>
        <Form.Item label="Tên shop" name="name" rules={[{ required: true, min: 3, max: 50, message: 'Tên shop 3–50 ký tự.' }]}>
          <Input aria-label="Tên shop" />
        </Form.Item>
        <Form.Item label="Loại hình" name="type">
          <Radio.Group options={[{ value: 'Personal', label: 'Cá nhân' }, { value: 'Business', label: 'Doanh nghiệp / Hộ kinh doanh' }]} />
        </Form.Item>
        <Form.Item label="Mô tả" name="description"><Input.TextArea rows={3} maxLength={2000} /></Form.Item>

        <Typography.Title level={5}>Địa chỉ lấy hàng</Typography.Title>
        <Space.Compact block>
          <Form.Item name="contactName" rules={[{ required: true, message: 'Nhập tên liên hệ.' }]} style={{ flex: 1 }}>
            <Input placeholder="Tên người liên hệ" aria-label="Người liên hệ lấy hàng" />
          </Form.Item>
          <Form.Item name="phone" rules={[{ required: true, message: 'Nhập số điện thoại.' }]} style={{ flex: 1 }}>
            <Input placeholder="Số điện thoại" aria-label="SĐT lấy hàng" />
          </Form.Item>
        </Space.Compact>
        <Form.Item name="provinceCode" rules={[{ required: true, message: 'Chọn tỉnh/thành.' }]}>
          <Select placeholder="Tỉnh/Thành phố" showSearch optionFilterProp="label" aria-label="Tỉnh lấy hàng"
            options={provinces.data?.map((p) => ({ value: p.code, label: p.name }))}
            onChange={() => form.setFieldsValue({ districtCode: undefined, wardCode: undefined })} />
        </Form.Item>
        <Form.Item name="districtCode" rules={[{ required: true, message: 'Chọn quận/huyện.' }]}>
          <Select placeholder="Quận/Huyện" showSearch optionFilterProp="label" disabled={!province} aria-label="Quận lấy hàng"
            options={districts.data?.map((p) => ({ value: p.code, label: p.name }))}
            onChange={() => form.setFieldsValue({ wardCode: undefined })} />
        </Form.Item>
        <Form.Item name="wardCode" rules={[{ required: true, message: 'Chọn phường/xã.' }]}>
          <Select placeholder="Phường/Xã" showSearch optionFilterProp="label" disabled={!district} aria-label="Phường lấy hàng"
            options={wards.data?.map((p) => ({ value: p.code, label: p.name }))} />
        </Form.Item>
        <Form.Item name="street" rules={[{ required: true, message: 'Nhập địa chỉ cụ thể.' }]}>
          <Input placeholder="Số nhà, tên đường" aria-label="Địa chỉ lấy hàng" />
        </Form.Item>

        <Typography.Title level={5}>Định danh</Typography.Title>
        {type === 'Personal' ? (
          <>
            <Form.Item label="Họ tên trên CCCD" name="legalName" rules={[{ required: true, message: 'Nhập họ tên.' }]}><Input /></Form.Item>
            <Form.Item label="Số CCCD (12 số)" name="idCardNumber" rules={[{ required: true, pattern: /^\d{12}$/, message: 'Số CCCD gồm 12 chữ số.' }]}>
              <Input inputMode="numeric" maxLength={12} />
            </Form.Item>
            <Space>
              <UploadBox purpose="kyc" label="Mặt trước CCCD" value={front} onChange={setFront} />
              <UploadBox purpose="kyc" label="Mặt sau CCCD" value={back} onChange={setBack} />
            </Space>
            <Typography.Paragraph type="secondary">Ảnh định danh được lưu riêng tư, chỉ bộ phận duyệt của sàn xem được.</Typography.Paragraph>
          </>
        ) : (
          <>
            <Form.Item label="Tên doanh nghiệp / hộ kinh doanh" name="legalName" rules={[{ required: true, message: 'Nhập tên doanh nghiệp.' }]}><Input /></Form.Item>
            <Form.Item label="Mã số thuế" name="taxCode" rules={[{ required: true, pattern: /^\d{10}(-\d{3})?$/, message: 'Mã số thuế gồm 10 số.' }]}><Input /></Form.Item>
            <UploadBox purpose="kyc" label="Giấy phép kinh doanh" accept="image/*,application/pdf" value={license} onChange={setLicense} />
          </>
        )}

        <Typography.Title level={5}>Tài khoản nhận tiền</Typography.Title>
        <Form.Item name="bankCode"><Select options={BANKS} aria-label="Ngân hàng" /></Form.Item>
        <Form.Item name="accountNo" rules={[{ required: true, pattern: /^\d{6,20}$/, message: 'Số tài khoản 6–20 chữ số.' }]}>
          <Input placeholder="Số tài khoản" inputMode="numeric" />
        </Form.Item>
        <Form.Item name="accountName" rules={[{ required: true, message: 'Nhập tên chủ tài khoản.' }]}>
          <Input placeholder="Tên chủ tài khoản (không dấu)" />
        </Form.Item>

        <Button type="primary" htmlType="submit" loading={submit.isPending} disabled={!docsReady} data-testid="register-shop-submit">
          Gửi hồ sơ
        </Button>
        {!docsReady && <Typography.Text type="secondary" style={{ marginLeft: 12 }}>Cần tải đủ giấy tờ định danh.</Typography.Text>}
      </Form>
    </Card>
  )
}

export default RegisterShopPage
