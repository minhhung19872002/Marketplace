import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { trackingApi, type ShipmentEvent } from '../api/commerce';
import { formatDate, formatDateTime } from '../lib/datetime';
import { usePageTitle } from '../lib/pageTitle';
import QueryState from '../components/QueryState';
import { BandSearch, PageBand } from '../components/ui/PageBand';
import './PaymentPages.css';

/** Timeline of carrier events, newest first (shared by the order page and the public tracking page). */
export const ShipmentTimeline = ({ events }: { events: ShipmentEvent[] }) => (
  <ol className="shipment-timeline" data-testid="shipment-timeline">
    {[...events].reverse().map((e, i) => (
      <li key={`${e.occurredAt}-${i}`} className={i === 0 ? 'current' : ''}>
        <span className="shipment-time">{formatDateTime(e.occurredAt)}</span>
        <span>
          <strong>{e.label}</strong> — {e.description}
          {e.location && <em> · {e.location}</em>}
        </span>
      </li>
    ))}
  </ol>
);

/**
 * /tra-cuu-van-don[/:trackingNo] — public, shows only the parcel's journey (no names, phones or addresses). The bright
 * band with the search box, then the parcel as a card: its facts in one row and the timeline of the order page (G5-B).
 */
const TrackingPage = () => {
  const { trackingNo = '' } = useParams();
  const navigate = useNavigate();
  const [input, setInput] = useState(trackingNo);
  usePageTitle(trackingNo ? `Vận đơn ${trackingNo}` : 'Tra cứu vận đơn');
  const tracking = useQuery({
    queryKey: ['tracking', trackingNo],
    queryFn: () => trackingApi.get(trackingNo),
    enabled: trackingNo.length > 0,
    retry: false,
  });

  const submit = () => {
    if (input.trim()) navigate(`/tra-cuu-van-don/${encodeURIComponent(input.trim().toUpperCase())}`);
  };

  return (
    <>
      <PageBand className="tracking-band">
        <h1>Tra cứu vận đơn</h1>
        <BandSearch value={input} onChange={setInput} onSubmit={submit} placeholder="Nhập mã vận đơn, ví dụ SIM123456789VN" label="Mã vận đơn"
          button="Tra cứu" />
      </PageBand>
      <div className="container tracking-page">
        {trackingNo.length === 0 && (
          <p className="tracking-hint">Mã vận đơn có trên phiếu giao hàng và trong chi tiết đơn ở mục Đơn mua.</p>
        )}
        {trackingNo.length > 0 && (
          <QueryState query={tracking} loading={<p className="tracking-hint">Đang tra cứu…</p>}>
            {(data) => (
              <section className="tracking-card" data-testid="tracking-result">
                <div className="tracking-facts">
                  <div><span>Mã vận đơn</span><strong>{data.trackingNo}</strong></div>
                  <div><span>Đơn vị vận chuyển</span><strong>{data.carrierName}</strong></div>
                  <div><span>Dự kiến giao</span><strong>{formatDate(data.expectedDeliveryAt)}</strong></div>
                  <span className="tracking-status" data-testid="tracking-status">{data.statusLabel}</span>
                </div>
                <h2 className="tracking-journey">Hành trình đơn hàng</h2>
                <ShipmentTimeline events={data.events} />
              </section>
            )}
          </QueryState>
        )}
      </div>
    </>
  );
};

export default TrackingPage;
