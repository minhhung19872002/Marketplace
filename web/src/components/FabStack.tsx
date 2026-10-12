import { useEffect, useState, type ReactNode } from 'react';

const PHONE = '(max-width: 767px)';
// Scrolled this far down before the buttons tuck away; the page's top always shows them
const TUCK_AFTER_PX = 160;
// A small scroll back up is enough to bring them back
const SHOW_ON_UP_PX = 24;

/**
 * Floating actions bottom-right (P0-6). On phones they tuck away while the page scrolls down — the reading direction,
 * when the round Chat button would sit on top of a price or a card's buttons (G8 D) — and come back on any scroll up
 * or at the end of the page.
 */
const FabStack = ({ children }: { children: ReactNode }) => {
  const [tucked, setTucked] = useState(false);

  useEffect(() => {
    let last = window.scrollY;
    let upFrom = last;
    const onScroll = () => {
      const y = window.scrollY;
      const atEnd = y + window.innerHeight >= document.documentElement.scrollHeight - 8;
      if (!window.matchMedia(PHONE).matches || y < TUCK_AFTER_PX || atEnd) setTucked(false);
      else if (y > last) { setTucked(true); upFrom = y; }
      else if (upFrom - y >= SHOW_ON_UP_PX) setTucked(false);
      last = y;
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  return <div className={`fab-stack${tucked ? ' is-tucked' : ''}`} data-testid="fab-stack">{children}</div>;
};

export default FabStack;
