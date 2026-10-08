import { useEffect, useId, useRef, useState } from 'react';
import { ChevronRight, Percent, Ticket, Truck, X } from 'lucide-react';
import type { VoucherOption } from '../api/commerce';
import { formatPrice } from '../lib/money';
import { formatDate } from '../lib/datetime';
import { describeVoucher } from '../lib/vouchers';
import './VoucherPicker.css';

/** "CODE — name (−₫discount)" as the server priced it. */
export const voucherLabel = (v: VoucherOption) =>
  `${v.code} — ${v.name}${v.usable && v.discount > 0 ? ` (−${formatPrice(v.discount)})` : ''}`;

/** Usable vouchers first (largest saving first), then the ones that cannot be used yet, each keeping the server's order. */
export const orderVouchers = (options: VoucherOption[]): VoucherOption[] =>
  [...options.filter((v) => v.usable).sort((a, b) => b.discount - a.discount), ...options.filter((v) => !v.usable)];

/** One voucher as a ticket: a coloured stub and the conditions; an unusable one says why (spec 3.6). */
const VoucherTicket = ({ v, name, checked, onPick }: { v: VoucherOption; name: string; checked: boolean; onPick: () => void }) => {
  const Icon = v.type === 'FreeShipping' ? Truck : v.type === 'Percent' ? Percent : Ticket;
  return (
    <label className={`voucher-ticket ${v.usable ? '' : 'is-unusable'} ${checked ? 'is-checked' : ''} ${v.type === 'FreeShipping' ? 'is-freeship' : ''}`}
      data-testid="voucher-ticket">
      <span className="voucher-ticket-stub" aria-hidden>
        <Icon size={26} />
        <span>{v.type === 'FreeShipping' ? 'FREESHIP' : v.code}</span>
      </span>
      <span className="voucher-ticket-body">
        <strong>{v.name}</strong>
        <span>{describeVoucher(v)}</span>
        <span className="voucher-ticket-meta">Mã {v.code} · HSD {formatDate(v.endAt)}</span>
        {v.usable && v.discount > 0 && <span className="voucher-ticket-saving">Tiết kiệm {formatPrice(v.discount)}</span>}
        {!v.usable && v.problem && <span className="voucher-ticket-problem" data-testid="voucher-problem">{v.problem}</span>}
      </span>
      <input type="radio" name={name} checked={checked} disabled={!v.usable} onChange={onPick} aria-label={voucherLabel(v)} />
    </label>
  );
};

/**
 * Voucher choice of the cart blocks and the checkout (G2-B4): the trigger shows the code in use; the dialog lists the
 * vouchers as tickets — the unusable ones stay visible with their reason ("Mua thêm ₫35.000…", spec 3.6) — and takes a
 * typed code. The server prices whatever is chosen.
 */
const VoucherPicker = ({
  title,
  options,
  value,
  onChange,
  testId,
}: {
  title: string;
  options: VoucherOption[];
  value: string | null;
  onChange: (code: string | null) => void;
  testId: string;
}) => {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<string | null>(value);
  const [typed, setTyped] = useState('');
  const dialog = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const chosen = options.find((o) => o.code === value);

  useEffect(() => {
    if (!open) return undefined;
    dialog.current?.querySelector<HTMLInputElement>('input')?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open]);

  const show = () => {
    setDraft(value);
    setTyped('');
    setOpen(true);
  };
  const apply = (code: string | null) => {
    onChange(code);
    setOpen(false);
  };
  const usableCount = options.filter((v) => v.usable).length;

  return (
    <div className="checkout-voucher" data-testid={testId}>
      <span className="checkout-voucher-title"><Ticket size={18} aria-hidden /> {title}</span>
      <button type="button" className="voucher-trigger" onClick={show} aria-haspopup="dialog" data-testid={`${testId}-open`}>
        {value ? (
          <span className="voucher-trigger-chosen" data-testid={`${testId}-value`}>
            {value}{chosen?.usable && chosen.discount > 0 ? ` · −${formatPrice(chosen.discount)}` : ''}
          </span>
        ) : (
          <span>{usableCount > 0 ? `${usableCount} mã dùng được` : 'Chọn hoặc nhập mã'}</span>
        )}
        <ChevronRight size={16} aria-hidden />
      </button>

      {open && (
        <div className="voucher-dialog" role="presentation">
          <button type="button" className="voucher-dialog-backdrop" aria-label="Đóng" tabIndex={-1} onClick={() => setOpen(false)} />
          <div className="voucher-dialog-panel" role="dialog" aria-modal="true" aria-labelledby={titleId} ref={dialog} data-testid="voucher-dialog">
            <div className="voucher-dialog-head">
              <h2 id={titleId}>{title}</h2>
              <button type="button" className="voucher-dialog-close" onClick={() => setOpen(false)} aria-label="Đóng"><X size={20} aria-hidden /></button>
            </div>
            <form className="voucher-dialog-code" onSubmit={(e) => { e.preventDefault(); if (typed.trim()) apply(typed.trim()); }}>
              <label htmlFor={`${titleId}-code`}>Mã voucher</label>
              <input id={`${titleId}-code`} value={typed} onChange={(e) => setTyped(e.target.value.toUpperCase())} placeholder="Nhập mã"
                aria-label={`Nhập mã ${title}`} data-testid={`${testId}-input`} />
              <button type="submit" disabled={!typed.trim()} data-testid={`${testId}-apply`}>Áp dụng</button>
            </form>
            <div className="voucher-dialog-list" role="radiogroup" aria-label={title}>
              {options.length === 0 && <p className="voucher-dialog-empty">Chưa có voucher nào cho đơn này.</p>}
              {orderVouchers(options).map((v) => (
                <VoucherTicket key={v.id} v={v} name={titleId} checked={draft === v.code} onPick={() => setDraft(v.code)} />
              ))}
            </div>
            <div className="voucher-dialog-foot">
              <button type="button" className="voucher-dialog-none" onClick={() => apply(null)} disabled={!value} data-testid="voucher-none">Không dùng</button>
              <button type="button" className="voucher-dialog-ok" onClick={() => apply(draft)} data-testid="voucher-confirm">OK</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default VoucherPicker;
