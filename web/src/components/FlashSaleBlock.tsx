import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { AlarmClock, ChevronRight, Flame } from 'lucide-react';
import { clockSkew, marketingApi, type FlashBoard, type FlashBoardItem } from '../api/marketing';
import { formatPrice, priceParts } from '../lib/money';
import { formatClock } from '../lib/datetime';
import { handleImgError, imageOrPlaceholder, imageSrcSet } from '../lib/image';
import Countdown from './Countdown';
import { CampaignStrip, ShopBadge } from './ProductCard';
import Carousel from './ui/Carousel';
import './FlashSaleBlock.css';

/**
 * Text of the progress bar (G-VIS): real units of the slot's quota (spec 3.10) — shown upper-case by CSS. Nothing sold
 * yet: "Vừa mở bán"; then "Đã bán N"; half the quota gone: "Đang bán chạy"; the last 20 %: "Chỉ còn N" (with a flame).
 */
export const flashBarLabel = (i: Pick<FlashBoardItem, 'sold' | 'quota' | 'soldPercent'>): string =>
  i.sold >= i.quota ? 'Đã bán hết'
    : i.sold === 0 ? 'Vừa mở bán'
      : i.soldPercent >= 80 ? `Chỉ còn ${i.quota - i.sold}`
        : i.soldPercent >= 50 ? 'Đang bán chạy'
          : `Đã bán ${i.sold}`;

const FlashItem = ({ i }: { i: FlashBoardItem }) => {
  const hot = i.sold < i.quota && i.soldPercent >= 80;
  const pct = Math.max(0, Math.min(100, i.soldPercent));
  return (
    <Link to={`/san-pham/${i.productId}`} className="flash-item" data-testid="flash-item">
      <span className={`flash-item-image ${i.campaignName ? 'has-campaign' : ''}`}>
        <img src={imageOrPlaceholder(i.imageUrl)} srcSet={imageSrcSet(i.imageUrl)} sizes="200px" alt={i.name} loading="lazy" width={200} height={200} onError={handleImgError} />
        <ShopBadge isMall={!!i.isMall} isPreferred={!!i.isPreferred} className="flash-item-flag" />
        {i.discountPercent > 0 && <span className="flash-item-badge">-{i.discountPercent}%</span>}
        {i.campaignName && <CampaignStrip name={i.campaignName} />}
      </span>
      <span className="flash-item-price price" aria-label={formatPrice(i.flashPrice)}>
        <span aria-hidden>{priceParts(i.flashPrice).amount}</span><span className="flash-item-currency" aria-hidden>{priceParts(i.flashPrice).currency}</span>
      </span>
      <span className="flash-item-bar" role="progressbar" aria-label={`Đã bán ${i.sold} trên ${i.quota} suất`}
        aria-valuenow={i.soldPercent} aria-valuemin={0} aria-valuemax={100}>
        <span className="flash-item-bar-fill" style={{ width: `${pct}%` }} />
        {/* The label twice: dark orange on the pale track, white clipped to the orange fill — each part reads 4.5:1 */}
        <span className="flash-item-bar-text">
          {hot && <Flame size={13} fill="currentColor" aria-hidden />}
          {flashBarLabel(i)}
        </span>
        <span className="flash-item-bar-text flash-item-bar-text--on-fill" aria-hidden style={{ clipPath: `inset(0 ${100 - pct}% 0 0)` }}>
          {hot && <Flame size={13} fill="currentColor" aria-hidden />}
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
// `compact` (campaign page block) is kept for callers; since G-VIS no variant shows the next slots any more
export const FlashSaleBoard = ({ board, full = false }: { board: FlashBoard; compact?: boolean; full?: boolean }) => {
  const queryClient = useQueryClient();
  const [skew] = useState(() => clockSkew(board.serverTime, Date.now()));
  if (!board.slot || board.items.length === 0) return null;
  const slot = board.slot;
  const items = full ? board.items : board.items.slice(0, 18);
  return (
    <section className="flash-sale" data-testid="flash-sale">
      <div className="flash-sale-head">
        <h2 className="flash-sale-title">
          <AlarmClock className="flash-sale-clock" size={26} strokeWidth={2.4} aria-hidden />
          <span className="flash-sale-wordmark">Flash Sale</span>
        </h2>
        <span className="flash-sale-when">
          {/* Running: the boxes alone, as on the reference (the words stay for screen readers) */}
          <span className={`flash-sale-when-label ${slot.running ? 'sh-visually-hidden' : ''}`}>{slot.running ? 'Kết thúc sau' : `Bắt đầu lúc ${formatClock(slot.startAt)} — còn`}</span>
          <Countdown endAt={slot.running ? slot.endAt : slot.startAt} skewMs={skew}
            onDone={() => void queryClient.invalidateQueries({ queryKey: ['flash-sale'] })} />
        </span>
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
