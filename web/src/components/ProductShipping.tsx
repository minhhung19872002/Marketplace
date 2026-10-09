import { useRef, useState, type KeyboardEvent } from 'react';
import { useQuery } from '@tanstack/react-query';
import { accountApi } from '../api/account';
import { storefrontApi } from '../api/storefront';
import { formatDate } from '../lib/datetime';
import { formatPrice } from '../lib/money';
import { Check, Truck } from 'lucide-react';
import SearchSelect from './ui/SearchSelect';

/**
 * "Vận chuyển" on the product page (II.4): the fee and expected day to the buyer's default address, computed by the
 * server with the shop's carriers; the buyer can switch to another saved address, a guest to another province.
 */
const ProductShipping = ({ productId }: { productId: string }) => {
  const [to, setTo] = useState<{ addressId?: string; province?: string }>({});
  const estimate = useQuery({ queryKey: ['shipping-estimate', productId, to], queryFn: () => storefrontApi.shipping(productId, to), staleTime: 60_000 });
  const provinces = useQuery({ queryKey: ['divisions', ''], queryFn: () => accountApi.divisions(), staleTime: Infinity, enabled: !!estimate.data });
  // The channel the buyer looks at (the cheapest by default); the real choice is made per shop at checkout
  const [chosen, setChosen] = useState<string | null>(null);
  const group = useRef<HTMLDivElement>(null);
  const e = estimate.data;
  if (!e) return null;
  const cheapest = [...e.options].sort((a, b) => a.fee - b.fee)[0];
  const picked = e.destination.addressId ?? `p:${e.destination.provinceCode}`;
  const selected = e.options.find((o) => o.code === chosen) ?? cheapest;

  /** Arrow keys move the choice between the cards, like a native radio group. */
  const onKey = (ev: KeyboardEvent<HTMLDivElement>) => {
    const step = ev.key === 'ArrowRight' || ev.key === 'ArrowDown' ? 1 : ev.key === 'ArrowLeft' || ev.key === 'ArrowUp' ? -1 : 0;
    if (!step || !selected) return;
    ev.preventDefault();
    const index = (e.options.findIndex((o) => o.code === selected.code) + step + e.options.length) % e.options.length;
    setChosen(e.options[index].code);
    group.current?.querySelectorAll<HTMLElement>('[role="radio"]')[index]?.focus();
  };

  return (
    <div className="product-detail-row product-detail-row-top" data-testid="pd-shipping">
      <span className="row-label">Vận chuyển</span>
      <div className="pd-shipping">
        <div className="pd-shipping-to">
          <Truck size={18} className="pd-shipping-icon" aria-hidden />
          <span>Vận chuyển tới</span>
          {/* Saved addresses first, then the 34 provinces — filterable (P1) */}
          <SearchSelect label="Vận chuyển tới" placeholder={e.destination.label} value={picked} testId="pd-shipping-to"
            options={[
              ...e.myAddresses.map((a) => ({ value: a.addressId!, label: a.label })),
              ...(provinces.data ?? []).map((p) => ({ value: `p:${p.code}`, label: p.name })),
              ...(!provinces.data && !e.destination.addressId ? [{ value: picked, label: e.destination.label }] : []),
            ]}
            onChange={(v) => setTo(v.startsWith('p:') ? { province: v.slice(2) } : { addressId: v })} />
        </div>
        {e.fromProvinceName && <span className="pd-shipping-from">Gửi từ {e.fromProvinceName}</span>}
        {cheapest ? (
          <div className="pd-shipping-options" role="radiogroup" aria-label="Đơn vị vận chuyển" ref={group} onKeyDown={onKey}>
            {e.options.map((o) => {
              const active = o.code === selected?.code;
              return (
                <div key={o.code} className={`pd-ship-card ${active ? 'is-active' : ''}`} role="radio" aria-checked={active} tabIndex={active ? 0 : -1}
                  onClick={() => setChosen(o.code)} onKeyDown={(ev) => { if (ev.key === ' ' || ev.key === 'Enter') { ev.preventDefault(); setChosen(o.code); } }}
                  data-testid="pd-shipping-option">
                  <span className="pd-ship-card-head">
                    <strong className="pd-ship-card-name">{o.name}</strong>
                    <span className="pd-ship-card-fee">{formatPrice(o.fee)}</span>
                  </span>
                  <span className="pd-ship-card-eta">Nhận dự kiến {formatDate(`${o.expectedDate}T12:00:00+07:00`)}</span>
                  {!o.supportsCod && <span className="pd-shipping-nocod">Không nhận COD</span>}
                  {active && <span className="pd-ship-card-tick" aria-hidden><Check size={10} strokeWidth={3} /></span>}
                </div>
              );
            })}
          </div>
        ) : (
          <span className="pd-shipping-none">Chưa có đơn vị vận chuyển giao tới địa chỉ này.</span>
        )}
      </div>
    </div>
  );
};

export default ProductShipping;
