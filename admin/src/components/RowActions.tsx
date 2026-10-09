import type { ReactNode } from 'react'
import { App as AntApp, Button, Dropdown } from 'antd'
import type { MenuProps } from 'antd'
import { MoreOutlined } from '@ant-design/icons'

export interface RowAction {
  key: string
  label: ReactNode
  icon?: ReactNode
  onClick: () => unknown
  /** Destructive: shown in red. */
  danger?: boolean
  /** Asked first through Modal.confirm (required for a destructive item that does not open its own dialog). */
  confirm?: { title: ReactNode; content?: ReactNode; okText: string }
  hidden?: boolean
  disabled?: boolean
  testId?: string
}

export interface RowActionsProps {
  /** The one action used most on this row (a small Button), or nothing. */
  primary?: ReactNode
  /** Everything else, in the "⋯" menu; destructive items last, after a divider. */
  items?: RowAction[]
  /** Names the row for screen readers: "Thao tác khác: <name>". */
  name: string
  testId?: string
}

/**
 * One primary action + a "⋯" dropdown for the rest. Clicks never reach the row (rows that open a drawer on click stay
 * usable) — React events bubble through the dropdown's portal, so the wrapper stops them.
 */
const RowActions = ({ primary, items = [], name, testId }: RowActionsProps) => {
  const { modal } = AntApp.useApp()
  const shown = items.filter((i) => !i.hidden)
  const safe = shown.filter((i) => !i.danger)
  const risky = shown.filter((i) => i.danger)
  const toItem = (a: RowAction) => ({
    key: a.key,
    icon: a.icon,
    danger: a.danger,
    disabled: a.disabled,
    label: a.testId ? <span data-testid={a.testId}>{a.label}</span> : a.label,
    onClick: () => {
      if (!a.confirm) {
        void a.onClick()
        return
      }
      modal.confirm({
        title: a.confirm.title,
        content: a.confirm.content,
        okText: a.confirm.okText,
        okButtonProps: { danger: a.danger },
        cancelText: 'Không',
        onOk: async () => { await a.onClick() },
      })
    },
  })
  const menu: MenuProps['items'] = [...safe.map(toItem), ...(safe.length && risky.length ? [{ type: 'divider' as const }] : []), ...risky.map(toItem)]
  return (
    <span className="row-actions" onClick={(e) => e.stopPropagation()} onKeyDown={(e) => e.stopPropagation()} role="presentation">
      {primary}
      {shown.length > 0 && (
        <Dropdown trigger={['click']} placement="bottomRight" menu={{ items: menu }}>
          <Button size="small" type="text" icon={<MoreOutlined />} aria-label={`Thao tác khác: ${name}`} data-testid={testId ?? 'row-more'} />
        </Dropdown>
      )}
    </span>
  )
}

export default RowActions
