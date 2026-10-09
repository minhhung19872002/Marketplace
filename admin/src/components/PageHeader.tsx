import type { ReactNode } from 'react'
import { Typography } from 'antd'

export interface PageHeaderProps {
  title: ReactNode
  /** One short line under the title: what the page is for. */
  description?: ReactNode
  /** The page's primary action (and at most one or two secondary ones), shown on the right. */
  actions?: ReactNode
}

/** Title + short description on the left, the page's actions on the right — the top of every admin page. */
const PageHeader = ({ title, description, actions }: PageHeaderProps) => (
  <div className="page-header">
    <div className="page-header-text">
      <Typography.Title level={3} className="page-header-title">{title}</Typography.Title>
      {description && <Typography.Text type="secondary">{description}</Typography.Text>}
    </div>
    {actions && <div className="page-header-actions">{actions}</div>}
  </div>
)

export default PageHeader
