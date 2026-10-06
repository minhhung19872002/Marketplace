import { useState } from 'react'
import { App, Button, Card, Image, Input, List, Rate, Select, Space, Tag, Typography } from 'antd'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { aftercareApi, type ShopReview } from '../api/aftercare'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'

/** Chăm sóc khách hàng → Quản lý đánh giá: filter by stars / replied, reply once per review. */
const ReviewsPage = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [rating, setRating] = useState<number | undefined>()
  const [replied, setReplied] = useState<boolean | undefined>()
  const [page, setPage] = useState(1)
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const list = useQuery({
    queryKey: ['reviews', shopId, rating, replied, page],
    queryFn: () => aftercareApi.reviews(shopId, { rating, replied, page }),
    placeholderData: keepPreviousData,
  })
  const reply = useMutation({
    mutationFn: ({ id, text }: { id: string; text: string }) => aftercareApi.reply(shopId, id, text),
    onSuccess: (r) => {
      message.success(r.message)
      void queryClient.invalidateQueries({ queryKey: ['reviews', shopId] })
    },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không gửi được phản hồi.'),
  })

  return (
    <Card
      title="Đánh giá của khách hàng"
      extra={
        <Space>
          <Select allowClear placeholder="Số sao" style={{ width: 120 }} value={rating} onChange={(v) => { setRating(v); setPage(1) }}
            options={[5, 4, 3, 2, 1].map((n) => ({ value: n, label: `${n} sao` }))} />
          <Select allowClear placeholder="Trạng thái" style={{ width: 160 }} value={replied} onChange={(v) => { setReplied(v); setPage(1) }}
            options={[{ value: false, label: 'Chưa trả lời' }, { value: true, label: 'Đã trả lời' }]} data-testid="replied-filter" />
        </Space>
      }
    >
      <List<ShopReview>
        loading={list.isLoading}
        dataSource={list.data?.items ?? []}
        locale={{ emptyText: 'Chưa có đánh giá nào' }}
        pagination={list.data && list.data.totalCount > list.data.pageSize
          ? { current: page, pageSize: list.data.pageSize, total: list.data.totalCount, onChange: setPage } : false}
        renderItem={({ review: r, productName, orderCode }) => (
          <List.Item data-testid="shop-review">
            <Space direction="vertical" style={{ width: '100%' }}>
              <Space wrap>
                <Typography.Text strong>{r.reviewerName}</Typography.Text>
                <Rate disabled value={r.rating} />
                <Typography.Text type="secondary">{productName} · đơn {orderCode} · {formatDateTime(r.createdAt)}</Typography.Text>
              </Space>
              {r.tags.length > 0 && <Space wrap>{r.tags.map((t) => <Tag key={t}>{t}</Tag>)}</Space>}
              {r.content && <Typography.Paragraph style={{ marginBottom: 0 }}>{r.content}</Typography.Paragraph>}
              {r.media.length > 0 && (
                <Image.PreviewGroup>
                  <Space wrap>
                    {r.media.map((m, i) => m.type === 'Image'
                      ? <Image key={i} src={m.url} width={72} height={72} style={{ objectFit: 'cover' }} />
                      : <video key={i} src={m.url} width={120} controls preload="metadata" />)}
                  </Space>
                </Image.PreviewGroup>
              )}
              {r.sellerReply ? (
                <Typography.Text type="secondary">Shop đã trả lời: {r.sellerReply}</Typography.Text>
              ) : (
                <Space.Compact style={{ width: '100%' }}>
                  <Input placeholder="Trả lời khách hàng (chỉ 1 lần)" maxLength={500} value={drafts[r.id] ?? ''}
                    onChange={(e) => setDrafts({ ...drafts, [r.id]: e.target.value })} data-testid="reply-input" />
                  <Button type="primary" loading={reply.isPending} disabled={!(drafts[r.id] ?? '').trim()}
                    onClick={() => reply.mutate({ id: r.id, text: drafts[r.id].trim() })} data-testid="reply-submit">Trả lời</Button>
                </Space.Compact>
              )}
            </Space>
          </List.Item>
        )}
      />
    </Card>
  )
}

export default ReviewsPage
