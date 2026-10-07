import { useEffect } from 'react';
import { useToasts, type Toast } from '../lib/toast';
import './Toaster.css';

const LIFETIME_MS = 3500;

const Item = ({ t }: { t: Toast }) => {
  const dismiss = useToasts((s) => s.dismiss);
  useEffect(() => {
    const timer = window.setTimeout(() => dismiss(t.id), t.kind === 'error' ? LIFETIME_MS * 1.5 : LIFETIME_MS);
    return () => window.clearTimeout(timer);
  }, [t.id, t.kind, dismiss]);
  return (
    <div className={`toast toast-${t.kind}`} role={t.kind === 'error' ? 'alert' : 'status'} data-testid="toast">
      <span>{t.text}</span>
      <button type="button" onClick={() => dismiss(t.id)} aria-label="Đóng thông báo">✕</button>
    </div>
  );
};

/** Where every toast of the buyer site appears (F4) — mounted once in App. */
const Toaster = () => {
  const items = useToasts((s) => s.items);
  return (
    <div className="toaster" aria-live="polite">
      {items.map((t) => <Item key={t.id} t={t} />)}
    </div>
  );
};

export default Toaster;
