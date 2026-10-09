import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ChevronRight, Flame, Zap } from 'lucide-react';
import { clockSkew, marketingApi, type FlashBoard, type FlashBoardItem } from '../api/marketing';
import { formatPrice } from '../lib/money';
import { formatClock } from '../lib/datetime';
import { handleImgError, imageOrPlaceholder, imageSrcSet } from '../lib/image';
import Countdown from './Countdown';
import Carousel from './ui/Carousel';
import './FlashSaleBlock.css';

/** Text of the progress bar: real units sold of the slot's quota (spec 3.10). */
export const flashBarLabel = (i: Pick<FlashBoardItem, 'sold' | 'quota' | 'soldPercent'>): string =>
  i.sold >= i.quota ? 'Đã hết' : i.sold === 0 ? 'Vừa mở bán' : i.soldPercent >= 80 ? 'Sắp cháy hàng' : `Đã bán ${i.sold}`;

const FlashItem = ({ i }: { i: FlashBoardItem }) => {
  const hot = i.sold < i.quota && i.soldPercent >= 80;
  return (
    <Link to={`/san-pham/${i.productId}`} className="flash-item" data-testid="flash-item">
      <span className="flash-item-image">
        <img src={imageOrPlaceholder(i.imageUrl)} srcSet={imageSrcSet(i.imageUrl)} sizes="200px" alt={i.name} loading="lazy" width={200} height={200} onError={handleImgError} />
        {i.discountPercent > 0 && <span className="flash-item-badge">-{i.discountPercent}%</span>}
      </span>
      <span className="flash-item-price price">{formatPrice(i.flashPrice)}</span>
      {i.basePrice > i.flashPrice && <span className="flash-item-original price">{formatPrice(i.basePrice)}</span>}
      <span className="flash-item-bar" role="progressbar" aria-label={`Đã bán ${i.sold} trên ${i.quota} suất`}
        aria-valuenow={i.soldPercent} aria-valuemin={0} aria-valuemax={100}>
        <span className="flash-item-bar-fill" style={{ width: `${Math.min(100, i.soldPercent)}%` }} />
        <span className="flash-item-bar-text">
          {hot && <Flame size={12} fill="currentColor" aria-hidden />}
          {flashBarLabel(i)}
        </span>
      </span>
    </Link>
  );
};

/**
 * Flash Sale of the platform: the running slot (or the next one) with a countdown on the server's clock and a real
 * "Đã bán" bar from the units sold of the slot's quota (spec II.1, 3.10). Home: one sliding row; /flash-sale: a grid.
 */
export const FlashSaleBoard = ({ board, compact = false, full = false }: { board: FlashBoard; compact?: boolean; full?: boolean }) => {
  const queryClient = useQueryClient();
  const [skew] = useState(() => clockSkew(board.serverTime, Date.now()));
  if (!board.slot || board.items.length === 0) return null;
  const slot = board.slot;
  const items = full ? board.items : board.items.slice(0, 18);
  return (
    <section className="flash-sale" data-testid="flash-sale">
      <div className="flash-sale-head">
        <h2 className="flash-sale-title"><Zap size={24} fill="currentColor" aria-hidden /> Flash Sale</h2>
        <span className="flash-sale-when">
          <span className="flash-sale-when-label">{slot.running ? 'Kết thúc sau' : `Bắt đầu lúc ${formatClock(slot.startAt)} — còn`}</span>
          <Countdown endAt={slot.running ? slot.endAt : slot.startAt} skewMs={skew}
            onDone={() => void queryClient.invalidateQueries({ queryKey: ['flash-sale'] })} />
        </span>
        {!compact && board.upcoming.length > 0 && (
          <span className="flash-sale-next">Khung sau: {board.upcoming.map((s) => formatClock(s.startAt)).join(' · ')}</span>
        )}
        {!full && <Link to="/flash-sale" className="flash-sale-all" data-testid="flash-sale-all">Xem tất cả <ChevronRight size={16} aria-hidden /></Link>}
      </div>
      {full ? (
        <div className="flash-sale-grid">{items.map((i) => <FlashItem key={i.itemId} i={i} />)}</div>
      ) : (
        <div className="flash-sale-items">
          <Carousel label="Flash Sale">{items.map((i) => <FlashItem key={i.itemId} i={i} />)}</Carousel>
        </div>
      )}
    </section>
  );
};

const FlashSaleBlock = () => {
  const { data } = useQuery({ queryKey: ['flash-sale'], queryFn: () => marketingApi.flashSale(), staleTime: 30_000, refetchInterval: 60_000 });
  // Holds its height while loading (CLS); a time with no running slot then folds it away
  if (!data) return <section className="flash-sale flash-sale--loading" aria-hidden />;
  return <FlashSaleBoard key={data.serverTime} board={data} />;
};

export default FlashSaleBlock;
