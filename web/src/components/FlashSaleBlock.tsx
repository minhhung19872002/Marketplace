import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { clockSkew, marketingApi, type FlashBoard } from '../api/marketing';
import { formatPrice } from '../lib/money';
import { formatClock } from '../lib/datetime';
import { handleImgError, imageOrPlaceholder } from '../lib/image';
import Countdown from './Countdown';
import './FlashSaleBlock.css';

/**
 * Flash Sale of the platform: the running slot (or the next one) with a countdown on the server's clock and a real
 * "Đã bán" bar from the units sold (spec II.1, 3.10).
 */
export const FlashSaleBoard = ({ board, compact = false }: { board: FlashBoard; compact?: boolean }) => {
  const queryClient = useQueryClient();
  const [skew] = useState(() => clockSkew(board.serverTime, Date.now()));
  if (!board.slot || board.items.length === 0) return null;
  const slot = board.slot;
  return (
    <section className="flash-sale" data-testid="flash-sale">
      <div className="flash-sale-head">
        <span className="flash-sale-title">⚡ FLASH SALE</span>
        <span className="flash-sale-when">
          {slot.running ? 'Kết thúc sau' : `Bắt đầu lúc ${formatClock(slot.startAt)} — còn`}
          <Countdown endAt={slot.running ? slot.endAt : slot.startAt} skewMs={skew}
            onDone={() => void queryClient.invalidateQueries({ queryKey: ['flash-sale'] })} />
        </span>
        {!compact && board.upcoming.length > 0 && (
          <span className="flash-sale-next">Khung sau: {board.upcoming.map((s) => formatClock(s.startAt)).join(' · ')}</span>
        )}
      </div>
      <div className="flash-sale-items">
        {board.items.slice(0, compact ? 6 : 12).map((i) => (
          <Link key={i.itemId} to={`/san-pham/${i.productId}`} className="flash-item" data-testid="flash-item">
            <span className="flash-item-image">
              <img src={imageOrPlaceholder(i.imageUrl)} alt={i.name} loading="lazy" onError={handleImgError} />
              {i.discountPercent > 0 && <span className="flash-item-badge">-{i.discountPercent}%</span>}
            </span>
            <span className="flash-item-price">{formatPrice(i.flashPrice)}</span>
            <span className="flash-item-bar" role="progressbar" aria-valuenow={i.soldPercent} aria-valuemin={0} aria-valuemax={100}>
              <span className="flash-item-bar-fill" style={{ width: `${Math.max(i.soldPercent, 8)}%` }} />
              <span className="flash-item-bar-text">
                {i.sold >= i.quota ? 'ĐÃ HẾT' : i.soldPercent >= 80 ? 'SẮP CHÁY HÀNG' : `ĐÃ BÁN ${i.sold}`}
              </span>
            </span>
          </Link>
        ))}
      </div>
    </section>
  );
};

const FlashSaleBlock = () => {
  const { data } = useQuery({ queryKey: ['flash-sale'], queryFn: () => marketingApi.flashSale(), staleTime: 30_000, refetchInterval: 60_000 });
  return data ? <FlashSaleBoard key={data.serverTime} board={data} /> : null;
};

export default FlashSaleBlock;
