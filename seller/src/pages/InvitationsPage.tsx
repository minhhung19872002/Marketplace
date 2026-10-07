import { App as AntApp, Button, Card, Empty, List, Space, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { PERMISSION_LABELS, ROLE_LABELS, staffApi } from '../api/staff'
import { ApiError } from '../api/http'
import { formatDateTime } from '../lib/datetime'
import { useShopStore } from '../stores/shop'

/** "Lời mời làm nhân viên" (III.9, D6): the signed-in user joins a shop only by accepting here. */
const InvitationsPage = () => {
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const select = useShopStore((s) => s.select)
  const invitations = useQuery({ queryKey: ['my-invitations'], queryFn: staffApi.myInvitations })
  const fail = (e: unknown) => void message.error(e instanceof ApiError ? e.message : 'Thao tác thất bại.')

  const accept = useMutation({
    mutationFn: (id: string) => staffApi.accept(id),
    onSuccess: async (r, id) => {
      void message.success(r.message)
      const shopId = invitations.data?.find((i) => i.id === id)?.shopId
      await queryClient.invalidateQueries({ queryKey: ['my-shops'] })
      await queryClient.invalidateQueries({ queryKey: ['my-invitations'] })
      if (shopId) select(shopId)
      navigate('/')
    },
    onError: fail,
  })
  const decline = useMutation({
    mutationFn: (id: string) => staffApi.decline(id),
    onSuccess: (r) => { void message.success(r.message); void queryClient.invalidateQueries({ queryKey: ['my-invitations'] }) },
    onError: fail,
  })

  return (
    <Card title="Lời mời làm nhân viên shop" style={{ maxWidth: 760 }}>
      <List
        loading={invitations.isLoading}
        dataSource={invitations.data ?? []}
        locale={{ emptyText: <Empty description="Không có lời mời nào đang chờ" /> }}
        renderItem={(i) => (
          <List.Item
            data-testid="invitation"
            actions={[
              <Button key="accept" type="primary" loading={accept.isPending && accept.variables === i.id} onClick={() => accept.mutate(i.id)}
                data-testid="invitation-accept">Đồng ý</Button>,
              <Button key="decline" loading={decline.isPending && decline.variables === i.id} onClick={() => decline.mutate(i.id)}>Từ chối</Button>,
            ]}
          >
            <List.Item.Meta
              title={<Space><Typography.Text strong>{i.shopName}</Typography.Text><Tag>{ROLE_LABELS[i.role]}</Tag></Space>}
              description={
                <Space direction="vertical" size={4}>
                  <span>Quyền: {i.permissions.map((p) => PERMISSION_LABELS[p] ?? p).join(', ') || '—'}</span>
                  <Typography.Text type="secondary">Hết hạn lúc {formatDateTime(i.expiresAt)}</Typography.Text>
                </Space>
              }
            />
          </List.Item>
        )}
      />
    </Card>
  )
}

export default InvitationsPage
