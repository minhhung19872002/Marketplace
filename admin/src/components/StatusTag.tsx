import type { ReactNode } from 'react'
import { Tag } from 'antd'
import { TONE_CLASS, statusInfo, type StatusMap, type Tone } from '../lib/status'

/** A tag in one of the shared tones (labels such as Mall, Bắt buộc, Chính hãng). */
export const ToneTag = ({ tone, children, title }: { tone: Tone; children: ReactNode; title?: string }) => (
  <Tag bordered={false} className={`status-tag ${TONE_CLASS[tone]}`} title={title}>{children}</Tag>
)

/** A status as a Tag: label and tone from the one map in lib/status (an API-worded label may override the text). */
const StatusTag = ({ map, value, label, title }: { map: StatusMap; value: string; label?: string | null; title?: string }) => {
  const info = statusInfo(map, value, label)
  return <ToneTag tone={info.tone} title={title}>{info.label}</ToneTag>
}

export default StatusTag
