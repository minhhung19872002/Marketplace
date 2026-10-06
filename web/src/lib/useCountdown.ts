import { useEffect, useState } from 'react';

/** Seconds left before an action (e.g. "resend code") is allowed again. */
export function useCountdown(): [number, (seconds: number) => void] {
  const [left, setLeft] = useState(0);

  useEffect(() => {
    if (left <= 0) return undefined;
    const timer = setTimeout(() => setLeft((s) => s - 1), 1000);
    return () => clearTimeout(timer);
  }, [left]);

  return [left, setLeft];
}
