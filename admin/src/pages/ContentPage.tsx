import { useState } from 'react'
import { App, Breadcrumb, Button, Card, Form, Input, InputNumber, Modal, Select, Space, Switch, Tabs, Tag, Typography } from 'antd'
import { EditOutlined, ExportOutlined, PlusOutlined } from '@ant-design/icons'
import { useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { platformApi, type CmsKind, type CmsPage, type MessageTemplate } from '../api/platform'
import { formatDateTime } from '../lib/datetime'
import { formatNumber } from '../lib/money'
import { ON_OFF } from '../lib/status'
import DataTable from '../components/DataTable'
import PageHeader from '../components/PageHeader'
import RowActions from '../components/RowActions'
import StatusTag from '../components/StatusTag'

/** Case- and accent-insensitive "contains" for the client-side search boxes. */
const fold = (v: string) => v.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase()
const matches = (q: string, ...values: (string | null | undefined)[]) => !q || values.some((v) => v && fold(v).includes(fold(q)))

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : fallback)

type CmsForm = { kind: CmsKind; slug: string; title: string; content: string; topic?: string; sortOrder: number; isPublished: boolean }

const PagesTab = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<CmsPage | 'new' | null>(null)
  const [form] = Form.useForm<CmsForm>()
  const [q, setQ] = useState('')
  const [kind, setKind] = useState<CmsKind | undefined>()
  const list = useQuery({ queryKey: ['cms'], queryFn: platformApi.cms })
  const rows = (list.data ?? []).filter((p) => (!kind || p.kind === kind) && matches(q, p.title, p.slug, p.topic))
  const save = useMutation({
    mutationFn: (v: CmsForm) => platformApi.saveCms({ id: editing === 'new' ? null : editing!.id, ...v, topic: v.topic || null }),
    onSuccess: (r) => { message.success(r.message); setEditing(null); void queryClient.invalidateQueries({ queryKey: ['cms'] }) },
    onError: (e) => message.error(errorText(e, 'Không lưu được trang.')),
  })
  const open = (p: CmsPage | 'new') => {
    setEditing(p)
    form.setFieldsValue(p === 'new' ? { kind: 'Page', slug: '', title: '', content: '', sortOrder: 0, isPublished: true }
      : { kind: p.kind, slug: p.slug, title: p.title, content: p.content, topic: p.topic ?? undefined, sortOrder: p.sortOrder, isPublished: p.isPublished })
  }
  return (
    <>
      <DataTable<CmsPage> rowKey="id" loading={list.isPending} error={list.error} dataSource={rows} paging="client"
        search={{ value: q, onSearch: setQ, placeholder: 'Tiêu đề, đường dẫn, chủ đề', width: 260 }}
        filters={(
          <Select allowClear placeholder="Mọi loại" style={{ width: 150 }} value={kind} onChange={setKind} aria-label="Lọc theo loại"
            options={[{ value: 'Page', label: 'Trang tĩnh' }, { value: 'Help', label: 'Trợ giúp' }]} />
        )}
        onReset={() => { setQ(''); setKind(undefined) }}
        actions={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => open('new')}>Thêm trang / bài trợ giúp</Button>}
        emptyText="Không có trang phù hợp"
        columns={[
          { title: 'Tiêu đề', dataIndex: 'title', render: (v: string, p) => <><span className="cell-main">{v}</span>{p.topic && <span className="cell-sub">{p.topic}</span>}</> },
          { title: 'Loại', dataIndex: 'kind', render: (k: CmsKind) => <Tag bordered={false}>{k === 'Page' ? 'Trang tĩnh' : 'Trợ giúp'}</Tag> },
          { title: 'Đường dẫn', dataIndex: 'slug', render: (s: string, p) => <a href={p.kind === 'Page' ? `/trang/${s}` : `/tro-giup/${s}`} target="_blank" rel="noreferrer">{s}</a> },
          { title: 'Hiển thị', dataIndex: 'isPublished', render: (v: boolean) => <StatusTag map={ON_OFF} value={v ? 'on' : 'off'} label={v ? 'Đang hiện' : 'Đã ẩn'} /> },
          { title: 'Cập nhật', dataIndex: 'updatedAt', render: (v: string) => <span className="cell-nowrap">{formatDateTime(v)}</span> },
          {
            title: '', key: 'actions', align: 'right',
            render: (_, p) => (
              <RowActions name={p.title}
                primary={<Button size="small" icon={<EditOutlined aria-hidden />} onClick={() => open(p)}>Sửa</Button>}
                items={[{ key: 'open', icon: <ExportOutlined aria-hidden />, label: 'Mở trang', onClick: () => window.open(p.kind === 'Page' ? `/trang/${p.slug}` : `/tro-giup/${p.slug}`, '_blank', 'noopener') }]} />
            ),
          },
        ]} />
      <Modal open={!!editing} title="Trang nội dung" width={820} okText="Lưu" confirmLoading={save.isPending} onCancel={() => setEditing(null)}
        onOk={() => form.submit()} destroyOnClose>
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)}>
          <Space>
            <Form.Item name="kind" label="Loại"><Select style={{ width: 160 }} disabled={editing !== 'new'}
              options={[{ value: 'Page', label: 'Trang tĩnh' }, { value: 'Help', label: 'Trợ giúp' }]} /></Form.Item>
            <Form.Item name="slug" label="Đường dẫn" rules={[{ required: true, pattern: /^[a-z0-9]+(-[a-z0-9]+)*$/, message: 'Chữ thường không dấu, số, gạch ngang.' }]}>
              <Input style={{ width: 260 }} disabled={editing !== 'new'} />
            </Form.Item>
            <Form.Item name="topic" label="Chủ đề (trợ giúp)"><Input style={{ width: 180 }} /></Form.Item>
            <Form.Item name="sortOrder" label="Thứ tự"><InputNumber min={0} /></Form.Item>
            <Form.Item name="isPublished" label="Hiện" valuePropName="checked"><Switch /></Form.Item>
          </Space>
          <Form.Item name="title" label="Tiêu đề" rules={[{ required: true, message: 'Nhập tiêu đề.' }]}><Input /></Form.Item>
          <Form.Item name="content" label="Nội dung (HTML — được lọc an toàn khi lưu)" rules={[{ required: true, message: 'Nhập nội dung.' }]}>
            <Input.TextArea rows={14} style={{ fontFamily: 'monospace' }} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}

const TemplatesTab = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<MessageTemplate | null>(null)
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [q, setQ] = useState('')
  const [channel, setChannel] = useState<string | undefined>()
  const list = useQuery({ queryKey: ['templates'], queryFn: platformApi.templates })
  const rows = (list.data ?? []).filter((t) => (!channel || t.channel === channel) && matches(q, t.name, t.subject))
  const save = useMutation({
    mutationFn: () => platformApi.saveTemplate(editing!.id, editing!.channel === 'Sms' ? null : subject, body),
    onSuccess: (r) => { message.success(r.message); setEditing(null); void queryClient.invalidateQueries({ queryKey: ['templates'] }) },
    onError: (e) => message.error(errorText(e, 'Không lưu được mẫu.')),
  })
  return (
    <>
      <DataTable<MessageTemplate> rowKey="id" loading={list.isPending} error={list.error} dataSource={rows} paging="client"
        search={{ value: q, onSearch: setQ, placeholder: 'Tên mẫu, tiêu đề', width: 240 }}
        filters={(
          <Select allowClear placeholder="Mọi kênh" style={{ width: 150 }} value={channel} onChange={setChannel} aria-label="Lọc theo kênh"
            options={[{ value: 'Email', label: 'Email' }, { value: 'Sms', label: 'SMS' }, { value: 'InApp', label: 'Thông báo' }]} />
        )}
        onReset={() => { setQ(''); setChannel(undefined) }}
        emptyText="Không có mẫu phù hợp"
        columns={[
          { title: 'Mẫu', dataIndex: 'name', render: (v: string) => <span className="cell-main">{v}</span> },
          { title: 'Kênh', dataIndex: 'channel', render: (c: string) => <Tag bordered={false}>{c === 'Sms' ? 'SMS' : c === 'Email' ? 'Email' : 'Thông báo'}</Tag> },
          { title: 'Biến', dataIndex: 'placeholders', render: (v: string) => <Space size={[4, 4]} wrap style={{ maxWidth: 420 }}>{v.split(',').map((p) => <Tag key={p} bordered={false}>{`{{${p}}}`}</Tag>)}</Space> },
          { title: 'Cập nhật', dataIndex: 'updatedAt', render: (v: string) => <span className="cell-nowrap">{formatDateTime(v)}</span> },
          {
            title: '', key: 'actions', align: 'right',
            render: (_, t) => <RowActions name={t.name} primary={<Button size="small" icon={<EditOutlined aria-hidden />} onClick={() => { setEditing(t); setSubject(t.subject ?? ''); setBody(t.body) }}>Sửa</Button>} />,
          },
        ]} />
      <Modal open={!!editing} title={editing?.name} width={720} okText="Lưu" confirmLoading={save.isPending} onCancel={() => setEditing(null)} onOk={() => save.mutate()}>
        <Typography.Paragraph type="secondary">
          Biến dùng được: {editing?.placeholders.split(',').map((p) => `{{${p}}}`).join(', ')}. {editing?.channel === 'Sms' ? 'SMS tối đa 320 ký tự, nên viết không dấu.' : ''}
        </Typography.Paragraph>
        {editing && editing.channel !== 'Sms' && <Input value={subject} onChange={(e) => setSubject(e.target.value)} placeholder={editing.channel === 'Email' ? 'Tiêu đề thư' : 'Tiêu đề thông báo'} style={{ marginBottom: 8 }} data-testid="template-subject" />}
        <Input.TextArea rows={8} value={body} onChange={(e) => setBody(e.target.value)} style={{ fontFamily: 'monospace' }} />
      </Modal>
    </>
  )
}

type DivisionRow = { code: string; name: string; level: 'Province' | 'District' | 'Ward'; parentCode: string | null; childCount: number; addressCount: number; isActive: boolean }

/**
 * Danh mục hành chính (VI.8): two levels since 2025-07-01 (34 tỉnh/thành → phường/xã). Browse, add a ward under an active
 * province, rename — codes stay, nothing is deleted; units retired by the reform are listed as "Ngừng dùng".
 */
const DivisionsTab = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [path, setPath] = useState<DivisionRow[]>([])
  const parent = path[path.length - 1]
  const [editing, setEditing] = useState<DivisionRow | 'new' | null>(null)
  const [q, setQ] = useState('')
  const [form] = Form.useForm<{ code: string; name: string }>()
  const list = useQuery({ queryKey: ['divisions', parent?.code ?? ''], queryFn: () => platformApi.divisions(parent?.code) })
  const save = useMutation({
    mutationFn: (v: { code: string; name: string }) =>
      editing === 'new' ? platformApi.addDivision({ code: v.code.trim(), name: v.name.trim(), parentCode: parent?.code ?? null })
        : platformApi.renameDivision((editing as DivisionRow).code, v.name.trim()),
    onSuccess: (r) => { message.success(r.message); setEditing(null); void queryClient.invalidateQueries({ queryKey: ['divisions'] }) },
    onError: (e) => message.error(errorText(e, 'Không lưu được.')),
  })
  return (
    <>
      <DataTable<DivisionRow>
        rowKey="code"
        size="small"
        loading={list.isPending}
        error={list.error}
        dataSource={(list.data ?? []).filter((d) => matches(q, d.name, d.code))}
        paging="client"
        search={{ value: q, onSearch: setQ, placeholder: 'Tên hoặc mã', width: 220 }}
        filters={(
          <Breadcrumb items={[
            { title: path.length === 0 ? 'Toàn quốc' : <a onClick={() => { setPath([]); setQ('') }}>Toàn quốc</a> },
            ...path.map((d, i) => ({ title: i === path.length - 1 ? d.name : <a onClick={() => { setPath(path.slice(0, i + 1)); setQ('') }}>{d.name}</a> })),
          ]} />
        )}
        actions={(!parent || (parent.level === 'Province' && parent.isActive)) && (
          <Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => { setEditing('new'); form.setFieldsValue({ code: '', name: '' }) }} data-testid="division-add">
            Thêm {parent ? 'phường / xã' : 'tỉnh / thành phố'}
          </Button>
        )}
        emptyText="Không có đơn vị phù hợp"
        columns={[
          { title: 'Mã', dataIndex: 'code', width: 100 },
          { title: 'Tên', dataIndex: 'name', render: (n: string, d) => d.level === 'Ward' ? n : <a onClick={() => { setPath([...path, d]); setQ('') }}>{n}</a> },
          { title: 'Cấp', dataIndex: 'level', width: 120, render: (l: DivisionRow['level']) => ({ Province: 'Tỉnh / thành', District: 'Quận / huyện (cũ)', Ward: 'Phường / xã' })[l] },
          { title: 'Trạng thái', dataIndex: 'isActive', width: 120, render: (a: boolean) => <StatusTag map={ON_OFF} value={a ? 'on' : 'off'} label={a ? 'Đang dùng' : 'Ngừng dùng'} /> },
          { title: 'Cấp dưới', dataIndex: 'childCount', width: 100, align: 'right', render: (v: number) => formatNumber(v) },
          { title: 'Địa chỉ đã lưu', dataIndex: 'addressCount', width: 130, align: 'right', render: (v: number) => formatNumber(v) },
          {
            title: '', key: 'actions', width: 110, align: 'right',
            render: (_, d) => <RowActions name={d.name} primary={<Button size="small" onClick={() => { setEditing(d); form.setFieldsValue({ code: d.code, name: d.name }) }}>Đổi tên</Button>} />,
          },
        ]}
      />
      <Modal open={editing !== null} title={editing === 'new' ? 'Thêm đơn vị hành chính' : 'Đổi tên đơn vị'} okText="Lưu" cancelText="Huỷ"
        confirmLoading={save.isPending} onOk={() => form.submit()} onCancel={() => setEditing(null)} destroyOnClose>
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)}>
          <Form.Item name="code" label="Mã (Tổng cục Thống kê)" rules={[{ required: true, pattern: /^\d{2,10}$/, message: 'Mã gồm 2–10 chữ số.' }]}>
            <Input disabled={editing !== 'new'} />
          </Form.Item>
          <Form.Item name="name" label="Tên" rules={[{ required: true, whitespace: true, max: 100, message: 'Nhập tên (tối đa 100 ký tự).' }]}>
            <Input />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}

const TABS = [
  { key: 'pages', label: 'Trang tĩnh & trợ giúp', children: <PagesTab /> },
  { key: 'templates', label: 'Mẫu thư / SMS', children: <TemplatesTab /> },
  { key: 'divisions', label: 'Danh mục hành chính', children: <DivisionsTab /> },
]

/** VI.8 Nội dung: static pages, help center, message templates, administrative divisions. The tab lives in the URL (?tab=). */
const ContentPage = () => {
  const [params, setParams] = useSearchParams()
  const tab = TABS.find((t) => t.key === params.get('tab'))?.key ?? 'pages'
  return (
    <>
      <PageHeader title="Nội dung & mẫu tin" description="Trang pháp lý, bài trợ giúp, mẫu thư / SMS / thông báo và danh mục đơn vị hành chính" />
      <Card className="tabs-card">
        <Tabs activeKey={tab} onChange={(k) => setParams(k === 'pages' ? {} : { tab: k })} items={TABS} />
      </Card>
    </>
  )
}

export default ContentPage
