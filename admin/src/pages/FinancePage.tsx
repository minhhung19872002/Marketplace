import { useState } from 'react'
import { Alert, App, Button, Card, DatePicker, Form, Input, InputNumber, Modal, Select, Space, Statistic, Table, Tabs, Tag, TreeSelect, Typography, Upload } from 'antd'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { FEE_TYPE_LABEL, ISSUE_LABEL, financeApi, type FeeRule, type FeeType, type LedgerEntry, type ReconcileResult, type Withdrawal } from '../api/finance'
import { catalogApi, type CategoryNode } from '../api/catalog'
import { ApiError } from '../api/http'
import { P, can } from '../permissions'
import { formatPercentBp, formatPrice } from '../lib/money'
import { addDaysIso, formatDateTime, vnDayBoundsIso } from '../lib/datetime'

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : fallback)

interface TreeOption { value: string; title: string; children: TreeOption[] }

const toTree = (nodes: CategoryNode[]): TreeOption[] => nodes.map((n) => ({ value: n.id, title: n.name, children: toTree(n.children) }))

const FeeRulesTab = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm<{ categoryId?: string; feeType: FeeType; percent: number; validFrom: Dayjs; note?: string }>()
  const rules = useQuery({ queryKey: ['fee-rules'], queryFn: financeApi.feeRules })
  const categories = useQuery({ queryKey: ['admin-categories'], queryFn: catalogApi.categories })
  const create = useMutation({
    mutationFn: (v: { categoryId?: string; feeType: FeeType; percent: number; validFrom: Dayjs; note?: string }) => financeApi.createFeeRule({
      categoryId: v.feeType === 'Payment' ? null : v.categoryId ?? null, feeType: v.feeType, rateBp: Math.round(v.percent * 100),
      validFrom: v.validFrom.toISOString(), note: v.note?.trim() || null,
    }),
    onSuccess: (r) => {
      message.success(r.message)
      setOpen(false)
      form.resetFields()
      void queryClient.invalidateQueries({ queryKey: ['fee-rules'] })
    },
    onError: (e) => message.error(errorText(e, 'Không lưu được biểu phí.')),
  })
  return (
    <>
      <Space style={{ marginBottom: 12 }}>
        <Button type="primary" onClick={() => setOpen(true)} data-testid="fee-new">Đặt biểu phí mới</Button>
        <Typography.Text type="secondary">Đơn hàng tính phí theo biểu phí có hiệu lực lúc đặt; biểu phí mới chỉ áp dụng từ hôm nay trở đi.</Typography.Text>
      </Space>
      <Table<FeeRule> rowKey="id" loading={rules.isLoading} dataSource={rules.data ?? []} pagination={{ pageSize: 20 }}
        columns={[
          { title: 'Loại phí', dataIndex: 'feeType', render: (t: FeeType) => FEE_TYPE_LABEL[t] },
          { title: 'Phạm vi', dataIndex: 'categoryName', render: (n: string | null) => n ?? 'Mọi ngành hàng' },
          { title: 'Tỉ lệ', dataIndex: 'rateBp', render: (bp: number) => formatPercentBp(bp) },
          { title: 'Hiệu lực từ', dataIndex: 'validFrom', render: formatDateTime },
          { title: 'Đến', dataIndex: 'validTo', render: (v: string | null) => (v ? formatDateTime(v) : '—') },
          { title: '', dataIndex: 'inForce', render: (v: boolean) => v && <Tag color="green">Đang áp dụng</Tag> },
          { title: 'Ghi chú', dataIndex: 'note' },
        ]} />
      <Modal title="Biểu phí mới" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} okText="Lưu" confirmLoading={create.isPending} destroyOnClose>
        <Form form={form} layout="vertical" onFinish={(v) => create.mutate(v)} initialValues={{ feeType: 'Fixed', validFrom: dayjs().add(1, 'day').startOf('day') }}>
          <Form.Item name="feeType" label="Loại phí">
            <Select options={(Object.keys(FEE_TYPE_LABEL) as FeeType[]).map((t) => ({ value: t, label: FEE_TYPE_LABEL[t] }))} />
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(a, b) => a.feeType !== b.feeType}>
            {({ getFieldValue }) => getFieldValue('feeType') !== 'Payment' && (
              <Form.Item name="categoryId" label="Ngành hàng (để trống = mọi ngành)">
                <TreeSelect allowClear treeData={toTree(categories.data ?? [])} showSearch treeNodeFilterProp="title" />
              </Form.Item>
            )}
          </Form.Item>
          <Form.Item name="percent" label="Tỉ lệ (%)" rules={[{ required: true, message: 'Nhập tỉ lệ.' }]}>
            <InputNumber min={0} max={50} step={0.1} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="validFrom" label="Hiệu lực từ" rules={[{ required: true, message: 'Chọn ngày.' }]}>
            <DatePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} disabledDate={(d) => d.isBefore(dayjs().startOf('day'))} />
          </Form.Item>
          <Form.Item name="note" label="Ghi chú"><Input maxLength={200} /></Form.Item>
        </Form>
      </Modal>
    </>
  )
}

const WithdrawalsTab = () => {
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const [status, setStatus] = useState('Pending')
  const [page, setPage] = useState(1)
  const list = useQuery({ queryKey: ['withdrawals', status, page], queryFn: () => financeApi.withdrawals(status, page), placeholderData: keepPreviousData })
  const done = (r: { message: string }) => {
    message.success(r.message)
    void queryClient.invalidateQueries({ queryKey: ['withdrawals'] })
  }
  const approve = useMutation({ mutationFn: financeApi.approve, onSuccess: done, onError: (e) => message.error(errorText(e, 'Không duyệt được.')) })
  const reject = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => financeApi.reject(id, reason),
    onSuccess: done,
    onError: (e) => message.error(errorText(e, 'Không từ chối được.')),
  })
  const askReject = (w: Withdrawal) => {
    let reason = ''
    modal.confirm({
      title: `Từ chối rút ${formatPrice(w.amount)}`,
      content: <Input.TextArea rows={3} onChange={(e) => { reason = e.target.value }} placeholder="Lý do (người rút sẽ thấy)" />,
      okText: 'Từ chối',
      okButtonProps: { danger: true },
      onOk: () => reject.mutateAsync({ id: w.id, reason }),
    })
  }
  return (
    <>
      <Select value={status} onChange={(v) => { setStatus(v); setPage(1) }} style={{ width: 200, marginBottom: 12 }}
        options={[{ value: 'Pending', label: 'Chờ duyệt' }, { value: 'Done', label: 'Đã chuyển' }, { value: 'Rejected', label: 'Từ chối' }]} />
      <Table<Withdrawal> rowKey="id" loading={list.isLoading} dataSource={list.data?.items ?? []}
        pagination={list.data && list.data.totalCount > list.data.pageSize ? { current: page, total: list.data.totalCount, pageSize: list.data.pageSize, onChange: setPage } : false}
        columns={[
          { title: 'Ngày', dataIndex: 'createdAt', render: formatDateTime },
          { title: 'Người rút', render: (_, w) => <>{w.ownerType === 'Shop' ? <Tag>Shop</Tag> : <Tag color="blue">Ví</Tag>}{w.ownerName}</> },
          { title: 'Số tiền', dataIndex: 'amount', render: formatPrice },
          { title: 'Tài khoản', render: (_, w) => `${w.bankCode} ***${w.accountLast4} · ${w.accountName}` },
          { title: 'Trạng thái', render: (_, w) => <>{w.statusLabel}{w.rejectReason && ` — ${w.rejectReason}`}</> },
          {
            title: '',
            render: (_, w) => w.status === 'Pending' && (
              <Space>
                <Button size="small" type="primary" loading={approve.isPending} onClick={() => approve.mutate(w.id)}>Duyệt & chuyển</Button>
                <Button size="small" danger onClick={() => askReject(w)}>Từ chối</Button>
              </Space>
            ),
          },
        ]} />
    </>
  )
}

const LedgerTab = () => {
  const [page, setPage] = useState(1)
  const overview = useQuery({ queryKey: ['ledger'], queryFn: financeApi.ledger })
  const entries = useQuery({ queryKey: ['ledger-entries', page], queryFn: () => financeApi.entries(page), placeholderData: keepPreviousData })
  const check = overview.data?.check
  const healthy = check && check.mismatches.length === 0 && check.unbalancedTransactions === 0 && check.totalDebits === check.totalCredits
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      {check && (healthy
        ? <Alert type="success" showIcon message={`Sổ cái khớp: ${check.accounts} tài khoản, Σ Nợ = Σ Có = ${formatPrice(check.totalDebits)}, số dư tính lại từ bút toán đúng với số dư lưu.`} />
        : <Alert type="error" showIcon message={`Sổ cái lệch: ${check.mismatches.length} tài khoản sai số dư, ${check.unbalancedTransactions} bút toán không cân.`} />)}
      <Space wrap>
        {(overview.data?.platform ?? []).map((a) => <Card key={a.type} size="small"><Statistic title={a.label} value={formatPrice(a.balance)} /></Card>)}
        {(overview.data?.totals ?? []).map((a) => <Card key={`${a.ownerType}${a.type}`} size="small"><Statistic title={`Tổng ${a.label.toLowerCase()}`} value={formatPrice(a.balance)} /></Card>)}
      </Space>
      <Table<LedgerEntry> rowKey="id" size="small" loading={entries.isLoading} dataSource={entries.data?.items ?? []}
        pagination={entries.data && entries.data.totalCount > entries.data.pageSize ? { current: page, total: entries.data.totalCount, pageSize: entries.data.pageSize, onChange: setPage } : false}
        columns={[
          { title: 'Thời gian', dataIndex: 'postedAt', render: formatDateTime },
          { title: 'Loại', dataIndex: 'kind' },
          { title: 'Tài khoản', render: (_, e) => `${e.ownerType} · ${e.accountType}` },
          { title: 'Nợ', render: (_, e) => (e.direction === 'Debit' ? formatPrice(e.amount) : '') },
          { title: 'Có', render: (_, e) => (e.direction === 'Credit' ? formatPrice(e.amount) : '') },
          { title: 'Diễn giải', dataIndex: 'description' },
        ]} />
    </Space>
  )
}

const ReconcileTab = () => {
  const { message } = App.useApp()
  const [provider, setProvider] = useState<'gateway' | 'carrier'>('gateway')
  const [source, setSource] = useState<string | null>(null)
  const [period, setPeriod] = useState<[Dayjs, Dayjs]>([dayjs(), dayjs()])
  const [result, setResult] = useState<ReconcileResult | null>(null)
  const sources = useQuery({ queryKey: ['reconcile-sources'], queryFn: financeApi.sources })
  const choices = (provider === 'gateway' ? sources.data?.gateways : sources.data?.carriers) ?? []
  const chosen = choices.find((c) => c.code === source)
  // [from, to) in Vietnam days, whatever the browser's time zone
  const from = vnDayBoundsIso(period[0].format('YYYY-MM-DD')).from!
  const to = vnDayBoundsIso(addDaysIso(period[1].format('YYYY-MM-DD'), 1)).from!
  const download = async () => {
    if (!source) return
    try {
      const blob = await financeApi.statement(provider, source, from, to)
      const a = document.createElement('a')
      a.href = URL.createObjectURL(blob)
      a.download = `sao-ke-${provider}.csv`
      a.click()
    } catch (e) {
      message.error(errorText(e, 'Không tải được sao kê.'))
    }
  }
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Space wrap>
        <Select value={provider} onChange={(v) => { setProvider(v); setSource(null); setResult(null) }} style={{ width: 260 }}
          options={[{ value: 'gateway', label: 'Cổng thanh toán' }, { value: 'carrier', label: 'Đơn vị vận chuyển — tiền thu hộ COD' }]} />
        <Select value={source} onChange={(v) => { setSource(v); setResult(null) }} style={{ width: 240 }} data-testid="reconcile-source"
          placeholder={provider === 'gateway' ? 'Chọn cổng thanh toán' : 'Chọn đơn vị vận chuyển'} loading={sources.isLoading}
          options={choices.map((c) => ({ value: c.code, label: c.simulated ? `${c.name} (giả lập)` : c.name }))} />
        <DatePicker.RangePicker value={period} onChange={(v) => v?.[0] && v[1] && setPeriod([v[0], v[1]])} format="DD/MM/YYYY" allowClear={false} />
        {chosen?.simulated && <Button onClick={download}>Tải sao kê của nhà cung cấp giả lập</Button>}
        <Upload accept=".csv,text/csv" showUploadList={false} disabled={!source} beforeUpload={async (file) => {
          if (!source) return false
          try {
            setResult(await financeApi.reconcile(provider, source, from, to, file))
          } catch (e) {
            message.error(errorText(e, 'Đối soát không thành công.'))
          }
          return false
        }}>
          <Button type="primary" disabled={!source}>Tải tệp lên & đối soát</Button>
        </Upload>
      </Space>
      {result && (
        <>
          <Alert type={result.issues.length === 0 ? 'success' : 'warning'} showIcon
            message={`${result.statementLines} dòng trong tệp, khớp ${result.matched}; ${result.issues.length} chênh lệch. Tổng tệp ${formatPrice(result.statementTotal)} · tổng ShopHub ${formatPrice(result.systemTotal)}${result.statementFees > 0 ? ` · phí cổng ${formatPrice(result.statementFees)}` : ''}.`} />
          <Table rowKey={(r) => `${r.reference}-${r.issue}`} size="small" dataSource={result.issues} pagination={{ pageSize: 50 }}
            columns={[
              { title: 'Mã giao dịch / vận đơn', dataIndex: 'reference' },
              { title: 'Chênh lệch', dataIndex: 'issue', render: (v: string) => <Tag color="red">{ISSUE_LABEL[v] ?? v}</Tag> },
              { title: 'Theo tệp', dataIndex: 'providerAmount', render: (v: number | null) => (v === null ? '' : formatPrice(v)) },
              { title: 'Theo ShopHub', dataIndex: 'systemAmount', render: (v: number | null) => (v === null ? '' : formatPrice(v)) },
              { title: 'Ghi chú', dataIndex: 'note' },
            ]} />
        </>
      )}
    </Space>
  )
}

/** VI.7 Tài chính: biểu phí theo ngành, duyệt rút tiền, sổ cái, đối soát với cổng và hãng. Tabs follow the admin's permissions. */
const FinancePage = ({ permissions }: { permissions: string[] }) => {
  const tabs = [
    can(permissions, P.FinanceLedgerView) && { key: 'ledger', label: 'Sổ cái', children: <LedgerTab /> },
    can(permissions, P.FinanceWithdrawalApprove) && { key: 'withdrawals', label: 'Rút tiền', children: <WithdrawalsTab /> },
    can(permissions, P.FinanceFeeManage) && { key: 'fees', label: 'Biểu phí', children: <FeeRulesTab /> },
    can(permissions, P.FinanceReconcile) && { key: 'reconcile', label: 'Đối soát', children: <ReconcileTab /> },
  ].filter(Boolean) as { key: string; label: string; children: JSX.Element }[]
  return <Card title="Tài chính"><Tabs items={tabs} destroyInactiveTabPane /></Card>
}

export default FinancePage
