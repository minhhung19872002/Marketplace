import { useState } from 'react'
import { App, Button, Descriptions, Drawer, Form, Image, Input, InputNumber, Radio, Segmented, Space, Switch, Table, Tag, Timeline } from 'antd'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { aftercareApi, type DisputeInfo } from '../api/aftercare'
import { ApiError } from '../api/http'
import { formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'
import { RETURN_STATUS } from '../lib/status'
import DataTable from '../components/DataTable'
import RowActions from '../components/RowActions'
import StatusTag from '../components/StatusTag'

interface DecideValues {
  decision: 'FavorBuyer' | 'FavorShop'
  reason: string
  refundAmount?: number
  requireReturn: boolean
}

const partyLabel = { Buyer: 'Người mua', Shop: 'Shop', Admin: 'Sàn' }

const DisputeDrawer = ({ item, onClose }: { item: DisputeInfo; onClose: () => void }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<DecideValues>()
  const decide = useMutation({
    mutationFn: (v: DecideValues) => aftercareApi.decide(item.id, {
      decision: v.decision,
      reason: v.reason.trim(),
      refundAmount: v.decision === 'FavorBuyer' ? v.refundAmount ?? null : null,
      requireReturn: v.decision === 'FavorBuyer' && v.requireReturn,
    }),
    onSuccess: (r) => {
      message.success(r.message)
      void queryClient.invalidateQueries({ queryKey: ['disputes'] })
      onClose()
    },
    onError: (e) => message.error(e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : 'Không phân xử được.'),
  })
  const open = item.status === 'Disputed'
  return (
    <Drawer open width={680} onClose={onClose} title={`Khiếu nại ${item.code}`} destroyOnClose>
      <Descriptions column={1} size="small" bordered>
        <Descriptions.Item label="Đơn / Shop">{item.orderCode} · {item.shopName}</Descriptions.Item>
        <Descriptions.Item label="Hình thức">{item.type === 'RefundOnly' ? 'Chỉ hoàn tiền' : 'Trả hàng & hoàn tiền'}</Descriptions.Item>
        <Descriptions.Item label="Mô tả của người mua">{item.description}</Descriptions.Item>
        <Descriptions.Item label="Lý do khiếu nại">{item.disputeReason}</Descriptions.Item>
        <Descriptions.Item label="Phản hồi của shop">{item.shopNote ?? '—'}</Descriptions.Item>
        <Descriptions.Item label="Yêu cầu hoàn">{formatPrice(item.requestedAmount)}</Descriptions.Item>
        {item.offeredAmount !== null && <Descriptions.Item label="Shop đề nghị">{formatPrice(item.offeredAmount)}</Descriptions.Item>}
        {item.disputeDecision && (
          <Descriptions.Item label="Kết quả">{item.disputeDecision === 'FavorBuyer' ? 'Người mua đúng' : 'Shop đúng'} — {item.disputeDecisionReason}</Descriptions.Item>
        )}
      </Descriptions>
      <Table style={{ marginTop: 16 }} size="small" rowKey="orderItemId" pagination={false} dataSource={item.items}
        columns={[
          { title: 'Sản phẩm', render: (_, i) => `${i.name}${i.variant ? ` (${i.variant})` : ''}` },
          { title: 'SL', dataIndex: 'quantity' },
          { title: 'Hoàn tối đa', dataIndex: 'refundAmount', render: (v: number) => formatPrice(v) },
        ]} />
      <Image.PreviewGroup>
        <Space wrap style={{ marginTop: 16 }}>
          {item.evidence.map((e, i) => (
            <Space key={i} direction="vertical" size={0}>
              {e.type === 'Image'
                ? <Image src={e.url} width={88} height={88} style={{ objectFit: 'cover' }} />
                : <video src={e.url} width={160} controls preload="metadata" />}
              <Tag>{partyLabel[e.party]}</Tag>
            </Space>
          ))}
        </Space>
      </Image.PreviewGroup>
      {open && (
        <Form<DecideValues> form={form} layout="vertical" style={{ marginTop: 16 }} onFinish={(v) => decide.mutate(v)}
          initialValues={{ decision: 'FavorBuyer', requireReturn: item.type === 'ReturnAndRefund' }} data-testid="decide-form">
          <Form.Item name="decision" label="Phán quyết">
            <Radio.Group options={[{ value: 'FavorBuyer', label: 'Người mua đúng — hoàn tiền' }, { value: 'FavorShop', label: 'Shop đúng — đóng yêu cầu' }]} />
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(a, b) => a.decision !== b.decision}>
            {({ getFieldValue }) => getFieldValue('decision') === 'FavorBuyer' && (
              <Space>
                <Form.Item name="refundAmount" label={`Số tiền hoàn (để trống = ${formatPrice(item.requestedAmount)})`}>
                  <InputNumber min={1000} max={item.requestedAmount} step={1000} style={{ width: 220 }} data-testid="decide-amount" />
                </Form.Item>
                <Form.Item name="requireReturn" label="Yêu cầu gửi hàng về" valuePropName="checked">
                  <Switch />
                </Form.Item>
              </Space>
            )}
          </Form.Item>
          <Form.Item name="reason" label="Lý do phán quyết" rules={[{ required: true, whitespace: true, message: 'Vui lòng nhập lý do.' }]}>
            <Input.TextArea rows={3} maxLength={1000} data-testid="decide-reason" />
          </Form.Item>
          <Button type="primary" htmlType="submit" loading={decide.isPending} data-testid="decide-submit">Phân xử</Button>
        </Form>
      )}
      <Timeline style={{ marginTop: 24 }} items={item.history.map((h) => ({
        children: <><strong>{h.label}</strong> · {formatDateTime(h.occurredAt)}{h.note && <div>{h.note}</div>}</>,
      }))} />
    </Drawer>
  )
}

/** Khiếu nại trả hàng: the platform decides; a refund follows the buyer's paid share after discounts. */
const DisputesPage = () => {
  const [open, setOpen] = useState(true)
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<string | null>(null)
  const list = useQuery({ queryKey: ['disputes', open, page], queryFn: () => aftercareApi.disputes(open, page), placeholderData: keepPreviousData })
  const item = list.data?.items.find((d) => d.id === selected)
  return (
    <>
      <DataTable<DisputeInfo>
        header={{ title: 'Khiếu nại trả hàng', description: 'Sàn phân xử khi shop từ chối yêu cầu trả hàng / hoàn tiền; tiền hoàn theo phần người mua đã trả sau giảm giá' }}
        filters={(
          <Segmented value={open ? 'open' : 'closed'} onChange={(k) => { setOpen(k === 'open'); setPage(1) }}
            options={[{ value: 'open', label: 'Chờ phân xử' }, { value: 'closed', label: 'Đã phân xử' }]} />
        )}
        rowKey="id"
        loading={list.isPending}
        fetching={list.isFetching && !list.isPending}
        error={list.error}
        dataSource={list.data?.items ?? []}
        emptyText={open ? 'Không có khiếu nại nào chờ phân xử' : 'Chưa có khiếu nại nào được phân xử'}
        onRow={(d) => ({ onClick: () => setSelected(d.id), className: 'ant-table-row-clickable' })}
        paging={{ page, pageSize: list.data?.pageSize ?? 20, total: list.data?.totalCount, onChange: setPage }}
        columns={[
          { title: 'Mã', dataIndex: 'code', render: (c: string) => <span className="cell-main" data-testid="dispute-row">{c}</span> },
          { title: 'Đơn / shop', render: (_, d) => <><span>{d.orderCode}</span><span className="cell-sub">{d.shopName}</span></> },
          { title: 'Hình thức', dataIndex: 'type', render: (t: DisputeInfo['type']) => (t === 'RefundOnly' ? 'Chỉ hoàn tiền' : 'Trả hàng & hoàn tiền') },
          { title: 'Yêu cầu hoàn', dataIndex: 'requestedAmount', align: 'right', render: (v: number) => <span className="cell-money">{formatPrice(v)}</span> },
          { title: 'Trạng thái', dataIndex: 'status', render: (s: string, d) => <StatusTag map={RETURN_STATUS} value={s} label={d.statusLabel} /> },
          { title: 'Ngày tạo', dataIndex: 'createdAt', render: (v: string) => <span className="cell-nowrap">{formatDateTime(v)}</span> },
          {
            title: '', key: 'actions', align: 'right',
            render: (_, d) => (
              <RowActions name={d.code}
                primary={<Button size="small" type={d.status === 'Disputed' ? 'primary' : 'default'} onClick={() => setSelected(d.id)}>
                  {d.status === 'Disputed' ? 'Phân xử' : 'Xem'}
                </Button>} />
            ),
          },
        ]}
      />
      {item && <DisputeDrawer item={item} onClose={() => setSelected(null)} />}
    </>
  )
}

export default DisputesPage
