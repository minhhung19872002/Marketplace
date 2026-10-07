import { useEffect, useState } from 'react'
import { App, Button, Card, Select, Space, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { marketingApi } from '../api/marketing'
import { ApiError } from '../api/http'

const MAX = 10

/**
 * Marketing → Từ khoá hot (VI.6, F8): the curated keywords shown under the search box while real search traffic is thin.
 * Its own permission (PROMO.HOT_KEYWORD.MANAGE) — no right over the other system parameters needed.
 */
const HotKeywordsPage = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const current = useQuery({ queryKey: ['hot-keywords'], queryFn: marketingApi.hotKeywords })
  const [keywords, setKeywords] = useState<string[]>([])
  useEffect(() => { if (current.data) setKeywords(current.data) }, [current.data])

  const save = useMutation({
    mutationFn: () => marketingApi.setHotKeywords(keywords),
    onSuccess: (r) => {
      message.success(r.message)
      setKeywords(r.data)
      void queryClient.invalidateQueries({ queryKey: ['hot-keywords'] })
    },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không lưu được từ khoá.'),
  })

  return (
    <Card title="Từ khoá hot">
      <Typography.Paragraph type="secondary">
        Hiện dưới ô tìm kiếm của site người mua khi chưa đủ lượt tìm thật trong 7 ngày (từ khoá người mua tìm nhiều luôn đứng trước).
        Tối đa {MAX} từ khoá, mỗi từ khoá đến 50 ký tự.
      </Typography.Paragraph>
      <Space direction="vertical" style={{ width: '100%' }}>
        <Select
          mode="tags"
          value={keywords}
          onChange={(v: string[]) => setKeywords(v.slice(0, MAX))}
          tokenSeparators={[',']}
          placeholder="Gõ từ khoá rồi Enter"
          style={{ width: '100%', maxWidth: 720 }}
          loading={current.isLoading}
          aria-label="Từ khoá hot"
          data-testid="hot-keywords"
        />
        <Button type="primary" onClick={() => save.mutate()} loading={save.isPending} disabled={current.isLoading} data-testid="hot-keywords-save">
          Lưu
        </Button>
      </Space>
    </Card>
  )
}

export default HotKeywordsPage
