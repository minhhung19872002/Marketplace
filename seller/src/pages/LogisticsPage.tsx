import { useState } from 'react'
import { App, Button, Card, Checkbox, Form, Input, Modal, Popconfirm, Select, Space, Switch, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { logisticsApi, type ShippingChannel, type Warehouse, type WarehouseBody } from '../api/logistics'
import { sellerApi } from '../api/seller'

type Draft = { id?: string; name: string; contactName: string; phone: string; provinceCode?: string; districtCode?: string; wardCode?: string; street: string;
  isPickupDefault: boolean; isReturnDefault: boolean }

const WarehouseForm = ({ draft, saving, onSave, onCancel }: { draft: Draft; saving: boolean; onSave: (d: Draft) => void; onCancel: () => void }) => {
  const [form] = Form.useForm<Draft>()
  const province = Form.useWatch('provinceCode', form) ?? draft.provinceCode
  const district = Form.useWatch('districtCode', form) ?? draft.districtCode
  const provinces = useQuery({ queryKey: ['div', ''], queryFn: () => sellerApi.divisions(), staleTime: Infinity })
  const districts = useQuery({ queryKey: ['div', province], queryFn: () => sellerApi.divisions(province), enabled: !!province, staleTime: Infinity })
  const wards = useQuery({ queryKey: ['div', district], queryFn: () => sellerApi.divisions(district), enabled: !!district, staleTime: Infinity })
  return (
    <Modal open title={draft.id ? 'Sửa kho hàng' : 'Thêm kho hàng'} okText="Lưu" cancelText="Huỷ" confirmLoading={saving}
      onOk={() => form.submit()} onCancel={onCancel} destroyOnClose>
      <Form form={form} layout="vertical" initialValues={draft} onFinish={(v) => onSave({ ...draft, ...v })}>
        <Form.Item label="Tên kho" name="name" rules={[{ required: true, message: 'Nhập tên kho.' }]}>
          <Input maxLength={100} placeholder="VD: Kho Hà Nội" />
        </Form.Item>
        <Space.Compact block>
          <Form.Item name="contactName" rules={[{ required: true, message: 'Nhập người liên hệ.' }]} style={{ flex: 1 }}>
            <Input placeholder="Người liên hệ" aria-label="Người liên hệ" />
          </Form.Item>
          <Form.Item name="phone" rules={[{ required: true, message: 'Nhập số điện thoại.' }]} style={{ flex: 1 }}>
            <Input placeholder="Số điện thoại" aria-label="Số điện thoại kho" />
          </Form.Item>
        </Space.Compact>
        <Form.Item name="provinceCode" rules={[{ required: true, message: 'Chọn tỉnh/thành.' }]}>
          <Select placeholder="Tỉnh/Thành phố" showSearch optionFilterProp="label" aria-label="Tỉnh"
            options={provinces.data?.map((p) => ({ value: p.code, label: p.name }))}
            onChange={() => form.setFieldsValue({ districtCode: undefined, wardCode: undefined })} />
        </Form.Item>
        <Form.Item name="districtCode" rules={[{ required: true, message: 'Chọn quận/huyện.' }]}>
          <Select placeholder="Quận/Huyện" showSearch optionFilterProp="label" disabled={!province} aria-label="Quận"
            options={districts.data?.map((p) => ({ value: p.code, label: p.name }))}
            onChange={() => form.setFieldsValue({ wardCode: undefined })} />
        </Form.Item>
        <Form.Item name="wardCode" rules={[{ required: true, message: 'Chọn phường/xã.' }]}>
          <Select placeholder="Phường/Xã" showSearch optionFilterProp="label" disabled={!district} aria-label="Phường"
            options={wards.data?.map((p) => ({ value: p.code, label: p.name }))} />
        </Form.Item>
        <Form.Item name="street" rules={[{ required: true, message: 'Nhập số nhà, tên đường.' }]}>
          <Input placeholder="Số nhà, tên đường" aria-label="Số nhà, tên đường" />
        </Form.Item>
        <Form.Item name="isPickupDefault" valuePropName="checked" style={{ marginBottom: 4 }}>
          <Checkbox>Kho lấy hàng mặc định</Checkbox>
        </Form.Item>
        <Form.Item name="isReturnDefault" valuePropName="checked">
          <Checkbox>Địa chỉ nhận hàng trả mặc định</Checkbox>
        </Form.Item>
      </Form>
    </Modal>
  )
}

const toBody = (d: Draft): WarehouseBody => ({
  name: d.name.trim(),
  address: { contactName: d.contactName, phone: d.phone, provinceCode: d.provinceCode ?? '', districtCode: d.districtCode ?? '', wardCode: d.wardCode ?? '',
    street: d.street },
  isPickupDefault: d.isPickupDefault,
  isReturnDefault: d.isReturnDefault,
})

/** Thiết lập shop → Kho hàng & vận chuyển: warehouses, return address, đa kho, the shop's carriers and COD. */
const LogisticsPage = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const data = useQuery({ queryKey: ['logistics', shopId], queryFn: () => logisticsApi.get(shopId) })
  const [draft, setDraft] = useState<Draft | null>(null)

  const done = (r: { message: string }) => {
    message.success(r.message)
    setDraft(null)
    void queryClient.invalidateQueries({ queryKey: ['logistics', shopId] })
  }
  const fail = (e: unknown) => message.error(e instanceof ApiError ? e.message : 'Không lưu được.')
  const save = useMutation({
    mutationFn: (d: Draft) => d.id ? logisticsApi.updateWarehouse(shopId, d.id, toBody(d)) : logisticsApi.addWarehouse(shopId, toBody(d)),
    onSuccess: done,
    onError: fail,
  })
  const remove = useMutation({ mutationFn: (id: string) => logisticsApi.removeWarehouse(shopId, id), onSuccess: done, onError: fail })
  const multi = useMutation({ mutationFn: (on: boolean) => logisticsApi.setMultiWarehouse(shopId, on), onSuccess: done, onError: fail })
  const channel = useMutation({
    mutationFn: (c: { code: string; enabled: boolean; codEnabled: boolean }) => logisticsApi.setChannel(shopId, c.code, c),
    onSuccess: done,
    onError: fail,
  })

  if (data.isError) {
    return <Card title="Kho hàng & vận chuyển"><Typography.Text type="danger">{data.error instanceof ApiError ? data.error.message : 'Không tải được.'}</Typography.Text></Card>
  }
  const d = data.data

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Card
        title="Kho hàng & địa chỉ trả hàng"
        loading={data.isLoading}
        extra={
          <Button type="primary" data-testid="warehouse-add" disabled={!d || d.warehouses.length >= d.maxWarehouses}
            onClick={() => setDraft({ name: '', contactName: '', phone: '', street: '', isPickupDefault: false, isReturnDefault: false })}>
            Thêm kho
          </Button>
        }
      >
        <Space align="center" style={{ marginBottom: 12 }}>
          <Switch checked={d?.multiWarehouse ?? false} loading={multi.isPending} data-testid="multi-warehouse"
            onChange={(on) => multi.mutate(on)} disabled={!d} />
          <Typography.Text strong>Đa kho</Typography.Text>
          <Typography.Text type="secondary">
            Bật: mỗi sản phẩm gửi từ kho đã chọn trong trang sửa sản phẩm, đơn có hàng ở nhiều kho được tách thành nhiều kiện (mỗi kho một kiện, phí vận chuyển tính riêng
            từng kiện). Tắt: mọi sản phẩm gửi từ kho lấy hàng mặc định.
          </Typography.Text>
        </Space>
        <Table<Warehouse>
          rowKey="id"
          dataSource={d?.warehouses ?? []}
          pagination={false}
          locale={{ emptyText: 'Chưa có kho hàng' }}
          columns={[
            {
              title: 'Kho',
              render: (_, w) => (
                <Space direction="vertical" size={0}>
                  <Space size={4}>
                    <Typography.Text strong>{w.name}</Typography.Text>
                    {w.isPickupDefault && <Tag color="blue">Lấy hàng mặc định</Tag>}
                    {w.isReturnDefault && <Tag color="purple">Nhận hàng trả</Tag>}
                  </Space>
                  <Typography.Text type="secondary">{w.contactName} · {w.phone}</Typography.Text>
                  <Typography.Text type="secondary">{w.fullAddress}</Typography.Text>
                </Space>
              ),
            },
            { title: 'Sản phẩm chọn kho này', width: 170, render: (_, w) => `${w.productCount} sản phẩm` },
            {
              title: '',
              width: 150,
              render: (_, w) => (
                <Space>
                  <Button size="small" onClick={() => setDraft({ ...w })}>Sửa</Button>
                  {!w.isPickupDefault && !w.isReturnDefault && (
                    <Popconfirm title={`Xoá ${w.name}? Sản phẩm của kho sẽ gửi từ kho mặc định.`} okText="Xoá" cancelText="Không" onConfirm={() => remove.mutate(w.id)}>
                      <Button size="small" danger>Xoá</Button>
                    </Popconfirm>
                  )}
                </Space>
              ),
            },
          ]}
        />
      </Card>

      <Card title="Đơn vị vận chuyển" loading={data.isLoading}>
        <Typography.Paragraph type="secondary">
          Đơn vị tắt sẽ không hiện cho người mua khi thanh toán đơn của shop. Tắt COD thì người mua chọn đơn vị ấy phải thanh toán trước. Từng sản phẩm còn có thể
          giới hạn đơn vị vận chuyển trong trang sửa sản phẩm.
        </Typography.Paragraph>
        <Table<ShippingChannel>
          rowKey="carrierCode"
          dataSource={d?.channels ?? []}
          pagination={false}
          columns={[
            {
              title: 'Đơn vị',
              render: (_, c) => (
                <Space direction="vertical" size={0}>
                  <Space size={4}><Typography.Text strong>{c.name}</Typography.Text>{!c.carrierActive && <Tag>Sàn đang tắt</Tag>}</Space>
                  {c.description && <Typography.Text type="secondary">{c.description}</Typography.Text>}
                </Space>
              ),
            },
            {
              title: 'Sử dụng',
              width: 110,
              render: (_, c) => (
                <Switch checked={c.isEnabled} disabled={!c.carrierActive} aria-label={`Bật ${c.name}`}
                  onChange={(on) => channel.mutate({ code: c.carrierCode, enabled: on, codEnabled: c.codEnabled })} />
              ),
            },
            {
              title: 'Thu hộ (COD)',
              width: 130,
              render: (_, c) => c.carrierSupportsCod ? (
                <Switch checked={c.codEnabled} disabled={!c.isEnabled || !c.carrierActive} aria-label={`COD ${c.name}`}
                  onChange={(on) => channel.mutate({ code: c.carrierCode, enabled: c.isEnabled, codEnabled: on })} />
              ) : <Typography.Text type="secondary">Không hỗ trợ</Typography.Text>,
            },
          ]}
        />
      </Card>

      {draft && <WarehouseForm draft={draft} saving={save.isPending} onSave={(x) => save.mutate(x)} onCancel={() => setDraft(null)} />}
    </Space>
  )
}

export default LogisticsPage
