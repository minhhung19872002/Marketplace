import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { marketingApi } from '../api/marketing';
import { FlashSaleBoard } from '../components/FlashSaleBlock';
import { formatClock, formatDate } from '../lib/datetime';
import './FlashSalePage.css';

/** Trang Flash Sale (II.1, E8): the running slot and the next ones, every item of the chosen slot. */
const FlashSalePage = () => {
  const [slotId, setSlotId] = useState<string | undefined>(undefined);
  const current = useQuery({ queryKey: ['flash-sale'], queryFn: () => marketingApi.flashSale(), staleTime: 30_000, refetchInterval: 60_000 });
  const chosen = useQuery({
    queryKey: ['flash-sale', slotId],
    queryFn: () => marketingApi.flashSale(slotId),
    enabled: !!slotId,
    staleTime: 30_000,
  });
  const board = slotId ? chosen.data : current.data;
  const slots = current.data ? [...(current.data.slot ? [current.data.slot] : []), ...current.data.upcoming] : [];

  return (
    <div className="container flash-page">
      <h1 className="flash-page-title">Flash Sale</h1>
      {slots.length > 0 && (
        <div className="flash-page-slots" role="tablist" aria-label="Khung giờ Flash Sale">
          {slots.map((s) => {
            const active = (slotId ?? current.data?.slot?.id) === s.id;
            return (
              <button key={s.id} type="button" role="tab" aria-selected={active} className={`flash-page-slot ${active ? 'active' : ''}`}
                onClick={() => setSlotId(s.id === current.data?.slot?.id ? undefined : s.id)} data-testid="flash-slot">
                <strong>{formatClock(s.startAt)}</strong>
                <span>{s.running ? 'Đang diễn ra' : `Sắp diễn ra · ${formatDate(s.startAt)}`}</span>
              </button>
            );
          })}
        </div>
      )}
      {current.isPending || (slotId && chosen.isPending) ? (
        <p className="flash-page-empty">Đang tải…</p>
      ) : board && board.slot && board.items.length > 0 ? (
        <FlashSaleBoard key={`${board.slot.id}-${board.serverTime}`} board={board} full />
      ) : (
        <p className="flash-page-empty" data-testid="flash-empty">Chưa có khung Flash Sale nào đang mở. Quay lại sau nhé!</p>
      )}
    </div>
  );
};

export default FlashSalePage;
