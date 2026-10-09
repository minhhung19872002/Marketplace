import { useCallback, useEffect, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import './ui.css';

interface Props {
  /** Accessible name of the row ("Flash Sale", "Sản phẩm đã xem"…) */
  label: string;
  children: ReactNode;
  className?: string;
  testId?: string;
  /** Rows of slides (2 for the home category grid); slides fill column by column */
  rows?: number;
  /** Custom properties for the caller's CSS (e.g. a column count) */
  style?: CSSProperties;
  /** Role of the track: a row of tabs (Flash Sale slots) is a "tablist", not a carousel region */
  trackRole?: 'region' | 'tablist';
}

/**
 * One horizontal row that scrolls (snap, swipe on touch, no browser scrollbar). Arrow buttons appear only on the side
 * that has more to show, the edges fade out. Keyboard: the row is focusable, ← / → scroll by one screen.
 * Children are the slides; their width comes from the caller (CSS grid-auto-columns via --carousel-item).
 */
const Carousel = ({ label, children, className, testId, rows = 1, style, trackRole = 'region' }: Props) => {
  const track = useRef<HTMLDivElement>(null);
  const [edges, setEdges] = useState({ start: true, end: true });

  const measure = useCallback(() => {
    const el = track.current;
    if (!el) return;
    setEdges({ start: el.scrollLeft <= 2, end: el.scrollLeft + el.clientWidth >= el.scrollWidth - 2 });
  }, []);

  useEffect(() => {
    measure();
    const el = track.current;
    if (!el) return undefined;
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, [measure, children]);

  const page = (dir: 1 | -1) => {
    const el = track.current;
    if (el) el.scrollBy({ left: dir * el.clientWidth * 0.9, behavior: 'smooth' });
  };

  const onKey = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.target !== e.currentTarget) return;
    if (e.key === 'ArrowRight') { e.preventDefault(); page(1); }
    if (e.key === 'ArrowLeft') { e.preventDefault(); page(-1); }
  };

  return (
    <div className={['sh-carousel', !edges.start && 'has-prev', !edges.end && 'has-next', className].filter(Boolean).join(' ')} data-testid={testId} style={style}>
      <div className="sh-carousel__track" ref={track} onScroll={measure} onKeyDown={onKey} tabIndex={0}
        style={rows > 1 ? { gridTemplateRows: `repeat(${rows}, auto)` } : undefined}
        role={trackRole} aria-roledescription={trackRole === 'region' ? 'carousel' : undefined} aria-label={label}>
        {children}
      </div>
      {!edges.start && (
        <button type="button" className="sh-carousel__arrow sh-carousel__arrow--prev" onClick={() => page(-1)} aria-label={`${label}: xem trước`}>
          <ChevronLeft size={20} aria-hidden />
        </button>
      )}
      {!edges.end && (
        <button type="button" className="sh-carousel__arrow sh-carousel__arrow--next" onClick={() => page(1)} aria-label={`${label}: xem tiếp`}>
          <ChevronRight size={20} aria-hidden />
        </button>
      )}
    </div>
  );
};

export default Carousel;
