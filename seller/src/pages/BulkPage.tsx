import { useState } from 'react'
import { App, Button, Card, Cascader, Progress, Space, Table, Tag, Typography, Upload } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { bulkApi, type BulkError, type BulkKind, type BulkStatus, type BulkTask } from '../api/bulk'
import { sellerApi, type CategoryNode } from '../api/seller'
import { formatDateTime } from '../lib/datetime'

interface Option { value: string; label: string; children?: Option[] }

const toOptions = (nodes: CategoryNode[]): Option[] =>
  nodes.map((n) => ({ value: n.id, label: n.name, children: n.children.length > 0 ? toOptions(n.children) : undefined }))

const STATUS: Record<BulkStatus, { label: string; color?: string }> = {
  Queued: { label: 'Đang chờ', color: 'default' },
  Running: { label: 'Đang xử lý', color: 'processing' },
  Done: { label: 'Xong', color: 'success' },
  Failed: { label: 'Dừng do lỗi', color: 'error' },
}

const saveBlob = (blob: Blob, name: string) => {
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = name
  a.click()
  URL.revokeObjectURL(a.href)
}

/** Sản phẩm → Excel hàng loạt (III.3): template per category, price / stock sheet, background tasks with an error table. */
const BulkPage = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [category, setCategory] = useState<string[]>([])
  const tree = useQuery({ queryKey: ['categories'], queryFn: sellerApi.categories, staleTime: 600_000 })
  const tasks = useQuery({
    queryKey: ['bulk-tasks', shopId],
    queryFn: () => bulkApi.tasks(shopId),
    // Follow running work every 2 seconds, stop once everything is finished
    refetchInterval: (q) => (q.state.data?.some((t) => t.status === 'Queued' || t.status === 'Running') ? 2000 : false),
  })
  const fail = (e: unknown) => message.error(e instanceof ApiError ? e.message : 'Không thực hiện được.')
  const start = useMutation({
    mutationFn: ({ kind, file }: { kind: BulkKind; file: File }) => bulkApi.start(shopId, kind, file),
    onSuccess: () => {
      message.success('Đã nhận tệp, đang xử lý — kết quả hiện ở bảng bên dưới.')
      void queryClient.invalidateQueries({ queryKey: ['bulk-tasks', shopId] })
    },
    onError: fail,
  })
  const leaf = category.at(-1)

  const uploader = (kind: BulkKind, testId: string) => (
    <Upload accept=".xlsx" showUploadList={false} beforeUpload={(file) => { start.mutate({ kind, file }); return false }}>
      <Button type="primary" loading={start.isPending} data-testid={testId}>Tải tệp đã điền lên</Button>
    </Upload>
  )

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%', maxWidth: 1100 }}>
      <Card title="Đăng sản phẩm hàng loạt">
        <Typography.Paragraph type="secondary">
          Chọn ngành hàng cấp cuối → tải tệp mẫu (cột thuộc tính theo đúng ngành) → điền mỗi dòng một SKU, các phân loại của một sản phẩm
          dùng chung "Mã nhóm" → tải lên. Ảnh là link https; sản phẩm hợp lệ được gửi duyệt ngay. Tối đa 1.000 dòng, 5 MB.
        </Typography.Paragraph>
        <Space wrap>
          <Cascader options={toOptions(tree.data ?? [])} value={category} onChange={(v) => setCategory((v ?? []) as string[])} placeholder="Ngành hàng"
            style={{ width: 360 }} showSearch changeOnSelect={false} data-testid="bulk-category" />
          <Button disabled={!leaf} data-testid="bulk-template"
            onClick={() => leaf && void bulkApi.template(shopId, leaf).then((b) => saveBlob(b, 'mau-dang-hang.xlsx')).catch(fail)}>
            Tải tệp mẫu
          </Button>
          {uploader('ProductImport', 'bulk-import-upload')}
        </Space>
      </Card>

      <Card title="Cập nhật giá & tồn kho hàng loạt">
        <Typography.Paragraph type="secondary">
          Tải tệp gồm mọi SKU đang bán của shop, sửa cột Giá / Giá gốc / Tồn kho, rồi tải lên. Không sửa cột ID SKU. Mỗi thay đổi tồn kho
          được ghi vào lịch sử tồn kho như khi sửa trên màn hình.
        </Typography.Paragraph>
        <Space wrap>
          <Button onClick={() => void bulkApi.priceStock(shopId).then((b) => saveBlob(b, 'gia-ton-kho.xlsx')).catch(fail)} data-testid="bulk-price-download">
            Tải tệp giá & tồn kho
          </Button>
          {uploader('PriceStockUpdate', 'bulk-price-upload')}
        </Space>
      </Card>

      <Card title="Lịch sử xử lý">
        <Table<BulkTask>
          rowKey="id"
          loading={tasks.isLoading}
          dataSource={tasks.data ?? []}
          pagination={false}
          locale={{ emptyText: 'Chưa tải tệp nào' }}
          expandable={{
            rowExpandable: (t) => t.errors.length > 0,
            expandedRowRender: (t) => (
              <Table<BulkError> rowKey={(e) => `${e.row}-${e.column ?? ''}-${e.message}`} size="small" pagination={{ pageSize: 20 }} dataSource={t.errors}
                columns={[
                  { title: 'Dòng', dataIndex: 'row', width: 80 },
                  { title: 'Cột', dataIndex: 'column', width: 220, render: (c: string | null) => c ?? '—' },
                  { title: 'Lỗi', dataIndex: 'message' },
                ]} />
            ),
          }}
          columns={[
            { title: 'Việc', render: (_, t) => (t.kind === 'ProductImport' ? 'Đăng sản phẩm' : 'Giá & tồn kho') },
            { title: 'Tệp', dataIndex: 'fileName' },
            { title: 'Lúc', render: (_, t) => formatDateTime(t.createdAt), width: 160 },
            { title: 'Trạng thái', width: 130, render: (_, t) => <Tag color={STATUS[t.status].color} data-testid="bulk-status">{STATUS[t.status].label}</Tag> },
            {
              title: 'Tiến độ',
              width: 220,
              render: (_, t) => <Progress percent={t.total > 0 ? Math.round((t.processed * 100) / t.total) : 0} size="small"
                status={t.status === 'Failed' ? 'exception' : undefined} format={() => `${t.processed}/${t.total}`} />,
            },
            { title: 'Kết quả', render: (_, t) => <span data-testid="bulk-message">{t.message ?? `${t.succeeded} thành công, ${t.failed} lỗi`}</span> },
          ]}
        />
      </Card>
    </Space>
  )
}

export default BulkPage
