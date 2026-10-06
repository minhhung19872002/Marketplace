import { DatePicker, Segmented, Space } from 'antd'
import dayjs from 'dayjs'
import type { Granularity, Range } from '../api/platform'
import { addDaysIso, vnTodayIso } from '../lib/datetime'

export const lastDays = (n: number, granularity: Granularity = 'Day'): Range => {
  const to = vnTodayIso()
  return { from: addDaysIso(to, -(n - 1)), to, granularity }
}

/** Vietnam calendar days, both ends included; granularity day / week / month. */
const RangePicker = ({ value, onChange }: { value: Range; onChange: (r: Range) => void }) => (
  <Space wrap>
    <DatePicker.RangePicker
      format="DD/MM/YYYY"
      allowClear={false}
      value={[dayjs(value.from), dayjs(value.to)]}
      onChange={(v) => v?.[0] && v[1] && onChange({ ...value, from: v[0].format('YYYY-MM-DD'), to: v[1].format('YYYY-MM-DD') })}
    />
    <Segmented<Granularity> value={value.granularity} onChange={(g) => onChange({ ...value, granularity: g })}
      options={[{ value: 'Day', label: 'Ngày' }, { value: 'Week', label: 'Tuần' }, { value: 'Month', label: 'Tháng' }]} />
  </Space>
)

export default RangePicker
