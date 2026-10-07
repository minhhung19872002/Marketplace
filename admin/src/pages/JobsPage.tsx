import { App, Button, Card, Space, Table, Tag, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { jobsApi, type BackupRun, type JobRow } from '../api/jobs'
import { ApiError } from '../api/http'
import { P, can } from '../permissions'
import { formatDateTime } from '../lib/datetime'

const STATE_COLOR: Record<string, string> = { Succeeded: 'green', Failed: 'red', Processing: 'blue', Enqueued: 'gold' }

/** Việc nền (spec 6.4): schedules, next / last run, last failure; "Chạy ngay"; the Hangfire dashboard through a one-use ticket. */
const JobsPage = ({ permissions }: { permissions: string[] }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const jobs = useQuery({ queryKey: ['jobs'], queryFn: jobsApi.list, refetchInterval: 15_000 })
  const backups = useQuery({ queryKey: ['backups'], queryFn: jobsApi.backups, refetchInterval: 15_000 })
  const run = useMutation({
    mutationFn: (id: string) => jobsApi.run(id),
    onSuccess: (r) => {
      message.success(r.message)
      void queryClient.invalidateQueries({ queryKey: ['jobs'] })
      void queryClient.invalidateQueries({ queryKey: ['backups'] })
    },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không chạy được việc nền.'),
  })
  const openDashboard = async () => {
    try {
      const { url } = await jobsApi.ticket()
      window.open(url, '_blank', 'noopener')
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : 'Không mở được bảng việc nền.')
    }
  }
  const canRun = can(permissions, P.JobRun)
  return (
    <Card title="Việc nền" extra={<Button onClick={openDashboard} data-testid="open-hangfire">Mở bảng Hangfire</Button>}>
      <Typography.Paragraph type="secondary">
        Lịch chạy sửa ở Tham số hệ thống (nhóm Việc nền); thời điểm hiển thị theo giờ Việt Nam.
      </Typography.Paragraph>
      <Table<JobRow> rowKey="id" size="small" loading={jobs.isLoading} dataSource={jobs.data ?? []} pagination={false}
        locale={{ emptyText: jobs.isError ? 'Không tải được danh sách việc nền.' : 'Chưa có việc nền nào được đăng ký.' }}
        columns={[
          { title: 'Mã việc', dataIndex: 'id' },
          { title: 'Lịch (cron)', dataIndex: 'cron', render: (v: string, r) => `${v}${r.timeZone ? ` · ${r.timeZone}` : ''}` },
          { title: 'Lần tới', dataIndex: 'nextExecution', render: (v: string | null) => (v ? formatDateTime(`${v}${v.endsWith('Z') ? '' : 'Z'}`) : '—') },
          { title: 'Lần gần nhất', dataIndex: 'lastExecution', render: (v: string | null) => (v ? formatDateTime(`${v}${v.endsWith('Z') ? '' : 'Z'}`) : '—') },
          { title: 'Kết quả', dataIndex: 'lastState', render: (v: string | null) => (v ? <Tag color={STATE_COLOR[v]}>{v}</Tag> : '—') },
          { title: 'Lỗi', dataIndex: 'lastError', render: (v: string | null) => v ?? '' },
          {
            title: '', render: (_, r) => canRun && r.runnable && (
              <Space><Button size="small" loading={run.isPending && run.variables === r.id} onClick={() => run.mutate(r.id)}>Chạy ngay</Button></Space>
            ),
          },
        ]} />
      <Typography.Title level={5} style={{ marginTop: 24 }}>
        Bản sao lưu CSDL {backups.data && <Typography.Text type="secondary">(giữ {backups.data.keepCount} bản mới nhất)</Typography.Text>}
      </Typography.Title>
      {canRun && <Button onClick={() => run.mutate('sys.backup')} loading={run.isPending && run.variables === 'sys.backup'} data-testid="backup-now">Sao lưu ngay</Button>}
      <Table<BackupRun> rowKey="id" size="small" loading={backups.isLoading} dataSource={backups.data?.runs ?? []} pagination={false} style={{ marginTop: 12 }}
        locale={{ emptyText: backups.isError ? 'Không tải được danh sách sao lưu.' : 'Chưa có lần sao lưu nào.' }}
        columns={[
          { title: 'Tệp', dataIndex: 'fileName', render: (v: string) => (backups.data?.files.some((f) => f.name === v) ? v : <Typography.Text delete>{v}</Typography.Text>) },
          { title: 'Cỡ', dataIndex: 'sizeBytes', align: 'right', render: (v: number | null) => (v === null ? '' : `${(v / 1024 / 1024).toFixed(1)} MB`) },
          { title: 'Bắt đầu', dataIndex: 'startedAt', render: (v: string) => formatDateTime(v) },
          { title: 'Trạng thái', dataIndex: 'status', render: (v: string) => <Tag color={STATE_COLOR[v === 'Running' ? 'Processing' : v]}>{v === 'Succeeded' ? 'Thành công' : v === 'Failed' ? 'Thất bại' : 'Đang chạy'}</Tag> },
          { title: 'Lỗi', dataIndex: 'error', render: (v: string | null) => v ?? '' },
        ]} />
    </Card>
  )
}

export default JobsPage
