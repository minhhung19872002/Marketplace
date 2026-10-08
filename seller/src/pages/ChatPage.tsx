import { useCallback, useEffect, useRef, useState } from 'react'
import { App, Badge, Button, Card, Col, Empty, Form, Input, List, Popconfirm, Row, Segmented, Select, Space, Switch, Table, Tabs, Tag, Typography, Upload } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chatApi, type ChatMessage, type ChatSettings, type Conversation, type InboxFilter, type QuickReply } from '../api/chat'
import { uploadMedia } from '../api/seller'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { formatPrice } from '../lib/money'
import { realtime, useRealtimeEvent } from '../lib/realtime'
import { ShoppingOutlined, InboxOutlined, TagOutlined, WarningOutlined, PictureOutlined } from '@ant-design/icons'

const FILTERS: { value: InboxFilter; label: string }[] = [
  { value: 'All', label: 'Tất cả' },
  { value: 'Unread', label: 'Chưa đọc' },
  { value: 'Mine', label: 'Của tôi' },
  { value: 'Unassigned', label: 'Chưa phân công' },
]

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.message : fallback)

const Body = ({ m }: { m: ChatMessage }) => {
  const p = (m.payload ?? {}) as Record<string, string | number>
  switch (m.type) {
    case 'Image':
      return <a href={String(p.url ?? '')} target="_blank" rel="noreferrer"><img src={String(p.url ?? '')} alt="Ảnh" style={{ maxWidth: 200, maxHeight: 200 }} /></a>
    case 'Product':
      return <span><ShoppingOutlined /> <b>{String(p.name ?? m.body)}</b> · {formatPrice(Number(p.price ?? 0))}</span>
    case 'Order':
      return <span><InboxOutlined /> Đơn <b>{String(p.code)}</b> · {String(p.status)} · {formatPrice(Number(p.total ?? 0))}</span>
    case 'Voucher':
      return <span><TagOutlined /> Voucher <b>{String(p.code)}</b></span>
    default:
      return <span style={{ whiteSpace: 'pre-wrap' }}>{m.body}</span>
  }
}

/** One conversation seen from the shop: history, live messages, typing, quick replies via "/". */
const Thread = ({ shopId, conversation, quickReplies, staff }: {
  shopId: string
  conversation: Conversation
  quickReplies: QuickReply[]
  staff: { userId: string; name: string }[]
}) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [text, setText] = useState('')
  const [typing, setTyping] = useState(false)
  const bottom = useRef<HTMLDivElement>(null)
  const lastTyping = useRef(0)
  const id = conversation.id

  const markRead = useCallback(() => {
    void chatApi.read(shopId, id).then(() => queryClient.invalidateQueries({ queryKey: ['chat-inbox', shopId] }))
  }, [shopId, id, queryClient])

  useEffect(() => {
    let alive = true
    void chatApi.messages(shopId, id).then((list) => {
      if (!alive) return
      setMessages([...list].reverse())
      markRead()
    })
    return () => {
      alive = false
    }
  }, [shopId, id, markRead])

  useEffect(() => {
    bottom.current?.scrollIntoView({ block: 'end' })
  }, [messages.length])

  useRealtimeEvent<ChatMessage>('chat.message', (m) => {
    if (m.conversationId !== id) return
    setMessages((list) => (list.some((x) => x.id === m.id) ? list : [...list, m]))
    setTyping(false)
    if (m.senderRole === 'Buyer') markRead()
  })
  useRealtimeEvent<{ conversationId: string; side: string; at: string }>('chat.read', (e) => {
    if (e.conversationId !== id || e.side !== 'Buyer') return
    setMessages((list) => list.map((m) => (m.senderRole !== 'Buyer' && !m.readAt ? { ...m, readAt: e.at } : m)))
  })
  useRealtimeEvent<{ conversationId: string; side: string }>('chat.typing', (e) => {
    if (e.conversationId !== id || e.side !== 'Buyer') return
    setTyping(true)
    window.setTimeout(() => setTyping(false), 3_000)
  })

  const send = useMutation({
    mutationFn: (input: Parameters<typeof chatApi.send>[2]) => chatApi.send(shopId, id, input),
    onSuccess: (m) => {
      setMessages((list) => (list.some((x) => x.id === m.id) ? list : [...list, m]))
      setText('')
      void queryClient.invalidateQueries({ queryKey: ['chat-inbox', shopId] })
    },
    onError: (e) => message.error(errorText(e, 'Không gửi được tin nhắn.')),
  })
  const assign = useMutation({
    mutationFn: (staffUserId: string | null) => chatApi.assign(shopId, id, staffUserId),
    onSuccess: (r) => {
      message.success(r.message)
      void queryClient.invalidateQueries({ queryKey: ['chat-inbox', shopId] })
    },
    onError: (e) => message.error(errorText(e, 'Không phân công được.')),
  })

  const onType = (value: string) => {
    setText(value)
    if (Date.now() - lastTyping.current > 2_000) {
      lastTyping.current = Date.now()
      void realtime().then((c) => c?.invoke('Typing', id).catch(() => undefined))
    }
  }
  // "/" opens the quick-reply list, filtered by what follows it
  const suggestions = text.startsWith('/')
    ? quickReplies.filter((r) => r.shortcut.toLowerCase().startsWith(text.slice(1).toLowerCase()))
    : []
  const lastMine = [...messages].reverse().find((m) => m.senderRole !== 'Buyer')

  return (
    <Card
      size="small"
      title={<Space>{conversation.buyerName}{conversation.blockedByBuyer && <Tag color="red">Người mua đã chặn</Tag>}</Space>}
      extra={
        <Select allowClear placeholder="Phân công cho" style={{ width: 200 }} value={conversation.assignedTo ?? undefined}
          onChange={(v) => assign.mutate(v ?? null)} options={staff.map((s) => ({ value: s.userId, label: s.name }))} data-testid="chat-assign" />
      }
    >
      <div className="chat-messages" data-testid="chat-messages">
        {messages.map((m) => (
          <div key={m.id} data-testid="chat-message"
            style={{ alignSelf: m.senderRole === 'Buyer' ? 'flex-start' : 'flex-end', maxWidth: '75%', display: 'flex', flexDirection: 'column', alignItems: m.senderRole === 'Buyer' ? 'flex-start' : 'flex-end' }}>
            {m.flagged && <Typography.Text type="warning" style={{ fontSize: 11 }}><WarningOutlined /> Có thông tin liên hệ ngoài sàn</Typography.Text>}
            <div className={`chat-bubble chat-bubble-${m.senderRole.toLowerCase()}`}>
              <Body m={m} />
            </div>
            <Typography.Text type="secondary" style={{ fontSize: 11 }}>
              {m.senderRole === 'Shop' && m.senderName ? `${m.senderName} · ` : ''}{formatDateTime(m.createdAt)}
            </Typography.Text>
          </div>
        ))}
        {lastMine?.readAt && <Typography.Text type="secondary" style={{ alignSelf: 'flex-end', fontSize: 11 }} data-testid="chat-seen">Đã xem</Typography.Text>}
        {typing && <Typography.Text type="secondary" style={{ fontSize: 11 }} data-testid="chat-typing">Khách đang soạn tin…</Typography.Text>}
        <div ref={bottom} />
      </div>
      {suggestions.length > 0 && (
        <List size="small" bordered dataSource={suggestions} style={{ marginTop: 6 }}
          renderItem={(r) => (
            <List.Item style={{ cursor: 'pointer' }} onClick={() => setText(r.content)} data-testid="quick-reply-suggestion">
              <Typography.Text code>/{r.shortcut}</Typography.Text> {r.content}
            </List.Item>
          )} />
      )}
      <Space.Compact style={{ width: '100%', marginTop: 8 }}>
        <Upload accept="image/*" showUploadList={false} beforeUpload={async (file) => {
          try {
            const asset = await uploadMedia('chat', file)
            send.mutate({ type: 'Image', imageAssetId: asset.id })
          } catch (e) {
            message.error(errorText(e, 'Không gửi được ảnh.'))
          }
          return false
        }}>
          <Button icon={<PictureOutlined />} aria-label="Gửi ảnh" />
        </Upload>
        <Input value={text} onChange={(e) => onType(e.target.value)} maxLength={2000} placeholder="Nhập tin nhắn — gõ / để chọn câu trả lời nhanh"
          disabled={conversation.blockedByBuyer} data-testid="chat-input"
          onPressEnter={() => text.trim() && !text.startsWith('/') && send.mutate({ type: 'Text', text: text.trim() })} />
        <Button type="primary" loading={send.isPending} disabled={!text.trim() || conversation.blockedByBuyer}
          onClick={() => send.mutate({ type: 'Text', text: text.trim() })} data-testid="chat-send">Gửi</Button>
      </Space.Compact>
    </Card>
  )
}

const Inbox = ({ shopId }: { shopId: string }) => {
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<InboxFilter>('All')
  const [q, setQ] = useState('')
  const [currentId, setCurrentId] = useState<string | null>(null)
  const inbox = useQuery({ queryKey: ['chat-inbox', shopId, filter, q], queryFn: () => chatApi.inbox(shopId, filter, q) })
  const quickReplies = useQuery({ queryKey: ['quick-replies', shopId], queryFn: () => chatApi.quickReplies(shopId) })
  const staff = useQuery({ queryKey: ['chat-staff', shopId], queryFn: () => chatApi.staff(shopId) })
  useRealtimeEvent('chat.message', () => void queryClient.invalidateQueries({ queryKey: ['chat-inbox', shopId] }))
  const items = inbox.data?.items ?? []
  const current = items.find((c) => c.id === currentId) ?? null

  return (
    <Row gutter={12}>
      <Col xs={24} md={8}>
        <Space direction="vertical" style={{ width: '100%' }}>
          <Segmented<InboxFilter> block value={filter} onChange={setFilter} options={FILTERS} />
          <Input.Search allowClear placeholder="Tìm theo tên khách" onSearch={setQ} />
          <List<Conversation>
            loading={inbox.isLoading}
            dataSource={items}
            locale={{ emptyText: 'Chưa có cuộc trò chuyện nào' }}
            renderItem={(c) => (
              <List.Item onClick={() => setCurrentId(c.id)} data-testid="inbox-item"
                className={c.id === currentId ? 'inbox-item active' : 'inbox-item'}>
                <List.Item.Meta
                  title={<Space>{c.buyerName}<Badge count={c.unread} /></Space>}
                  description={
                    <Space direction="vertical" size={0}>
                      <Typography.Text type="secondary" ellipsis style={{ maxWidth: 240 }}>{c.lastMessagePreview ?? ''}</Typography.Text>
                      <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                        {formatDateTime(c.lastMessageAt)}{c.assignedName ? ` · ${c.assignedName}` : ''}
                      </Typography.Text>
                    </Space>
                  }
                />
              </List.Item>
            )}
          />
        </Space>
      </Col>
      <Col xs={24} md={16}>
        {current ? (
          <Thread key={current.id} shopId={shopId} conversation={current} quickReplies={quickReplies.data ?? []} staff={staff.data ?? []} />
        ) : (
          <Empty description="Chọn một cuộc trò chuyện" />
        )}
      </Col>
    </Row>
  )
}

const QuickReplies = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<{ shortcut: string; content: string }>()
  const list = useQuery({ queryKey: ['quick-replies', shopId], queryFn: () => chatApi.quickReplies(shopId) })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['quick-replies', shopId] })
  const save = useMutation({
    mutationFn: (v: { shortcut: string; content: string }) => chatApi.saveQuickReply(shopId, v),
    onSuccess: (r) => {
      message.success(r.message)
      form.resetFields()
      void refresh()
    },
    onError: (e) => message.error(errorText(e, 'Không lưu được.')),
  })
  const remove = useMutation({
    mutationFn: (id: string) => chatApi.deleteQuickReply(shopId, id),
    onSuccess: () => void refresh(),
    onError: (e) => message.error(errorText(e, 'Không xóa được.')),
  })
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Form form={form} layout="inline" onFinish={(v) => save.mutate(v)}>
        <Form.Item name="shortcut" rules={[{ required: true, message: 'Nhập phím tắt' }]}>
          <Input addonBefore="/" placeholder="camon" style={{ width: 160 }} />
        </Form.Item>
        <Form.Item name="content" rules={[{ required: true, message: 'Nhập nội dung' }]}>
          <Input placeholder="Cảm ơn bạn đã ủng hộ shop!" style={{ width: 360 }} />
        </Form.Item>
        <Button type="primary" htmlType="submit" loading={save.isPending}>Thêm</Button>
      </Form>
      <Table<QuickReply> rowKey="id" size="small" loading={list.isLoading} dataSource={list.data ?? []} pagination={false}
        columns={[
          { title: 'Phím tắt', dataIndex: 'shortcut', render: (v: string) => <Typography.Text code>/{v}</Typography.Text> },
          { title: 'Nội dung', dataIndex: 'content' },
          {
            title: '', key: 'x', width: 80,
            render: (_, r) => <Popconfirm title="Xóa câu trả lời nhanh này?" onConfirm={() => remove.mutate(r.id)}><Button size="small" danger>Xóa</Button></Popconfirm>,
          },
        ]} />
    </Space>
  )
}

// TimeOnly travels as "HH:mm:ss"; the form edits "HH:mm"
const hhmm = (t: string) => t.slice(0, 5)
const hhmmss = (t: string) => (t.length === 5 ? `${t}:00` : t)

const Settings = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const [form] = Form.useForm<ChatSettings>()
  const settings = useQuery({ queryKey: ['chat-settings', shopId], queryFn: () => chatApi.settings(shopId) })
  useEffect(() => {
    if (settings.data) form.setFieldsValue({ ...settings.data, openFrom: hhmm(settings.data.openFrom), openTo: hhmm(settings.data.openTo) })
  }, [settings.data, form])
  const save = useMutation({
    mutationFn: (v: ChatSettings) => chatApi.saveSettings(shopId, { ...v, openFrom: hhmmss(v.openFrom), openTo: hhmmss(v.openTo) }),
    onSuccess: (r) => message.success(r.message),
    onError: (e) => message.error(errorText(e, 'Không lưu được cài đặt.')),
  })
  return (
    <Form form={form} layout="vertical" style={{ maxWidth: 520 }} onFinish={(v) => save.mutate(v)}>
      <Form.Item name="autoReplyEnabled" label="Tự động trả lời" valuePropName="checked">
        <Switch />
      </Form.Item>
      <Form.Item name="autoReplyText" label="Nội dung tự động trả lời (ngoài giờ làm việc hoặc tin đầu tiên)" rules={[{ max: 500 }]}>
        <Input.TextArea rows={3} />
      </Form.Item>
      <Space>
        <Form.Item name="openFrom" label="Giờ làm việc từ" rules={[{ required: true }]}>
          <Input type="time" />
        </Form.Item>
        <Form.Item name="openTo" label="đến" rules={[{ required: true }]}>
          <Input type="time" />
        </Form.Item>
      </Space>
      <Button type="primary" htmlType="submit" loading={save.isPending}>Lưu cài đặt</Button>
    </Form>
  )
}

/** Chăm sóc khách hàng → Chat: inbox with filters and assignment, quick replies, auto-reply (spec II.11). */
const ChatPage = ({ shopId }: { shopId: string }) => (
  <Card title="Chat với khách hàng">
    <Tabs items={[
      { key: 'inbox', label: 'Hộp thư', children: <Inbox shopId={shopId} /> },
      { key: 'quick', label: 'Câu trả lời nhanh', children: <QuickReplies shopId={shopId} /> },
      { key: 'settings', label: 'Cài đặt', children: <Settings shopId={shopId} /> },
    ]} />
  </Card>
)

export default ChatPage
