import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { accountApi } from '../api/account';
import { storefrontApi } from '../api/storefront';
import { formatDate } from '../lib/datetime';
import { formatPrice } from '../lib/money';

/**
 * "Vận chuyển" on the product page (II.4): the fee and expected day to the buyer's default address, computed by the
 * server with the shop's carriers; the buyer can switch to another saved address, a guest to another province.
 */
const ProductShipping = ({ productId }: { productId: string }) => {
  const [to, setTo] = useState<{ addressId?: string; province?: string }>({});
  const estimate = useQuery({ queryKey: ['shipping-estimate', productId, to], queryFn: () => storefrontApi.shipping(productId, to), staleTime: 60_000 });
  const provinces = useQuery({ queryKey: ['divisions', ''], queryFn: () => accountApi.divisions(), staleTime: Infinity, enabled: !!estimate.data });
  const e = estimate.data;
  if (!e) return null;
  const cheapest = [...e.options].sort((a, b) => a.fee - b.fee)[0];
  const picked = e.destination.addressId ?? `p:${e.destination.provinceCode}`;

  return (
    <div className="product-detail-row product-detail-row-top" data-testid="pd-shipping">
      <span className="row-label">Vận Chuyển</span>
      <div className="pd-shipping">
        <label className="pd-shipping-to">
          Vận chuyển tới{' '}
          <select
            value={picked}
            aria-label="Vận chuyển tới"
            data-testid="pd-shipping-to"
            onChange={(ev) => {
              const v = ev.target.value;
              setTo(v.startsWith('p:') ? { province: v.slice(2) } : { addressId: v });
            }}
          >
            {e.myAddresses.map((a) => <option key={a.addressId} value={a.addressId!}>{a.label}</option>)}
            {(provinces.data ?? []).map((p) => <option key={p.code} value={`p:${p.code}`}>{p.name}</option>)}
            {!provinces.data && !e.destination.addressId && <option value={picked}>{e.destination.label}</option>}
          </select>
        </label>
        {e.fromProvinceName && <span className="pd-shipping-from">Gửi từ {e.fromProvinceName}</span>}
        {cheapest ? (
          <ul className="pd-shipping-options">
            {e.options.map((o) => (
              <li key={o.code} data-testid="pd-shipping-option">
                <strong>{o.name}</strong> {formatPrice(o.fee)} · Nhận dự kiến {formatDate(`${o.expectedDate}T12:00:00+07:00`)}
                {!o.supportsCod && <span className="pd-shipping-nocod"> · Không nhận COD</span>}
              </li>
            ))}
          </ul>
        ) : (
          <span className="pd-shipping-none">Chưa có đơn vị vận chuyển giao tới địa chỉ này.</span>
        )}
      </div>
    </div>
  );
};

export default ProductShipping;
