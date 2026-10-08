import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Link, type LinkProps } from 'react-router-dom';
import { ChevronLeft, ChevronRight, PackageOpen, Star, type LucideIcon } from 'lucide-react';
import { pageWindow } from '../../lib/text';
import './ui.css';

// Design system primitives shared by every page (G0). Icons come from lucide-react — never emoji.

type Variant = 'primary' | 'outline' | 'secondary' | 'ghost';
type Size = 'sm' | 'md' | 'lg';

const buttonClass = (variant: Variant, size: Size, block?: boolean, extra?: string) =>
  ['sh-btn', `sh-btn--${variant}`, `sh-btn--${size}`, block && 'sh-btn--block', extra].filter(Boolean).join(' ');

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
  block?: boolean;
  icon?: LucideIcon;
}

export const Button = ({ variant = 'primary', size = 'md', block, icon: Icon, className, children, type = 'button', ...rest }: ButtonProps) => (
  <button type={type} className={buttonClass(variant, size, block, className)} {...rest}>
    {Icon && <Icon size={size === 'sm' ? 14 : 18} aria-hidden />}
    {children}
  </button>
);

interface ButtonLinkProps extends LinkProps {
  variant?: Variant;
  size?: Size;
  block?: boolean;
  icon?: LucideIcon;
}

export const ButtonLink = ({ variant = 'primary', size = 'md', block, icon: Icon, className, children, ...rest }: ButtonLinkProps) => (
  <Link className={buttonClass(variant, size, block, className)} {...rest}>
    {Icon && <Icon size={size === 'sm' ? 14 : 18} aria-hidden />}
    {children}
  </Link>
);

export type BadgeTone = 'mall' | 'preferred' | 'discount' | 'top' | 'neutral' | 'success' | 'outline';

export const Badge = ({ tone, children, className }: { tone: BadgeTone; children: ReactNode; className?: string }) => (
  <span className={['sh-badge', `sh-badge--${tone}`, className].filter(Boolean).join(' ')}>{children}</span>
);

export const Skeleton = ({ width, height = 14, className }: { width?: number | string; height?: number | string; className?: string }) => (
  <span className={['sh-skeleton', className].filter(Boolean).join(' ')} style={{ width, height }} aria-hidden />
);

/** Placeholder of one product card while a grid loads */
export const ProductCardSkeleton = () => (
  <div className="sh-skeleton-card" aria-hidden>
    <Skeleton />
    <Skeleton height={14} />
    <Skeleton height={14} width="60%" />
    <Skeleton height={18} width="45%" />
  </div>
);

/** Grid of card skeletons, announced once to screen readers */
export const ProductGridSkeleton = ({ count = 12, className = 'product-grid' }: { count?: number; className?: string }) => (
  <div className={className} role="status" aria-label="Đang tải sản phẩm">
    {Array.from({ length: count }, (_, i) => <ProductCardSkeleton key={i} />)}
  </div>
);

interface EmptyStateProps {
  icon?: LucideIcon;
  title: ReactNode;
  text?: ReactNode;
  action?: ReactNode;
  testId?: string;
}

export const EmptyState = ({ icon: Icon = PackageOpen, title, text, action, testId }: EmptyStateProps) => (
  <div className="sh-empty" data-testid={testId}>
    <div className="sh-empty__art"><Icon size={44} strokeWidth={1.5} aria-hidden /></div>
    <div className="sh-empty__title">{title}</div>
    {text && <div className="sh-empty__text">{text}</div>}
    {action}
  </div>
);

/** 0–5 stars with halves; the number itself is announced through aria-label */
export const Stars = ({ value, size = 12 }: { value: number; size?: number }) => (
  <span className="sh-stars" role="img" aria-label={`${value.toFixed(1)} trên 5 sao`}>
    {[1, 2, 3, 4, 5].map((i) => {
      if (value >= i - 0.25) return <Star key={i} size={size} fill="currentColor" strokeWidth={0} aria-hidden />;
      if (value >= i - 0.75)
        return (
          <span key={i} className="sh-stars__half">
            <Star size={size} className="sh-stars__off" fill="currentColor" strokeWidth={0} aria-hidden />
            <Star size={size} fill="currentColor" strokeWidth={0} aria-hidden />
          </span>
        );
      return <Star key={i} size={size} className="sh-stars__off" fill="currentColor" strokeWidth={0} aria-hidden />;
    })}
  </span>
);

interface SectionProps {
  title: ReactNode;
  more?: { to: string; label?: string };
  extra?: ReactNode;
  className?: string;
  children: ReactNode;
  testId?: string;
}

/** White block with an UPPERCASE header and an optional "Xem tất cả" link */
export const Section = ({ title, more, extra, className, children, testId }: SectionProps) => (
  <section className={['sh-section', className].filter(Boolean).join(' ')} data-testid={testId}>
    <div className="sh-section__head">
      <h2 className="sh-section-title">{title}</h2>
      {extra}
      {more && (
        <Link to={more.to} className="sh-section__more">
          {more.label ?? 'Xem tất cả'} <ChevronRight size={16} aria-hidden />
        </Link>
      )}
    </div>
    {children}
  </section>
);

/** Numbered pager (G2): ‹ 1 … 4 5 6 … 20 ›. Renders nothing for a single page. */
export const Pager = ({ page, total, onPage, testId, label = 'Phân trang' }: { page: number; total: number; onPage: (p: number) => void; testId?: string; label?: string }) => {
  if (total <= 1) return null;
  return (
    <nav className="sh-pager" aria-label={label} data-testid={testId}>
      <button type="button" className="sh-pager__btn" disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label="Trang trước">
        <ChevronLeft size={18} aria-hidden />
      </button>
      {pageWindow(page, total).map((p, i) => p === '…'
        ? <span key={`gap-${i}`} className="sh-pager__gap" aria-hidden>…</span>
        : (
          <button key={p} type="button" className={`sh-pager__btn ${p === page ? 'is-current' : ''}`} onClick={() => onPage(p)}
            aria-current={p === page ? 'page' : undefined} aria-label={`Trang ${p}`}>{p}</button>
        ))}
      <button type="button" className="sh-pager__btn" disabled={page >= total} onClick={() => onPage(page + 1)} aria-label="Trang sau" data-testid="next-page">
        <ChevronRight size={18} aria-hidden />
      </button>
    </nav>
  );
};
