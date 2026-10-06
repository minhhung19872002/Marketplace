import { App, Card, InputNumber, Switch, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { platformApi, type CarrierRow, type GatewayRow } from '../api/platform'

const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.message : fallback)

/** VI.8 Đơn vị vận chuyển & cổng thanh toán: switch configured providers on / off (keys themselves live in .env). */
const ProvidersPage = () => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const data = useQuery({ queryKey: ['providers'], queryFn: platformApi.providers })
  const done = (r: { message: string }) => { message.success(r.message); void queryClient.invalidateQueries({ queryKey: ['providers'] }) }
  const carrier = useMutation({ mutationFn: platformApi.updateCarrier, onSuccess: done, onError: (e) => message.error(errorText(e, 'Không lưu được.')) })
  const gateway = useMutation({
    mutationFn: (g: { method: string; enabled: boolean }) => platformApi.setGateway(g.method, g.enabled),
    onSuccess: done, onError: (e) => message.error(errorText(e, 'Không lưu được.')),
  })

  return (
    <>
      <Card title="Cổng thanh toán" style={{ marginBottom: 16 }}>
        <Typography.Paragraph type="secondary">
          Cổng chỉ có ở đây khi đã cấu hình khoá trong .env. Tắt cổng: không nhận thanh toán mới, giao dịch cũ vẫn tra cứu và hoàn tiền được.
        </Typography.Paragraph>
        <Table<GatewayRow> rowKey="method" loading={data.isLoading} dataSource={data.data?.gateways ?? []} pagination={false}
          columns={[
            { title: 'Cổng', dataIndex: 'name' },
            { title: 'Mã', dataIndex: 'provider' },
            { title: 'Đang bật', key: 'on', render: (_, g) => <Switch checked={g.enabled} onChange={(v) => gateway.mutate({ method: g.method, enabled: v })}
              data-testid={`gateway-${g.method}`} /> },
          ]} />
      </Card>
      <Card title="Đơn vị vận chuyển">
        <Table<CarrierRow> rowKey="id" loading={data.isLoading} dataSource={data.data?.carriers ?? []} pagination={false}
          columns={[
            { title: 'Kênh', render: (_, c) => <span>{c.name} <Typography.Text type="secondary">({c.code})</Typography.Text></span> },
            { title: 'Hãng', render: (_, c) => <Tag color={c.providerConfigured ? 'green' : 'default'}>{c.provider}{c.providerConfigured ? '' : ' — chưa cấu hình khoá'}</Tag> },
            { title: 'Đang bật', render: (_, c) => <Switch checked={c.isActive} onChange={(v) => carrier.mutate({ ...c, isActive: v })} /> },
            { title: 'Thu hộ COD', render: (_, c) => <Switch checked={c.supportsCod} onChange={(v) => carrier.mutate({ ...c, supportsCod: v })} /> },
            {
              title: 'Ngày giao (nội tỉnh / nội miền / liên miền)',
              render: (_, c) => (
                <span>
                  {(['daysSameProvince', 'daysSameRegion', 'daysCrossRegion'] as const).map((k) => (
                    <InputNumber key={k} min={0} max={30} size="small" style={{ width: 56, marginRight: 4 }} defaultValue={c[k]}
                      onBlur={(e) => { const v = Number(e.target.value); if (v !== c[k]) carrier.mutate({ ...c, [k]: v }) }} />
                  ))}
                </span>
              ),
            },
          ]} />
      </Card>
    </>
  )
}

export default ProvidersPage
