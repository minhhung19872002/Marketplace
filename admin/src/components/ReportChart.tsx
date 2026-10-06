import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { ChartPoint } from '../api/platform'
import { palette } from '../theme'
import { formatNumber } from '../lib/money'

const short = (v: number) =>
  Math.abs(v) >= 1_000_000_000 ? `${(v / 1_000_000_000).toFixed(1)} tỷ`
    : Math.abs(v) >= 1_000_000 ? `${(v / 1_000_000).toFixed(1)} tr`
      : Math.abs(v) >= 1_000 ? `${(v / 1_000).toFixed(0)}k` : String(v)

/** Line chart for series over time, bar chart for rankings / funnels. The labels come from the API (Vietnam days). */
const ReportChart = ({ kind, series, series2, points, height = 280 }: {
  kind: 'line' | 'bar' | 'funnel'
  series: string
  series2?: string | null
  points: ChartPoint[]
  height?: number
}) => {
  const data = points.map((p) => ({ label: p.label, [series]: p.value, ...(series2 ? { [series2]: p.value2 ?? 0 } : {}) }))
  if (points.length === 0) return null
  return (
    <ResponsiveContainer width="100%" height={height}>
      {kind === 'line' ? (
        <LineChart data={data}>
          <CartesianGrid strokeDasharray="3 3" />
          <XAxis dataKey="label" />
          <YAxis yAxisId="a" tickFormatter={short} />
          {series2 && <YAxis yAxisId="b" orientation="right" tickFormatter={short} />}
          <Tooltip formatter={(v: number) => formatNumber(v)} />
          <Legend />
          <Line yAxisId="a" type="monotone" dataKey={series} stroke={palette.primary} dot={false} />
          {series2 && <Line yAxisId="b" type="monotone" dataKey={series2} stroke={palette.secondary} dot={false} />}
        </LineChart>
      ) : (
        <BarChart data={data} layout={kind === 'funnel' ? 'vertical' : 'horizontal'}>
          <CartesianGrid strokeDasharray="3 3" />
          {kind === 'funnel' ? <XAxis type="number" tickFormatter={short} /> : <XAxis dataKey="label" interval={0} angle={-20} textAnchor="end" height={70} />}
          {kind === 'funnel' ? <YAxis type="category" dataKey="label" width={120} /> : <YAxis tickFormatter={short} />}
          <Tooltip formatter={(v: number) => formatNumber(v)} />
          <Legend />
          <Bar dataKey={series} fill={palette.primary} />
          {series2 && <Bar dataKey={series2} fill={palette.secondary} />}
        </BarChart>
      )}
    </ResponsiveContainer>
  )
}

export default ReportChart
