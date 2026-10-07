import { useState } from 'react';
import type { VoucherOption } from '../api/commerce';
import { formatPrice } from '../lib/money';
import './VoucherPicker.css';

/** "CODE — name (−₫discount)" as the server priced it. */
export const voucherLabel = (v: VoucherOption) =>
  `${v.code} — ${v.name}${v.usable && v.discount > 0 ? ` (−${formatPrice(v.discount)})` : ''}`;

/**
 * Voucher choice of the cart blocks and the checkout (E6): pick from the list (the unusable ones stay visible with their
 * reason, spec 3.6) or type a code.
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
  const [typed, setTyped] = useState('');
  return (
    <div className="checkout-voucher" data-testid={testId}>
      <span className="checkout-voucher-title">{title}</span>
      <select value={value ?? ''} onChange={(e) => onChange(e.target.value || null)} aria-label={title} data-testid={`${testId}-select`}>
        <option value="">— Không dùng —</option>
        {options.map((v) => (
          <option key={v.id} value={v.code} disabled={!v.usable && v.code !== value}>
            {voucherLabel(v)}{v.problem ? ` · ${v.problem}` : ''}
          </option>
        ))}
      </select>
      <input value={typed} onChange={(e) => setTyped(e.target.value.toUpperCase())} placeholder="Nhập mã" aria-label={`Nhập mã ${title}`} data-testid={`${testId}-input`} />
      <button
        type="button"
        onClick={() => {
          if (typed.trim()) onChange(typed.trim());
          setTyped('');
        }}
        data-testid={`${testId}-apply`}
      >
        Áp dụng
      </button>
      {options.filter((v) => !v.usable && v.problem).length > 0 && (
        <ul className="checkout-voucher-unusable">
          {options.filter((v) => !v.usable && v.problem).slice(0, 3).map((v) => (
            <li key={v.id}>
              <strong>{v.code}</strong>: {v.problem}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};

export default VoucherPicker;
