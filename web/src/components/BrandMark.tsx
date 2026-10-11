/** ShopHub mark: a shopping bag with an "H" cut out (the bag takes currentColor, the cut `.brand-mark-cut`'s stroke). */
const BrandMark = ({ size = 50, className }: { size?: number; className?: string }) => (
  <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" className={className}>
    <path d="M12 10.5a4 4 0 0 1 8 0" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" />
    <path d="M8.2 10.5h15.6l-1.3 14.2a2.2 2.2 0 0 1-2.2 2H11.7a2.2 2.2 0 0 1-2.2-2L8.2 10.5z" fill="currentColor" />
    <path className="header-logo-cut brand-mark-cut" d="M13 14.5v8M19 14.5v8M13 18.5h6" fill="none" strokeWidth="2.2" strokeLinecap="round" />
  </svg>
);

export default BrandMark;
