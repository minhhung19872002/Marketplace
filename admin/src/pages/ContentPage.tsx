import { useState } from 'react'
import { App, Button, Card, Form, Input, InputNumber, Modal, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { platformApi, type CmsKind, type CmsPage, type MessageTemplate } from '../api/platform'
import { formatDateTime } from '../lib/datetime'

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : fallback)

type CmsForm = { kind: CmsKind; slug: string; title: string; content: string; topic?: string; sortOrder: number; isPublished: boolean }

const PagesTab = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<CmsPage | 'new' | null>(null)
  const [form] = Form.useForm<CmsForm>()
  const list = useQuery({ queryKey: ['cms'], queryFn: platformApi.cms })
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
      <Button type="primary" onClick={() => open('new')} style={{ marginBottom: 12 }}>Thêm trang / bài trợ giúp</Button>
      <Table<CmsPage> rowKey="id" loading={list.isLoading} dataSource={list.data ?? []} pagination={false}
        columns={[
          { title: 'Loại', dataIndex: 'kind', render: (k: CmsKind) => <Tag>{k === 'Page' ? 'Trang tĩnh' : 'Trợ giúp'}</Tag> },
          { title: 'Tiêu đề', dataIndex: 'title' },
          { title: 'Chủ đề', dataIndex: 'topic' },
          { title: 'Đường dẫn', dataIndex: 'slug', render: (s: string, p) => <a href={p.kind === 'Page' ? `/trang/${s}` : `/tro-giup/${s}`} target="_blank" rel="noreferrer">{s}</a> },
          { title: 'Hiện', dataIndex: 'isPublished', render: (v: boolean) => (v ? 'Có' : 'Ẩn') },
          { title: 'Cập nhật', dataIndex: 'updatedAt', render: (v: string) => formatDateTime(v) },
          { title: '', key: 'x', render: (_, p) => <Button size="small" onClick={() => open(p)}>Sửa</Button> },
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
  const list = useQuery({ queryKey: ['templates'], queryFn: platformApi.templates })
  const save = useMutation({
    mutationFn: () => platformApi.saveTemplate(editing!.id, editing!.channel === 'Sms' ? null : subject, body),
    onSuccess: (r) => { message.success(r.message); setEditing(null); void queryClient.invalidateQueries({ queryKey: ['templates'] }) },
    onError: (e) => message.error(errorText(e, 'Không lưu được mẫu.')),
  })
  return (
    <>
      <Table<MessageTemplate> rowKey="id" loading={list.isLoading} dataSource={list.data ?? []} pagination={false}
        columns={[
          { title: 'Mẫu', dataIndex: 'name' },
          { title: 'Kênh', dataIndex: 'channel', render: (c: string) => <Tag>{c === 'Sms' ? 'SMS' : c === 'Email' ? 'Email' : 'Thông báo'}</Tag> },
          { title: 'Biến', dataIndex: 'placeholders', render: (v: string) => v.split(',').map((p) => <Tag key={p}>{`{{${p}}}`}</Tag>) },
          { title: 'Cập nhật', dataIndex: 'updatedAt', render: (v: string) => formatDateTime(v) },
          { title: '', key: 'x', render: (_, t) => <Button size="small" onClick={() => { setEditing(t); setSubject(t.subject ?? ''); setBody(t.body) }}>Sửa</Button> },
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
      <Space style={{ marginBottom: 12 }} wrap>
        <Button size="small" onClick={() => setPath([])} disabled={path.length === 0}>Toàn quốc</Button>
        {path.map((d, i) => <Button key={d.code} size="small" onClick={() => setPath(path.slice(0, i + 1))}>{d.name}</Button>)}
        {(!parent || (parent.level === 'Province' && parent.isActive)) && (
          <Button type="primary" size="small" onClick={() => { setEditing('new'); form.setFieldsValue({ code: '', name: '' }) }} data-testid="division-add">
            Thêm {parent ? 'phường / xã' : 'tỉnh / thành phố'}
          </Button>
        )}
      </Space>
      <Table<DivisionRow>
        rowKey="code"
        size="small"
        loading={list.isPending}
        dataSource={list.data ?? []}
        pagination={{ pageSize: 50 }}
        columns={[
          { title: 'Mã', dataIndex: 'code', width: 100 },
          { title: 'Tên', dataIndex: 'name', render: (n: string, d) => d.level === 'Ward' ? n : <a onClick={() => setPath([...path, d])}>{n}</a> },
          { title: 'Cấp', dataIndex: 'level', width: 120, render: (l: DivisionRow['level']) => ({ Province: 'Tỉnh / thành', District: 'Quận / huyện (cũ)', Ward: 'Phường / xã' })[l] },
          { title: 'Trạng thái', dataIndex: 'isActive', width: 120, render: (a: boolean) => a ? <Tag color="green">Đang dùng</Tag> : <Tag>Ngừng dùng</Tag> },
          { title: 'Cấp dưới', dataIndex: 'childCount', width: 100 },
          { title: 'Địa chỉ đã lưu', dataIndex: 'addressCount', width: 130 },
          { title: '', key: 'x', width: 90, render: (_, d) => <Button size="small" onClick={() => { setEditing(d); form.setFieldsValue({ code: d.code, name: d.name }) }}>Đổi tên</Button> },
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

/** VI.8 Nội dung: static pages, help center, message templates. */
const ContentPage = () => (
  <Card title="Nội dung & mẫu tin">
    <Tabs items={[
      { key: 'pages', label: 'Trang tĩnh & trợ giúp', children: <PagesTab /> },
      { key: 'templates', label: 'Mẫu thư / SMS', children: <TemplatesTab /> },
      { key: 'divisions', label: 'Danh mục hành chính', children: <DivisionsTab /> },
    ]} />
  </Card>
)

export default ContentPage
