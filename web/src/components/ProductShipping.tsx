import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { accountApi } from '../api/account';
import { storefrontApi } from '../api/storefront';
import { formatDate } from '../lib/datetime';
import { formatPrice } from '../lib/money';
import { Truck } from 'lucide-react';
import SearchSelect from './ui/SearchSelect';

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
