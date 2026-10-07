import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { walletApi } from '../api/commerce';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import { formatDate } from '../lib/datetime';
import { describeVoucher } from '../lib/vouchers';
import './ShopVouchers.css';

/**
 * "Voucher của shop" (II.4, II.5): the shop's running public vouchers, each with "Lưu" into the buyer's voucher wallet;
 * guests are sent to sign in first. Renders nothing when the shop has none.
 */
const ShopVouchers = ({ shopId, compact = false }: { shopId: string; compact?: boolean }) => {
  const { isLoggedIn } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const [message, setMessage] = useState('');
  const vouchers = useQuery({ queryKey: ['shop-vouchers', shopId, isLoggedIn], queryFn: () => walletApi.available(shopId), staleTime: 30_000 });
  const list = vouchers.data ?? [];
  if (list.length === 0) return null;

  const save = async (id: string) => {
    if (!isLoggedIn) {
      navigate('/dang-nhap', { state: { from: location.pathname } });
      return;
    }
    try {
      setMessage((await walletApi.claim(id)).message);
      void queryClient.invalidateQueries({ queryKey: ['shop-vouchers', shopId] });
      void queryClient.invalidateQueries({ queryKey: ['wallet'] });
    } catch (e) {
      setMessage(e instanceof ApiError ? e.message : 'Không lưu được voucher.');
    }
  };

  return (
    <div className={`shop-vouchers ${compact ? 'compact' : ''}`} data-testid="shop-vouchers">
      {!compact && <h3 className="shop-vouchers-title">Mã giảm giá của shop</h3>}
      <div className="shop-vouchers-list">
        {list.map((w) => (
          <div key={w.voucher.id} className="shop-voucher" data-testid="shop-voucher">
            <div className="shop-voucher-body">
              <strong>{describeVoucher(w.voucher)}</strong>
              <span>HSD {formatDate(w.voucher.endAt)}</span>
              {w.problem && <span className="shop-voucher-problem">{w.problem}</span>}
            </div>
            <button type="button" className="shop-voucher-save" disabled={w.claimed} onClick={() => void save(w.voucher.id)} data-testid="save-shop-voucher">
              {w.claimed ? 'Đã lưu' : 'Lưu'}
            </button>
          </div>
        ))}
      </div>
      {message && <div className="shop-vouchers-message" role="status">{message}</div>}
    </div>
  );
};

export default ShopVouchers;
