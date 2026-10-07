import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { ordersApi, walletApi, type OrderDetail, type OrderTab, type VoucherInfo, type WalletTab } from '../../api/commerce';
import { CART_KEY } from '../../context/CartContext';
import { marketingApi } from '../../api/marketing';
import { ShipmentTimeline } from '../TrackingPage';
import { ApiError } from '../../api/http';
import { formatCount, formatPrice } from '../../lib/money';
import { formatDate, formatDateTime } from '../../lib/datetime';
import { ContactShopButton } from '../../components/chat/Chat';
import { handleImgError, imageOrPlaceholder } from '../../lib/image';
import { usePageTitle } from '../../lib/pageTitle';
import QueryState from '../../components/QueryState';

const ORDER_TABS: { key: OrderTab; label: string }[] = [
  { key: 'All', label: 'Tất cả' },
  { key: 'AwaitingPayment', label: 'Chờ thanh toán' },
  { key: 'Processing', label: 'Vận chuyển' },
  { key: 'Shipping', label: 'Chờ giao hàng' },
  { key: 'Completed', label: 'Hoàn thành' },
  { key: 'Cancelled', label: 'Đã huỷ' },
  { key: 'Returns', label: 'Trả hàng/Hoàn tiền' },
];

/** /tai-khoan/don-mua */
export const OrdersPage = () => {
  usePageTitle('Đơn mua');
  const [tab, setTab] = useState<OrderTab>('All');
  const [q, setQ] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const orders = useQuery({
    queryKey: ['orders', tab, search, page],
    queryFn: () => ordersApi.list(tab, search, page),
    placeholderData: keepPreviousData,
  });
  const data = orders.data;
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <div className="account-card" data-testid="orders-page">
      <div className="account-tabs">
        {ORDER_TABS.map((t) => (
          <button key={t.key} className={`account-tab ${tab === t.key ? 'active' : ''}`} onClick={() => { setTab(t.key); setPage(1); }}>
            {t.label}
          </button>
        ))}
      </div>
      <form className="account-search" onSubmit={(e) => { e.preventDefault(); setSearch(q.trim()); setPage(1); }}>
        <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Tìm theo mã đơn, tên shop hoặc tên sản phẩm" aria-label="Tìm đơn hàng" />
      </form>
      <QueryState query={orders} isEmpty={(d) => d.items.length === 0}
        emptyText={<p className="account-empty" data-testid="orders-empty">Chưa có đơn hàng.</p>}>
        {(d) => d.items.map((o) => (
          <div key={o.id} className="order-card" data-testid="order-card">
            <div className="order-card-head">
              <Link to={`/shop/${o.shopSlug}`} className="order-card-shop">{o.shopName}</Link>
              <span className="order-card-status" data-testid="order-status">{o.statusLabel}</span>
            </div>
            {o.firstItem && (
              <Link to={`/tai-khoan/don-mua/${o.code}`} className="order-card-item">
                <img src={imageOrPlaceholder(o.firstItem.imageUrl)} alt="" onError={handleImgError} />
                <span>
                  <span className="order-card-name">{o.firstItem.name}</span>
                  {o.firstItem.variant && <span className="order-card-variant">Phân loại: {o.firstItem.variant}</span>}
                  <span>x{o.firstItem.quantity}{o.itemCount > o.firstItem.quantity ? ` · và ${o.itemCount - o.firstItem.quantity} sản phẩm khác` : ''}</span>
                </span>
              </Link>
            )}
            <div className="order-card-foot">
              <span>Mã đơn <Link to={`/tai-khoan/don-mua/${o.code}`} data-testid="order-code">{o.code}</Link> · {formatDateTime(o.createdAt)}</span>
              <span>Thành tiền: <strong>{formatPrice(o.grandTotal)}</strong></span>
            </div>
            <div className="order-card-actions">
              {o.status === 'PendingPayment' && <Link to={`/thanh-toan/ket-qua/${o.checkoutId}`} className="account-btn">Thanh toán ngay</Link>}
              <Link to={`/tai-khoan/don-mua/${o.code}`} className="account-btn-outline">Xem chi tiết</Link>
            </div>
          </div>
        ))}
      </QueryState>
      {pages > 1 && (
        <div className="account-pager">
          <button disabled={page <= 1} onClick={() => setPage(page - 1)}>‹</button>
          <span>{page}/{pages}</span>
          <button disabled={page >= pages} onClick={() => setPage(page + 1)}>›</button>
        </div>
      )}
    </div>
  );
};

const CANCEL_REASONS = [
  'Muốn thay đổi địa chỉ giao hàng',
  'Muốn thay đổi sản phẩm (kích cỡ, màu sắc, số lượng…)',
  'Tìm thấy giá rẻ hơn ở chỗ khác',
  'Đổi ý, không muốn mua nữa',
  'Thủ tục thanh toán rắc rối',
  'Lý do khác',
];

/** The buttons of an order, from what the server says is allowed now. */
const OrderActions = ({ order }: { order: OrderDetail }) => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [asking, setAsking] = useState<'cancel' | 'request' | null>(null);
  const [reason, setReason] = useState(CANCEL_REASONS[0]);
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);

  const run = async (action: () => Promise<{ message: string }>, then?: () => void) => {
    setBusy(true);
    setMessage('');
    try {
      setMessage((await action()).message);
      setAsking(null);
      void queryClient.invalidateQueries({ queryKey: ['order', order.code] });
      void queryClient.invalidateQueries({ queryKey: ['orders'] });
      then?.();
    } catch (e) {
      setMessage(e instanceof ApiError ? e.message : 'Không thực hiện được, vui lòng thử lại.');
    } finally {
      setBusy(false);
    }
  };

  const a = order.actions;
  return (
    <div className="order-actions" data-testid="order-actions">
      {message && <div className="account-message" role="status" data-testid="order-action-message">{message}</div>}
      {a.pay && <Link to={`/thanh-toan/ket-qua/${order.checkoutId}`} className="account-btn">Thanh toán ngay</Link>}
      {a.confirmReceived && (
        <button className="account-btn" disabled={busy} onClick={() => run(() => ordersApi.received(order.code))} data-testid="confirm-received">
          Đã nhận được hàng
        </button>
      )}
      {a.review && <Link to={`/tai-khoan/don-mua/${order.code}/danh-gia`} className="account-btn" data-testid="review-order">Đánh giá</Link>}
      {a.return && <Link to={`/tai-khoan/don-mua/${order.code}/tra-hang`} className="account-btn-outline" data-testid="return-order">Trả hàng/Hoàn tiền</Link>}
      {a.cancel && <button className="account-btn-outline" onClick={() => setAsking('cancel')} data-testid="cancel-order" data-confirm="dialog">Huỷ đơn hàng</button>}
      {a.requestCancel && <button className="account-btn-outline" onClick={() => setAsking('request')} data-testid="request-cancel">Yêu cầu huỷ</button>}
      {a.buyAgain && (
        <button className="account-btn-outline" disabled={busy} data-testid="buy-again"
          onClick={() => run(() => ordersApi.buyAgain(order.code), () => {
            void queryClient.invalidateQueries({ queryKey: CART_KEY });
            navigate('/gio-hang');
          })}>
          Mua lại
        </button>
      )}
      <ContactShopButton shopId={order.shopId} orderCode={order.code} className="account-btn-outline" />
      {asking && (
        <div className="order-cancel-box" data-testid="cancel-box">
          <strong>{asking === 'cancel' ? 'Chọn lý do huỷ' : 'Lý do yêu cầu huỷ (shop sẽ phản hồi trong 24 giờ)'}</strong>
          {asking === 'cancel' && (order.cancelsWith?.length ?? 0) > 0 && (
            <div className="order-detail-alert" data-testid="cancels-with">
              Các đơn này được thanh toán chung một lần, nên huỷ đơn này sẽ huỷ luôn: {order.cancelsWith?.join(', ')}
            </div>
          )}
          {CANCEL_REASONS.map((r) => (
            <label key={r} className="account-radio">
              <input type="radio" name="cancel-reason" checked={reason === r} onChange={() => setReason(r)} /> {r}
            </label>
          ))}
          <div className="order-cancel-actions">
            <button className="account-btn-outline" onClick={() => setAsking(null)}>Không phải bây giờ</button>
            <button className="account-btn" disabled={busy} data-testid="cancel-confirm"
              onClick={() => run(() => asking === 'cancel' ? ordersApi.cancel(order.code, reason) : ordersApi.requestCancel(order.code, reason))}>
              {asking === 'cancel' ? 'Huỷ đơn hàng' : 'Gửi yêu cầu'}
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

/** /tai-khoan/don-mua/:code */
export const OrderDetailPage = () => {
  const { code = '' } = useParams();
  const { data, error } = useQuery({ queryKey: ['order', code], queryFn: () => ordersApi.get(code), retry: false, staleTime: 0 });
  usePageTitle(`Đơn hàng ${code}`);
  if (error) return <div className="account-card"><p>{error instanceof ApiError ? error.message : 'Không tải được đơn hàng.'}</p></div>;
  if (!data) return <div className="page-loader"><div className="loading-spinner" /></div>;

  const rows: [string, number, boolean?][] = [
    ['Tổng tiền hàng', data.subtotal],
    ['Phí vận chuyển', data.shippingFee],
    ['Giảm giá phí vận chuyển', -data.shippingDiscount],
    ['Voucher của shop', -data.shopDiscount],
    ['ShopHub Voucher', -data.platformDiscount],
    ['ShopHub Xu', -data.coinUsed],
  ];
  return (
    <div className="account-card" data-testid="order-detail">
      <div className="order-detail-head">
        <Link to="/tai-khoan/don-mua">‹ Trở lại</Link>
        <span>Mã đơn hàng: <strong>{data.code}</strong> · <span data-testid="order-detail-status">{data.statusLabel}</span></span>
      </div>
      {data.status === 'PendingPayment' && data.paymentExpiresAt && (
        <div className="order-detail-alert">
          Vui lòng thanh toán trước {formatDateTime(data.paymentExpiresAt)}.{' '}
          <Link to={`/thanh-toan/ket-qua/${data.checkoutId}`}>Thanh toán ngay ›</Link>
        </div>
      )}
      {data.cancelReason && <div className="order-detail-alert">Lý do huỷ: {data.cancelReason}</div>}
      {data.cancelRequest && (
        <div className="order-detail-alert" data-testid="cancel-request-status">
          Yêu cầu huỷ ({data.cancelRequest.reason}):{' '}
          {data.cancelRequest.status === 'Pending' ? `đang chờ shop phản hồi (trước ${formatDateTime(data.cancelRequest.dueAt)})`
            : data.cancelRequest.status === 'Rejected' ? `shop đã từ chối — ${data.cancelRequest.rejectReason ?? ''}` : 'đã được chấp thuận'}
        </div>
      )}
      {data.status === 'Delivered' && data.autoCompleteAt && (
        <div className="order-detail-alert">Đơn sẽ tự hoàn thành lúc {formatDateTime(data.autoCompleteAt)} nếu bạn không phản hồi.</div>
      )}
      <OrderActions order={data} />

      {data.parcels ? data.parcels.map((p) => (
        <div key={p.no} className="order-detail-shipment" data-testid="order-parcel">
          <h3>
            Kiện {p.no}/{data.parcels!.length} · gửi từ {p.warehouseName}{p.provinceName && ` (${p.provinceName})`} ·{' '}
            {data.items.filter((i) => p.itemIds.includes(i.id)).map((i) => i.name).join(', ')}
          </h3>
          {p.shipment ? (
            <>
              <p>
                {p.shipment.carrierName ?? p.shipment.carrierCode} · Mã vận đơn{' '}
                <Link to={`/tra-cuu-van-don/${p.shipment.trackingNo}`} data-testid="tracking-no">{p.shipment.trackingNo}</Link>
                {' '}· Dự kiến giao: {formatDate(p.shipment.expectedDeliveryAt)}
              </p>
              <ShipmentTimeline events={p.shipment.events} />
            </>
          ) : <p>Shop đang chuẩn bị kiện này.</p>}
        </div>
      )) : data.shipment && (
        <div className="order-detail-shipment">
          <h3>
            Vận chuyển: {data.shipment.carrierName ?? data.shipment.carrierCode} · Mã vận đơn{' '}
            <Link to={`/tra-cuu-van-don/${data.shipment.trackingNo}`} data-testid="tracking-no">{data.shipment.trackingNo}</Link>
          </h3>
          <p>Dự kiến giao: {formatDate(data.shipment.expectedDeliveryAt)}</p>
          <ShipmentTimeline events={data.shipment.events} />
        </div>
      )}

      <ol className="order-timeline" data-testid="order-timeline">
        {data.history.map((h, i) => (
          <li key={i}>
            <strong>{h.toLabel}</strong> <span>{formatDateTime(h.occurredAt)}</span>
            {h.reason && <em> — {h.reason}</em>}
          </li>
        ))}
      </ol>

      <div className="order-detail-address">
        <h3>Địa chỉ nhận hàng</h3>
        <p>{data.address.receiverName} · {data.address.phone}</p>
        <p>{data.address.fullAddress}</p>
        <p>Vận chuyển: {data.carrierName ?? data.carrierCode} (dự kiến {data.expectedDeliveryDays === 0 ? 'trong ngày' : `${data.expectedDeliveryDays} ngày`})</p>
        {data.buyerNote && <p>Lời nhắn: {data.buyerNote}</p>}
      </div>

      <div className="order-detail-shop"><Link to={`/shop/${data.shopSlug}`}>{data.shopName}</Link></div>
      {data.items.map((i) => (
        <div key={i.id} className="order-card-item" data-testid="order-item">
          <img src={imageOrPlaceholder(i.imageUrl)} alt="" onError={handleImgError} />
          <span>
            <Link to={`/san-pham/${i.productId}`} className="order-card-name">{i.name}</Link>
            {i.variant && <span className="order-card-variant">Phân loại: {i.variant}</span>}
            <span>x{i.quantity}</span>
          </span>
          <span className="order-item-price">{formatPrice(i.lineTotal)}</span>
        </div>
      ))}

      <table className="order-money" data-testid="order-money">
        <tbody>
          {rows.filter(([, v]) => v !== 0).map(([label, v]) => (
            <tr key={label}><td>{label}</td><td>{v < 0 ? `−${formatPrice(-v)}` : formatPrice(v)}</td></tr>
          ))}
          <tr className="order-money-total"><td>Thành tiền</td><td data-testid="order-grand-total">{formatPrice(data.grandTotal)}</td></tr>
          <tr><td>Phương thức thanh toán</td><td>{data.paymentMethod === 'Cod' ? 'Thanh toán khi nhận hàng' : data.paymentMethod === 'Wallet' ? 'Ví ShopHub' : 'Thẻ / Ví điện tử'}</td></tr>
        </tbody>
      </table>
    </div>
  );
};

const describe = (v: VoucherInfo) => {
  const value = v.type === 'Amount' ? `Giảm ${formatPrice(v.discountValue)}`
    : v.type === 'FreeShipping' ? `Miễn phí vận chuyển tối đa ${formatPrice(v.maxDiscount ?? 0)}`
    : v.type === 'CoinCashback' ? `Hoàn ${v.discountPercentBp / 100}% xu, tối đa ${formatCount(v.maxDiscount ?? 0)} xu`
    : `Giảm ${v.discountPercentBp / 100}% tối đa ${formatPrice(v.maxDiscount ?? 0)}`;
  return `${value}${v.minOrder > 0 ? ` · Đơn từ ${formatPrice(v.minOrder)}` : ''}`;
};

const WALLET_TABS: { key: WalletTab; label: string }[] = [
  { key: 'Valid', label: 'Còn hạn' },
  { key: 'ExpiringSoon', label: 'Sắp hết hạn' },
  { key: 'Used', label: 'Đã dùng' },
  { key: 'Expired', label: 'Hết hạn' },
];

/** /tai-khoan/voucher — saved vouchers plus the platform's public ones to save. */
export const VouchersPage = () => {
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<WalletTab>('Valid');
  const [message, setMessage] = useState('');
  const mine = useQuery({ queryKey: ['wallet', tab], queryFn: () => walletApi.mine(tab) });
  const available = useQuery({ queryKey: ['wallet', 'available'], queryFn: () => walletApi.available() });

  const claim = async (id: string) => {
    try {
      setMessage((await walletApi.claim(id)).message);
      void queryClient.invalidateQueries({ queryKey: ['wallet'] });
    } catch (e) {
      setMessage(e instanceof ApiError ? e.message : 'Không lưu được voucher.');
    }
  };

  return (
    <div className="account-card" data-testid="vouchers-page">
      <h2 className="account-title">Ví Voucher</h2>
      {message && <div className="account-message" role="status">{message}</div>}
      <div className="account-tabs">
        {WALLET_TABS.map((t) => (
          <button key={t.key} className={`account-tab ${tab === t.key ? 'active' : ''}`} onClick={() => setTab(t.key)}>{t.label}</button>
        ))}
      </div>
      <QueryState query={mine} isEmpty={(d) => d.length === 0} emptyText={<p className="account-empty">Chưa có voucher nào.</p>}>
        {(list) => (
      <div className="voucher-list">
        {list.map((w) => (
          <div key={w.voucher.id} className="voucher-ticket" data-testid="my-voucher">
            <div className="voucher-ticket-side">{w.voucher.owner === 'Platform' ? 'ShopHub' : w.voucher.shopName ?? 'Shop'}</div>
            <div className="voucher-ticket-body">
              <strong>{describe(w.voucher)}</strong>
              <span>Mã {w.voucher.code} · HSD {formatDate(w.voucher.endAt)}</span>
              {w.problem && <span className="voucher-problem">{w.problem}</span>}
            </div>
          </div>
        ))}
      </div>
        )}
      </QueryState>

      <h3 className="account-subtitle">Voucher có thể lưu</h3>
      <div className="voucher-list">
        {(available.data ?? []).filter((w) => !w.claimed).map((w) => (
          <div key={w.voucher.id} className="voucher-ticket" data-testid="available-voucher">
            <div className="voucher-ticket-side">ShopHub</div>
            <div className="voucher-ticket-body">
              <strong>{describe(w.voucher)}</strong>
              <span>Mã {w.voucher.code} · HSD {formatDate(w.voucher.endAt)}</span>
            </div>
            <button className="account-btn" onClick={() => claim(w.voucher.id)} data-testid="claim-voucher">Lưu</button>
          </div>
        ))}
      </div>
    </div>
  );
};

const REASONS: Record<string, string> = {
  AdminGrant: 'Sàn tặng',
  CheckoutSpend: 'Dùng khi đặt hàng',
  CheckoutRefund: 'Hoàn xu do huỷ đơn',
  ReviewReward: 'Thưởng đánh giá',
  VoucherCashback: 'Hoàn xu từ voucher',
  Expired: 'Xu hết hạn',
  CheckIn: 'Điểm danh',
};

/** /tai-khoan/xu */
/** Điểm danh 7 ngày + hạng thành viên (spec VIII). */
const LoyaltyPanel = () => {
  const queryClient = useQueryClient();
  const [message, setMessage] = useState('');
  const checkIn = useQuery({ queryKey: ['check-in'], queryFn: marketingApi.checkIn });
  const membership = useQuery({ queryKey: ['membership'], queryFn: marketingApi.membership });
  const press = async () => {
    try {
      queryClient.setQueryData(['check-in'], await marketingApi.doCheckIn());
      setMessage('Điểm danh thành công!');
      void queryClient.invalidateQueries({ queryKey: ['coins'] });
    } catch (e) {
      setMessage(e instanceof ApiError ? e.message : 'Không điểm danh được.');
    }
  };
  const m = membership.data;
  return (
    <div className="loyalty" data-testid="loyalty">
      {m && (
        <div className={`member-card member-${m.tier.toLowerCase()}`} data-testid="member-tier">
          <strong>Hạng {m.tierLabel}</strong>
          <span>Chi tiêu {m.windowDays} ngày: {formatPrice(m.spend)}</span>
          {m.nextTierSpend !== null && <small>Còn {formatPrice(Math.max(0, m.nextTierSpend - m.spend))} để lên hạng tiếp theo</small>}
        </div>
      )}
      {checkIn.data && (
        <div className="checkin">
          <div className="checkin-days">
            {checkIn.data.days.map((d) => (
              <span key={d.day} className={`checkin-day ${d.done ? 'done' : ''} ${d.today ? 'today' : ''}`}>
                <b>+{formatCount(d.coins)}</b>
                <small>Ngày {d.day}</small>
              </span>
            ))}
          </div>
          <button className="account-btn" disabled={checkIn.data.doneToday} onClick={press} data-testid="check-in">
            {checkIn.data.doneToday ? 'Đã điểm danh hôm nay' : 'Điểm danh nhận xu'}
          </button>
          {message && <small role="status">{message}</small>}
        </div>
      )}
    </div>
  );
};

export const CoinsPage = () => {
  const [page, setPage] = useState(1);
  const coins = useQuery({ queryKey: ['coins', page], queryFn: () => walletApi.coins(page), placeholderData: keepPreviousData });
  return (
    <QueryState query={coins}>
      {(data) => {
        const pages = Math.max(1, Math.ceil(data.history.totalCount / data.history.pageSize));
        return (
    <div className="account-card" data-testid="coins-page">
      <h2 className="account-title">ShopHub Xu</h2>
      <LoyaltyPanel />
      <div className="coins-balance">
        <span className="coins-amount" data-testid="coin-balance">{formatCount(data.balance)}</span> xu đang có
        {data.expiringSoon > 0 && <span className="coins-expiring"> · {formatCount(data.expiringSoon)} xu sẽ hết hạn trong 30 ngày</span>}
      </div>
      <table className="order-money">
        <tbody>
          {data.history.items.map((h) => (
            <tr key={h.id}>
              <td>{REASONS[h.reason] ?? h.reason}{h.note ? ` — ${h.note}` : ''}<br /><small>{formatDateTime(h.createdAt)}</small></td>
              <td className={h.delta > 0 ? 'coins-plus' : 'coins-minus'}>{h.delta > 0 ? '+' : ''}{formatCount(h.delta)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {pages > 1 && (
        <div className="account-pager">
          <button disabled={page <= 1} onClick={() => setPage(page - 1)}>‹</button>
          <span>{page}/{pages}</span>
          <button disabled={page >= pages} onClick={() => setPage(page + 1)}>›</button>
        </div>
      )}
    </div>
        );
      }}
    </QueryState>
  );
};
