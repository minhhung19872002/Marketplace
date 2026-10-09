import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { ChartPoint } from '../api/platform'
import { cssVars } from '../theme'
import { useThemeMode } from '../stores/themeMode'
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
  // Colours from the theme tokens of the current mode (SVG attributes cannot read CSS variables)
  const t = cssVars[useThemeMode((s) => s.mode)]
  const axis = { stroke: t['--sh-border-strong'], tick: { fill: t['--sh-text-muted'], fontSize: 12 } }
  const tooltip = { contentStyle: { background: t['--sh-surface'], border: `1px solid ${t['--sh-border-strong']}`, borderRadius: 8, color: t['--sh-text'] } }
  const data = points.map((p) => ({ label: p.label, [series]: p.value, ...(series2 ? { [series2]: p.value2 ?? 0 } : {}) }))
  if (points.length === 0) return null
  return (
    <ResponsiveContainer width="100%" height={height}>
      {kind === 'line' ? (
        <LineChart data={data}>
          <CartesianGrid strokeDasharray="3 3" stroke={t['--sh-border']} vertical={false} />
          <XAxis dataKey="label" {...axis} minTickGap={16} />
          <YAxis yAxisId="a" tickFormatter={short} {...axis} />
          {series2 && <YAxis yAxisId="b" orientation="right" tickFormatter={short} {...axis} />}
          <Tooltip formatter={(v: number) => formatNumber(v)} {...tooltip} />
          <Legend />
          <Line yAxisId="a" type="monotone" dataKey={series} stroke={t['--sh-primary']} strokeWidth={2} dot={false} />
          {series2 && <Line yAxisId="b" type="monotone" dataKey={series2} stroke={t['--sh-info']} strokeWidth={2} dot={false} />}
        </LineChart>
      ) : (
        <BarChart data={data} layout={kind === 'funnel' ? 'vertical' : 'horizontal'}>
          <CartesianGrid strokeDasharray="3 3" stroke={t['--sh-border']} vertical={false} />
          {kind === 'funnel' ? <XAxis type="number" tickFormatter={short} {...axis} /> : <XAxis dataKey="label" interval={0} angle={-20} textAnchor="end" height={70} {...axis} />}
          {kind === 'funnel' ? <YAxis type="category" dataKey="label" width={120} {...axis} /> : <YAxis tickFormatter={short} {...axis} />}
          <Tooltip formatter={(v: number) => formatNumber(v)} {...tooltip} />
          <Legend />
          <Bar dataKey={series} fill={t['--sh-primary']} radius={[4, 4, 0, 0]} />
          {series2 && <Bar dataKey={series2} fill={t['--sh-info']} radius={[4, 4, 0, 0]} />}
        </BarChart>
      )}
    </ResponsiveContainer>
  )
}

export default ReportChart
