import { useEffect, useState } from 'react';
import { countdownParts } from '../lib/datetime';

/**
 * Counts down to `endAt` on the server's clock: `skewMs` is how far the browser clock is ahead of the server
 * (spec 3.10 — never trust the visitor's clock for a sale deadline).
 */
const Countdown = ({ endAt, skewMs, onDone }: { endAt: string; skewMs: number; onDone?: () => void }) => {
  const remaining = () => Date.parse(endAt) - (Date.now() - skewMs);
  const [left, setLeft] = useState(remaining);
  useEffect(() => {
    const timer = setInterval(() => {
      const r = remaining();
      setLeft(r);
      if (r <= 0) {
        clearInterval(timer);
        onDone?.();
      }
    }, 1000);
    return () => clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [endAt, skewMs]);
  const [h, m, s] = countdownParts(left);
  return (
    <span className="countdown" data-testid="countdown" aria-label={`Còn ${h} giờ ${m} phút ${s} giây`}>
      <span>{h}</span>:<span>{m}</span>:<span>{s}</span>
    </span>
  );
};

export default Countdown;
