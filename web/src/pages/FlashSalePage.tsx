import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { AlarmClock } from 'lucide-react';
import { clockSkew, marketingApi, type FlashBoard } from '../api/marketing';
import { FlashSaleBoard } from '../components/FlashSaleBlock';
import Countdown from '../components/Countdown';
import QueryState from '../components/QueryState';
import Carousel from '../components/ui/Carousel';
import { formatClock, formatDate } from '../lib/datetime';
import { usePageTitle } from '../lib/pageTitle';
import './FlashSalePage.css';

/** The countdown of the band; remounted per board so the clock skew is measured again for each one. */
const FlashWhen = ({ board }: { board: FlashBoard }) => {
  const queryClient = useQueryClient();
  const slot = board.slot!;
  // Measured once per board, like the home block (spec 3.10 — never the visitor's clock)
  const [skew] = useState(() => clockSkew(board.serverTime, Date.now()));
  return (
    <p className="flash-page-when">
      <span>{slot.running ? 'Kết thúc sau' : `Bắt đầu lúc ${formatClock(slot.startAt)} — còn`}</span>
      <Countdown endAt={slot.running ? slot.endAt : slot.startAt} skewMs={skew}
        onDone={() => void queryClient.invalidateQueries({ queryKey: ['flash-sale'] })} />
    </p>
  );
};

/** The bright band at the top (G5-B): the wordmark and one countdown for the chosen slot, on the server's clock.
 *  The band itself stays mounted while the data loads or the slot changes — only the countdown is keyed (L188). */
const FlashHero = ({ board }: { board: FlashBoard | undefined }) => (
  <div className="flash-page-hero">
    <div className="container flash-page-hero-inner">
      <h1 className="flash-page-heading">
        <AlarmClock className="flash-page-clock" size={34} strokeWidth={2.4} aria-hidden />
        <span className="flash-page-wordmark">Flash Sale</span>
      </h1>
      {board?.slot && <FlashWhen key={`${board.slot.id}-${board.serverTime}`} board={board} />}
    </div>
  </div>
);

/** Trang Flash Sale (II.1, E8): the running slot and the next ones, every item of the chosen slot. */
const FlashSalePage = () => {
  usePageTitle('Flash Sale');
  const [slotId, setSlotId] = useState<string | undefined>(undefined);
  const current = useQuery({ queryKey: ['flash-sale'], queryFn: () => marketingApi.flashSale(), staleTime: 30_000, refetchInterval: 60_000 });
  const chosen = useQuery({
    queryKey: ['flash-sale', slotId],
    queryFn: () => marketingApi.flashSale(slotId),
    enabled: !!slotId,
    staleTime: 30_000,
  });
  const shown = slotId ? chosen : current;
  const slots = current.data ? [...(current.data.slot ? [current.data.slot] : []), ...current.data.upcoming] : [];

  return (
    <div className="flash-page">
      <FlashHero board={shown.data} />
      <div className="container flash-page-body">
        {slots.length > 0 && (
          // The slot bar scrolls inside itself with arrows, no browser scrollbar (G4-A3)
          <Carousel className="flash-page-slots" trackRole="tablist" label="Khung giờ Flash Sale">
            {slots.map((s) => {
              const active = (slotId ?? current.data?.slot?.id) === s.id;
              return (
                <button key={s.id} type="button" role="tab" aria-selected={active} className={`flash-page-slot ${active ? 'active' : ''}`}
                  onClick={() => setSlotId(s.id === current.data?.slot?.id ? undefined : s.id)} data-testid="flash-slot">
                  <strong>{formatClock(s.startAt)}</strong>
                  <span className="flash-page-slot-state">{s.running ? 'Đang diễn ra' : 'Sắp diễn ra'}</span>
                  {!s.running && <span className="flash-page-slot-date">{formatDate(s.startAt)}</span>}
                </button>
              );
            })}
          </Carousel>
        )}
        <QueryState query={shown} loading={<p className="flash-page-empty">Đang tải…</p>}
          isEmpty={(b) => !b.slot || b.items.length === 0}
          emptyText={<p className="flash-page-empty" data-testid="flash-empty">Chưa có khung Flash Sale nào đang mở. Quay lại sau nhé!</p>}>
          {/* The board's own head (wordmark + countdown) is folded away by the page CSS: the band above carries both */}
          {(b) => <FlashSaleBoard key={`${b.slot!.id}-${b.serverTime}`} board={b} full />}
        </QueryState>
      </div>
    </div>
  );
};

export default FlashSalePage;
