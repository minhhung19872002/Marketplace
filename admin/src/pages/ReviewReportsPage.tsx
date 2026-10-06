import { useState } from 'react'
import { App, Button, Card, Image, Rate, Space, Table, Tabs, Tag, Typography } from 'antd'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { aftercareApi, type ReviewReport } from '../api/aftercare'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'

/** Báo cáo đánh giá vi phạm: hide the review (rating recomputed) or dismiss the report. */
const ReviewReportsPage = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<ReviewReport['status']>('Pending')
  const [page, setPage] = useState(1)
  const list = useQuery({
    queryKey: ['review-reports', status, page],
    queryFn: () => aftercareApi.reports(status, page),
    placeholderData: keepPreviousData,
  })
  const resolve = useMutation({
    mutationFn: ({ id, hide }: { id: string; hide: boolean }) => aftercareApi.resolve(id, hide, null),
    onSuccess: (r) => {
      message.success(r.message)
      void queryClient.invalidateQueries({ queryKey: ['review-reports'] })
    },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không xử lý được.'),
  })
  return (
    <Card title="Báo cáo đánh giá">
      <Tabs activeKey={status} onChange={(k) => { setStatus(k as ReviewReport['status']); setPage(1) }}
        items={[{ key: 'Pending', label: 'Chờ xử lý' }, { key: 'Upheld', label: 'Đã ẩn' }, { key: 'Dismissed', label: 'Đã bỏ qua' }]} />
      <Table<ReviewReport>
        rowKey="id"
        loading={list.isLoading}
        dataSource={list.data?.items ?? []}
        pagination={list.data && list.data.totalCount > list.data.pageSize
          ? { current: page, pageSize: list.data.pageSize, total: list.data.totalCount, onChange: setPage } : false}
        columns={[
          { title: 'Sản phẩm', dataIndex: 'productName' },
          {
            title: 'Đánh giá',
            render: (_, r) => (
              <Space direction="vertical" size={2}>
                <Space><Typography.Text strong>{r.review.reviewerName}</Typography.Text><Rate disabled value={r.review.rating} /></Space>
                <Typography.Text>{r.review.content}</Typography.Text>
                {r.review.media.length > 0 && (
                  <Image.PreviewGroup>
                    <Space wrap>
                      {r.review.media.filter((m) => m.type === 'Image').map((m, i) => <Image key={i} src={m.url} width={56} height={56} style={{ objectFit: 'cover' }} />)}
                    </Space>
                  </Image.PreviewGroup>
                )}
              </Space>
            ),
          },
          { title: 'Lý do báo cáo', dataIndex: 'reason' },
          { title: 'Ngày', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
          {
            title: '',
            render: (_, r) => r.status === 'Pending' ? (
              <Space>
                <Button danger size="small" loading={resolve.isPending} onClick={() => resolve.mutate({ id: r.id, hide: true })}>Ẩn đánh giá</Button>
                <Button size="small" loading={resolve.isPending} onClick={() => resolve.mutate({ id: r.id, hide: false })}>Bỏ qua</Button>
              </Space>
            ) : <Tag>{r.reviewHidden ? 'Đã ẩn' : 'Giữ nguyên'}</Tag>,
          },
        ]}
      />
    </Card>
  )
}

export default ReviewReportsPage
