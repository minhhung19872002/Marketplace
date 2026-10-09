/** ShopHub brand mark: bag icon + name + the app's label (same bag as the buyer site's header logo). */
const BrandMark = ({ label = 'Kênh Người Bán', inverse = false, size = 'md' }: { label?: string; inverse?: boolean; size?: 'md' | 'lg' }) => (
  <span className={`brand-mark brand-mark-${size}${inverse ? ' brand-mark-inverse' : ''}`}>
    <span className="brand-mark-icon" aria-hidden="true">
      <svg viewBox="0 0 32 32" width="100%" height="100%">
        <path d="M10 11h12l-1 12a2 2 0 0 1-2 1.8H13a2 2 0 0 1-2-1.8L10 11z" fill="none" stroke="currentColor" strokeWidth="2" strokeLinejoin="round" />
        <path d="M12.5 11a3.5 3.5 0 0 1 7 0" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
      </svg>
    </span>
    <span className="brand-mark-text">
      <span className="brand-mark-name">ShopHub</span>
      {label && <span className="brand-mark-label">{label}</span>}
    </span>
  </span>
)

export default BrandMark
