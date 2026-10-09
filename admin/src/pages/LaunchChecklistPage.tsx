import { Alert, Button, Typography } from 'antd'
import { ReloadOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { platformApi, type LaunchCheck } from '../api/platform'
import { formatDateTime } from '../lib/datetime'
import DataTable from '../components/DataTable'
import { ToneTag } from '../components/StatusTag'

const STATUS = {
  Pass: { tone: 'success', label: 'Đạt' },
  Warn: { tone: 'warning', label: 'Cảnh báo' },
  Fail: { tone: 'error', label: 'Chưa đạt' },
} as const

/** Kiểm tra trước khi mở bán (G4-D): what still stands between this install and real sales — see docs/09. */
const LaunchChecklistPage = () => {
  const check = useQuery({ queryKey: ['launch-checklist'], queryFn: platformApi.launchChecklist })
  const data = check.data
  return (
    <>
      {data && (
        <Alert
          style={{ marginBottom: 16 }}
          type={data.ready ? 'success' : 'warning'}
          showIcon
          data-testid="launch-ready"
          message={data.ready ? 'Đủ điều kiện mở bán' : 'Chưa đủ điều kiện mở bán'}
          description={`Đạt ${data.passed}/${data.total} mục · kiểm lúc ${formatDateTime(data.checkedAt)}. Mục "chặn" chưa đạt thì chưa được mở bán thật.`}
        />
      )}
      <DataTable<LaunchCheck>
        header={{
          title: 'Kiểm tra trước khi mở bán',
          description: 'Mỗi mục được đo trên hệ thống đang chạy (tham số, cấu hình máy chủ, dữ liệu, sao lưu) — không tích tay',
          actions: <Button icon={<ReloadOutlined />} onClick={() => void check.refetch()} loading={check.isFetching}>Kiểm lại</Button>,
        }}
        rowKey="id"
        loading={check.isPending}
        error={check.error}
        dataSource={data?.items ?? []}
        paging={false}
        columns={[
          { title: 'Nhóm', dataIndex: 'group', width: 180 },
          { title: 'Mục kiểm', dataIndex: 'title', render: (t: string, r) => <span data-testid={`launch-${r.id}`}>{t}</span> },
          { title: 'Kết quả', dataIndex: 'status', width: 120, render: (s: LaunchCheck['status']) => <ToneTag tone={STATUS[s].tone}>{STATUS[s].label}</ToneTag> },
          { title: 'Chặn mở bán', dataIndex: 'blocking', width: 120, render: (b: boolean) => (b ? 'Có' : 'Không') },
          { title: 'Chi tiết', dataIndex: 'detail', render: (d: string) => <Typography.Text type="secondary">{d}</Typography.Text> },
        ]}
      />
    </>
  )
}

export default LaunchChecklistPage
