import { useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { trackingApi, type ShipmentEvent } from '../api/commerce';
import { formatDate, formatDateTime } from '../lib/datetime';
import QueryState from '../components/QueryState';
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

/** /tra-cuu-van-don[/:trackingNo] — public, shows only the parcel's journey (no names, phones or addresses). */
const TrackingPage = () => {
  const { trackingNo = '' } = useParams();
  const navigate = useNavigate();
  const [input, setInput] = useState(trackingNo);
  const tracking = useQuery({
    queryKey: ['tracking', trackingNo],
    queryFn: () => trackingApi.get(trackingNo),
    enabled: trackingNo.length > 0,
    retry: false,
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (input.trim()) navigate(`/tra-cuu-van-don/${encodeURIComponent(input.trim().toUpperCase())}`);
  };

  return (
    <div className="container payment-result tracking-page">
      <h1>Tra cứu vận đơn</h1>
      <form className="tracking-form" onSubmit={submit}>
        <input value={input} onChange={(e) => setInput(e.target.value)} placeholder="Nhập mã vận đơn, ví dụ SIM123456789VN" aria-label="Mã vận đơn" />
        <button type="submit" className="payment-result-btn">Tra cứu</button>
      </form>
      {trackingNo.length > 0 && (
      <QueryState query={tracking} loading={<p>Đang tra cứu…</p>}>
        {(data) => (
        <div className="tracking-result" data-testid="tracking-result">
          <p>
            <strong>{data.trackingNo}</strong> · {data.carrierName} · <span data-testid="tracking-status">{data.statusLabel}</span>
          </p>
          <p>Dự kiến giao: {formatDate(data.expectedDeliveryAt)}</p>
          <ShipmentTimeline events={data.events} />
        </div>
        )}
      </QueryState>
      )}
    </div>
  );
};

export default TrackingPage;
