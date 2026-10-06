import { useState } from 'react'
import { App, Button, Card, Checkbox, Col, DatePicker, Form, Input, InputNumber, Modal, Row, Select, Space, Statistic, Table, Tabs, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { BANKS, financeApi, type Earning, type LedgerLine, type Withdrawal } from '../api/finance'
import { ApiError } from '../api/http'
import { formatPrice } from '../lib/money'
import { formatDateTime } from '../lib/datetime'

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : fallback)

const save = (blob: Blob, name: string) => {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  a.click()
  setTimeout(() => URL.revokeObjectURL(url), 60_000)
}

const earningColumns = (released: boolean) => [
  { title: 'Mã đơn', dataIndex: 'orderCode' },
  released
    ? { title: 'Giải ngân lúc', dataIndex: 'releasedAt', render: (v: string | null) => (v ? formatDateTime(v) : '') }
    : {
        title: 'Dự kiến giải ngân',
        render: (_: unknown, e: Earning) => e.hasOpenReturn ? <Tag color="orange">Đang có yêu cầu trả hàng</Tag> : e.releaseAfter ? formatDateTime(e.releaseAfter) : '',
      },
  { title: 'Tiền hàng', dataIndex: 'goods', render: formatPrice },
  { title: 'Giảm giá shop', dataIndex: 'shopDiscount', render: (v: number) => (v ? `−${formatPrice(v)}` : '') },
  { title: 'Hoàn tiền', dataIndex: 'refundsBorne', render: (v: number) => (v ? `−${formatPrice(v)}` : '') },
  { title: 'Phí cố định', dataIndex: 'fixedFee', render: (v: number) => `−${formatPrice(v)}` },
  { title: 'Phí thanh toán', dataIndex: 'paymentFee', render: (v: number) => `−${formatPrice(v)}` },
  { title: 'Phí dịch vụ', dataIndex: 'serviceFee', render: (v: number) => (v ? `−${formatPrice(v)}` : '') },
  { title: 'Thực nhận', dataIndex: 'net', render: (v: number) => <Typography.Text strong data-testid="earning-net">{formatPrice(v)}</Typography.Text> },
]

/** Tài chính (spec III.7): doanh thu chờ / đã giải ngân, số dư, rút tiền, tài khoản ngân hàng, đối soát & hoá đơn phí. */
const FinancePage = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [tab, setTab] = useState('pending')
  const [page, setPage] = useState(1)
  const [withdrawOpen, setWithdrawOpen] = useState(false)
  const [bankOpen, setBankOpen] = useState(false)
  const [otpWait, setOtpWait] = useState(false)
  const [period, setPeriod] = useState<[Dayjs, Dayjs]>([dayjs().startOf('month'), dayjs()])
  const [withdrawForm] = Form.useForm<{ bankAccountId: string; amount: number }>()
  const [bankForm] = Form.useForm<{ bankCode: string; accountNo: string; accountName: string; otpCode: string; makeDefault: boolean }>()

  const summary = useQuery({ queryKey: ['finance', shopId, 'summary'], queryFn: () => financeApi.summary(shopId) })
  const list = useQuery({
    queryKey: ['finance', shopId, tab, page],
    queryFn: async (): Promise<{ items: unknown[]; totalCount: number; pageSize: number }> =>
      tab === 'pending' ? financeApi.pending(shopId, page)
        : tab === 'released' ? financeApi.released(shopId, page)
          : tab === 'withdrawals' ? financeApi.withdrawals(shopId, page)
            : financeApi.transactions(shopId, page),
    // Keep the previous page only within the same tab: rows of another tab have other fields (the table would crash on them)
    placeholderData: (previous, previousQuery) => (previousQuery?.queryKey[2] === tab ? previous : undefined),
    enabled: tab !== 'reports',
  })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['finance', shopId] })

  const withdraw = useMutation({
    mutationFn: (v: { bankAccountId: string; amount: number }) => financeApi.withdraw(shopId, v.bankAccountId, v.amount),
    onSuccess: (r) => {
      message.success(r.data.status === 'Done' ? `Đã chuyển ${formatPrice(r.data.amount)} về ngân hàng.` : r.message)
      setWithdrawOpen(false)
      withdrawForm.resetFields()
      refresh()
    },
    onError: (e) => message.error(errorText(e, 'Không rút được tiền.')),
  })
  const addBank = useMutation({
    mutationFn: (v: { bankCode: string; accountNo: string; accountName: string; otpCode: string; makeDefault: boolean }) => financeApi.addBank(shopId, v),
    onSuccess: (r) => {
      message.success(r.message)
      setBankOpen(false)
      bankForm.resetFields()
      refresh()
    },
    onError: (e) => message.error(errorText(e, 'Không thêm được tài khoản.')),
  })

  const s = summary.data
  const [from, to] = [period[0].startOf('day').toISOString(), period[1].add(1, 'day').startOf('day').toISOString()]
  const stamp = `${period[0].format('YYYYMMDD')}-${period[1].format('YYYYMMDD')}`
  const report = async (kind: 'Xlsx' | 'Pdf' | 'Invoice') => {
    try {
      if (kind === 'Invoice') save(await financeApi.feeInvoice(shopId, from, to), `hoa-don-phi-${stamp}.pdf`)
      else save(await financeApi.report(shopId, from, to, kind), `doi-soat-${stamp}.${kind === 'Pdf' ? 'pdf' : 'xlsx'}`)
    } catch (e) {
      message.error(errorText(e, 'Không tải được báo cáo.'))
    }
  }

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Row gutter={16}>
        <Col xs={24} md={6}><Card><Statistic title="Chờ giải ngân" value={formatPrice(s?.pending ?? 0)} data-testid="finance-pending" /></Card></Col>
        <Col xs={24} md={6}><Card><Statistic title="Số dư khả dụng" value={formatPrice(s?.available ?? 0)} data-testid="finance-available" /></Card></Col>
        <Col xs={24} md={6}><Card><Statistic title="Đang rút" value={formatPrice(s?.withdrawing ?? 0)} /></Card></Col>
        <Col xs={24} md={6}>
          <Card>
            <Space direction="vertical">
              <Button type="primary" disabled={!s || s.available < s.withdrawMin || s.bankAccounts.every((b) => !b.verified)}
                onClick={() => setWithdrawOpen(true)} data-testid="finance-withdraw">Rút tiền</Button>
              <Button onClick={() => setBankOpen(true)} data-testid="finance-add-bank">Thêm tài khoản ngân hàng</Button>
              {s && <Typography.Text type="secondary">Tối thiểu {formatPrice(s.withdrawMin)} · tối đa {s.withdrawPerWeek} lần / 7 ngày</Typography.Text>}
            </Space>
          </Card>
        </Col>
      </Row>

      <Card>
        <Tabs activeKey={tab} onChange={(k) => { setTab(k); setPage(1) }} items={[
          { key: 'pending', label: 'Chờ giải ngân' },
          { key: 'released', label: 'Đã giải ngân' },
          { key: 'withdrawals', label: 'Lịch sử rút tiền' },
          { key: 'transactions', label: 'Biến động số dư' },
          { key: 'reports', label: 'Đối soát & hoá đơn' },
        ]} />
        {tab === 'reports' ? (
          <Space wrap>
            <DatePicker.RangePicker value={period} onChange={(v) => v?.[0] && v[1] && setPeriod([v[0], v[1]])} format="DD/MM/YYYY" allowClear={false} />
            <Button onClick={() => report('Xlsx')} data-testid="report-xlsx">Tải Excel đối soát</Button>
            <Button onClick={() => report('Pdf')}>Tải PDF đối soát</Button>
            <Button onClick={() => report('Invoice')}>Hoá đơn phí sàn (PDF)</Button>
          </Space>
        ) : tab === 'withdrawals' ? (
          <Table<Withdrawal> rowKey="id" loading={list.isLoading} dataSource={(list.data?.items ?? []) as Withdrawal[]}
            pagination={list.data && list.data.totalCount > list.data.pageSize ? { current: page, total: list.data.totalCount, pageSize: list.data.pageSize, onChange: setPage } : false}
            columns={[
              { title: 'Ngày', dataIndex: 'createdAt', render: formatDateTime },
              { title: 'Số tiền', dataIndex: 'amount', render: formatPrice },
              { title: 'Tài khoản', render: (_, w) => `${w.bankCode} ***${w.accountLast4}` },
              { title: 'Trạng thái', render: (_, w) => <Tag color={w.status === 'Done' ? 'green' : w.status === 'Rejected' ? 'red' : 'gold'} data-testid="withdrawal-status">{w.statusLabel}</Tag> },
              { title: 'Ghi chú', render: (_, w) => w.rejectReason ?? w.bankRef ?? '' },
            ]} />
        ) : tab === 'transactions' ? (
          <Table<LedgerLine> rowKey="id" loading={list.isLoading} dataSource={(list.data?.items ?? []) as LedgerLine[]}
            pagination={list.data && list.data.totalCount > list.data.pageSize ? { current: page, total: list.data.totalCount, pageSize: list.data.pageSize, onChange: setPage } : false}
            columns={[
              { title: 'Thời gian', dataIndex: 'postedAt', render: formatDateTime },
              { title: 'Nội dung', dataIndex: 'description' },
              { title: 'Số tiền', render: (_, l) => `${l.direction === 'Credit' ? '+' : '−'}${formatPrice(l.amount)}` },
            ]} />
        ) : (
          <Table<Earning> rowKey="orderId" loading={list.isLoading} dataSource={(list.data?.items ?? []) as Earning[]} scroll={{ x: 1100 }}
            locale={{ emptyText: tab === 'pending' ? 'Chưa có đơn chờ giải ngân' : 'Chưa có đơn đã giải ngân' }}
            pagination={list.data && list.data.totalCount > list.data.pageSize ? { current: page, total: list.data.totalCount, pageSize: list.data.pageSize, onChange: setPage } : false}
            columns={earningColumns(tab === 'released')} />
        )}
      </Card>

      <Modal title="Rút tiền về ngân hàng" open={withdrawOpen} onCancel={() => setWithdrawOpen(false)} onOk={() => withdrawForm.submit()}
        okText="Rút tiền" confirmLoading={withdraw.isPending} destroyOnClose>
        <Form form={withdrawForm} layout="vertical" onFinish={(v) => withdraw.mutate(v)}
          initialValues={{ bankAccountId: s?.bankAccounts.find((b) => b.isDefault && b.verified)?.id ?? s?.bankAccounts.find((b) => b.verified)?.id }}>
          <Form.Item name="bankAccountId" label="Tài khoản nhận" rules={[{ required: true, message: 'Chọn tài khoản.' }]}>
            <Select options={(s?.bankAccounts ?? []).filter((b) => b.verified).map((b) => ({ value: b.id, label: `${b.bankCode} ***${b.accountNoLast4} · ${b.accountName}` }))} />
          </Form.Item>
          <Form.Item name="amount" label={`Số tiền (khả dụng ${formatPrice(s?.available ?? 0)})`}
            rules={[{ required: true, message: 'Nhập số tiền.' }]}>
            <InputNumber min={s?.withdrawMin ?? 1} max={s?.available} step={10_000} style={{ width: '100%' }} data-testid="withdraw-amount" />
          </Form.Item>
          {s && s.autoApproveMax > 0 && <Typography.Text type="secondary">Lệnh đến {formatPrice(s.autoApproveMax)} được chuyển ngay; lớn hơn chờ sàn duyệt.</Typography.Text>}
        </Form>
      </Modal>

      <Modal title="Thêm tài khoản ngân hàng" open={bankOpen} onCancel={() => setBankOpen(false)} onOk={() => bankForm.submit()} okText="Thêm"
        confirmLoading={addBank.isPending} destroyOnClose>
        <Form form={bankForm} layout="vertical" onFinish={(v) => addBank.mutate({ ...v, makeDefault: !!v.makeDefault })} initialValues={{ bankCode: BANKS[0] }}>
          <Form.Item name="bankCode" label="Ngân hàng"><Select options={BANKS.map((b) => ({ value: b, label: b }))} /></Form.Item>
          <Form.Item name="accountNo" label="Số tài khoản" rules={[{ required: true, pattern: /^\d{6,20}$/, message: '6–20 chữ số.' }]}><Input /></Form.Item>
          <Form.Item name="accountName" label="Tên chủ tài khoản" rules={[{ required: true, message: 'Nhập tên chủ tài khoản.' }]}><Input /></Form.Item>
          <Form.Item label="Mã xác thực gửi tới số điện thoại của bạn" required>
            <Space.Compact style={{ width: '100%' }}>
              <Form.Item name="otpCode" noStyle rules={[{ required: true, pattern: /^\d{6}$/, message: 'Mã gồm 6 chữ số.' }]}><Input maxLength={6} /></Form.Item>
              <Button disabled={otpWait} onClick={async () => {
                try {
                  const r = await financeApi.otp(shopId)
                  message.success(r.message)
                  setOtpWait(true)
                  setTimeout(() => setOtpWait(false), r.data.resendAfterSeconds * 1000)
                } catch (e) {
                  message.error(errorText(e, 'Không gửi được mã.'))
                }
              }}>Gửi mã</Button>
            </Space.Compact>
          </Form.Item>
          <Form.Item name="makeDefault" valuePropName="checked"><Checkbox>Đặt làm tài khoản mặc định</Checkbox></Form.Item>
        </Form>
      </Modal>
    </Space>
  )
}

export default FinancePage
