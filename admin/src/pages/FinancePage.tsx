import { useState } from 'react'
import { Alert, App, Button, Card, Col, DatePicker, Form, Input, InputNumber, Modal, Row, Segmented, Select, Space, Tabs, TreeSelect, Typography, Upload } from 'antd'
import { CheckOutlined, CloseOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons'
import { useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import dayjs, { type Dayjs } from 'dayjs'
import { FEE_TYPE_LABEL, ISSUE_LABEL, financeApi, type FeeRule, type FeeType, type LedgerEntry, type ReconcileResult, type Withdrawal } from '../api/finance'
import { catalogApi, type CategoryNode } from '../api/catalog'
import { ApiError } from '../api/http'
import { P, can } from '../permissions'
import { formatPercentBp, formatPrice } from '../lib/money'
import { addDaysIso, formatDateTime, vnDayBoundsIso, vnTodayIso, vnWallTimeIso } from '../lib/datetime'
import { ON_OFF, WITHDRAWAL_STATUS } from '../lib/status'
import DataTable from '../components/DataTable'
import PageHeader from '../components/PageHeader'
import RowActions from '../components/RowActions'
import StatusTag, { ToneTag } from '../components/StatusTag'

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
      validFrom: vnWallTimeIso(v.validFrom.format('YYYY-MM-DDTHH:mm')), note: v.note?.trim() || null,
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
      <DataTable<FeeRule> rowKey="id" loading={rules.isPending} error={rules.error} dataSource={rules.data ?? []} paging="client"
        filters={<Typography.Text type="secondary">Đơn hàng tính phí theo biểu phí có hiệu lực lúc đặt; biểu phí mới chỉ áp dụng từ hôm nay trở đi.</Typography.Text>}
        actions={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => setOpen(true)} data-testid="fee-new">Đặt biểu phí mới</Button>}
        emptyText="Chưa có biểu phí nào"
        columns={[
          { title: 'Loại phí', dataIndex: 'feeType', render: (t: FeeType) => FEE_TYPE_LABEL[t] },
          { title: 'Phạm vi', dataIndex: 'categoryName', render: (n: string | null) => n ?? 'Mọi ngành hàng' },
          { title: 'Tỉ lệ', dataIndex: 'rateBp', align: 'right', render: (bp: number) => formatPercentBp(bp) },
          { title: 'Hiệu lực từ', dataIndex: 'validFrom', render: (v: string) => <span className="cell-nowrap">{formatDateTime(v)}</span> },
          { title: 'Đến', dataIndex: 'validTo', render: (v: string | null) => <span className="cell-nowrap">{v ? formatDateTime(v) : '—'}</span> },
          { title: 'Trạng thái', dataIndex: 'inForce', render: (v: boolean) => (v ? <StatusTag map={ON_OFF} value="on" label="Đang áp dụng" /> : null) },
          { title: 'Ghi chú', dataIndex: 'note' },
        ]} />
      <Modal title="Biểu phí mới" open={open} onCancel={() => setOpen(false)} onOk={() => form.submit()} okText="Lưu" confirmLoading={create.isPending} destroyOnClose>
        <Form form={form} layout="vertical" onFinish={(v) => create.mutate(v)} initialValues={{ feeType: 'Fixed', validFrom: dayjs(`${addDaysIso(vnTodayIso(), 1)}T00:00`) }}>
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
          <Form.Item name="validFrom" label="Hiệu lực từ (giờ Việt Nam)" rules={[{ required: true, message: 'Chọn ngày.' }]}>
            <DatePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} disabledDate={(d) => d.format('YYYY-MM-DD') < vnTodayIso()} />
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
      <DataTable<Withdrawal> rowKey="id" loading={list.isPending} fetching={list.isFetching && !list.isPending} error={list.error}
        dataSource={list.data?.items ?? []}
        filters={(
          <Segmented value={status} onChange={(v) => { setStatus(String(v)); setPage(1) }}
            options={[{ value: 'Pending', label: 'Chờ duyệt' }, { value: 'Done', label: 'Đã chuyển' }, { value: 'Rejected', label: 'Từ chối' }]} />
        )}
        emptyText={status === 'Pending' ? 'Không có yêu cầu rút tiền nào chờ duyệt' : 'Không có yêu cầu nào'}
        paging={{ page, pageSize: list.data?.pageSize ?? 20, total: list.data?.totalCount, onChange: setPage }}
        columns={[
          { title: 'Ngày', dataIndex: 'createdAt', render: (v: string) => <span className="cell-nowrap">{formatDateTime(v)}</span> },
          { title: 'Người rút', render: (_, w) => <Space size={6}>{w.ownerType === 'Shop' ? <ToneTag tone="default">Shop</ToneTag> : <ToneTag tone="info">Ví</ToneTag>}{w.ownerName}</Space> },
          { title: 'Số tiền', dataIndex: 'amount', align: 'right', render: (v: number) => <span className="cell-money">{formatPrice(v)}</span> },
          { title: 'Tài khoản', render: (_, w) => <><span className="cell-nowrap">{w.bankCode} ***{w.accountLast4}</span><span className="cell-sub">{w.accountName}</span></> },
          { title: 'Trạng thái', render: (_, w) => <><StatusTag map={WITHDRAWAL_STATUS} value={w.status} label={w.statusLabel} />{w.rejectReason && <span className="cell-sub">{w.rejectReason}</span>}</> },
          {
            title: '', key: 'actions', align: 'right',
            render: (_, w) => w.status === 'Pending' && (
              <RowActions name={`${w.ownerName} ${formatPrice(w.amount)}`}
                primary={(
                  <Button size="small" type="primary" icon={<CheckOutlined aria-hidden />} loading={approve.isPending && approve.variables === w.id}
                    onClick={() => modal.confirm({
                      title: `Duyệt và chuyển ${formatPrice(w.amount)}?`, content: `Tới ${w.bankCode} ***${w.accountLast4} · ${w.accountName}`,
                      okText: 'Duyệt & chuyển', cancelText: 'Không', onOk: () => approve.mutateAsync(w.id).catch(() => undefined),
                    })}>Duyệt & chuyển</Button>
                )}
                // Opens a dialog with the reason box: that is the confirmation
                items={[{ key: 'reject', icon: <CloseOutlined aria-hidden />, label: 'Từ chối', danger: true, onClick: () => askReject(w) }]} />
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
      <Row gutter={[12, 12]}>
        {[
          ...(overview.data?.platform ?? []).map((a) => ({ key: a.type, title: a.label, value: a.balance })),
          ...(overview.data?.totals ?? []).map((a) => ({ key: `${a.ownerType}${a.type}`, title: `Tổng ${a.label.toLowerCase()}`, value: a.balance })),
        ].map((c) => (
          <Col key={c.key} xs={24} md={12} xl={6}>
            <Card size="small" className="stat-card">
              <span className="stat-card-title">{c.title}</span>
              <div className="stat-card-value" style={{ fontSize: 20 }}>{formatPrice(c.value)}</div>
            </Card>
          </Col>
        ))}
      </Row>
      <DataTable<LedgerEntry> rowKey="id" size="small" loading={entries.isPending} fetching={entries.isFetching && !entries.isPending} error={entries.error}
        dataSource={entries.data?.items ?? []} emptyText="Chưa có bút toán nào"
        paging={{ page, pageSize: entries.data?.pageSize ?? 50, total: entries.data?.totalCount, onChange: setPage }}
        columns={[
          { title: 'Thời gian', dataIndex: 'postedAt', render: (v: string) => <span className="cell-nowrap">{formatDateTime(v)}</span> },
          { title: 'Loại', dataIndex: 'kind' },
          { title: 'Tài khoản', render: (_, e) => `${e.ownerType} · ${e.accountType}` },
          { title: 'Nợ', align: 'right', render: (_, e) => <span className="cell-money">{e.direction === 'Debit' ? formatPrice(e.amount) : ''}</span> },
          { title: 'Có', align: 'right', render: (_, e) => <span className="cell-money">{e.direction === 'Credit' ? formatPrice(e.amount) : ''}</span> },
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
          <Button type="primary" icon={<UploadOutlined aria-hidden />} disabled={!source}>Tải tệp lên & đối soát</Button>
        </Upload>
      </Space>
      {result && (
        <>
          <Alert type={result.issues.length === 0 ? 'success' : 'warning'} showIcon
            message={`${result.statementLines} dòng trong tệp, khớp ${result.matched}; ${result.issues.length} chênh lệch. Tổng tệp ${formatPrice(result.statementTotal)} · tổng ShopHub ${formatPrice(result.systemTotal)}${result.statementFees > 0 ? ` · phí cổng ${formatPrice(result.statementFees)}` : ''}.`} />
          <DataTable<ReconcileResult['issues'][number]> rowKey={(r) => `${r.reference}-${r.issue}`} size="small" dataSource={result.issues} paging="client"
            emptyText="Không có chênh lệch"
            columns={[
              { title: 'Mã giao dịch / vận đơn', dataIndex: 'reference' },
              { title: 'Chênh lệch', dataIndex: 'issue', render: (v: string) => <ToneTag tone="error">{ISSUE_LABEL[v] ?? v}</ToneTag> },
              { title: 'Theo tệp', dataIndex: 'providerAmount', align: 'right', render: (v: number | null) => (v === null ? '' : formatPrice(v)) },
              { title: 'Theo ShopHub', dataIndex: 'systemAmount', align: 'right', render: (v: number | null) => (v === null ? '' : formatPrice(v)) },
              { title: 'Ghi chú', dataIndex: 'note' },
            ]} />
        </>
      )}
    </Space>
  )
}

/** VI.7 Tài chính: biểu phí theo ngành, duyệt rút tiền, sổ cái, đối soát với cổng và hãng. Tabs follow the admin's permissions; the tab lives in the URL (?tab=). */
const FinancePage = ({ permissions }: { permissions: string[] }) => {
  const [params, setParams] = useSearchParams()
  const tabs = [
    can(permissions, P.FinanceLedgerView) && { key: 'ledger', label: 'Sổ cái', children: <LedgerTab /> },
    can(permissions, P.FinanceWithdrawalApprove) && { key: 'withdrawals', label: 'Rút tiền', children: <WithdrawalsTab /> },
    can(permissions, P.FinanceFeeManage) && { key: 'fees', label: 'Biểu phí', children: <FeeRulesTab /> },
    can(permissions, P.FinanceReconcile) && { key: 'reconcile', label: 'Đối soát', children: <ReconcileTab /> },
  ].filter(Boolean) as { key: string; label: string; children: JSX.Element }[]
  const tab = tabs.find((t) => t.key === params.get('tab'))?.key ?? tabs[0]?.key
  return (
    <>
      <PageHeader title="Tài chính" description="Sổ cái kép của sàn, duyệt rút tiền, biểu phí theo ngành và đối soát với cổng thanh toán, đơn vị vận chuyển" />
      <Card className="tabs-card">
        <Tabs items={tabs} destroyInactiveTabPane activeKey={tab} onChange={(k) => setParams(k === tabs[0]?.key ? {} : { tab: k })} />
      </Card>
    </>
  )
}

export default FinancePage
