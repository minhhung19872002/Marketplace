import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { checkoutApi, gatewayApi } from '../api/commerce';
import { ApiError } from '../api/http';
import { formatPrice } from '../lib/money';
import { formatDateTime } from '../lib/datetime';
import { goTo } from '../lib/navigation';
import './PaymentPages.css';

const useSecondsLeft = (until: string | null | undefined) => {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(t);
  }, []);
  return until ? Math.max(0, Math.floor((Date.parse(until) - now) / 1000)) : 0;
};

const mmss = (s: number) => `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;

/**
 * /cong-thanh-toan/:paymentId — the SimulatedGateway's own page (stands in for VNPay / MoMo). Its buttons tell the
 * gateway what happened; the gateway then notifies ShopHub by a signed webhook. This page never marks anything paid.
 */
export const SimulatedGatewayPage = () => {
  const { paymentId = '' } = useParams();
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const { data, isLoading } = useQuery({ queryKey: ['sim-payment', paymentId], queryFn: () => gatewayApi.view(paymentId), retry: false, staleTime: 0 });
  const left = useSecondsLeft(data?.expiresAt);

  if (isLoading) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (!data) return <div className="container gateway-page"><p>Không tìm thấy giao dịch.</p></div>;
  const result = data.returnPath;

  const press = async (outcome: 'success' | 'fail') => {
    setBusy(true);
    setError('');
    try {
      await gatewayApi.complete(paymentId, outcome);
      navigate(result);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Cổng thanh toán không phản hồi.');
      setBusy(false);
    }
  };

  return (
    <div className="gateway-page">
      <div className="gateway-card" data-testid="gateway">
        <div className="gateway-brand">SimPay · Cổng thanh toán giả lập</div>
        <p className="gateway-note">Trang thử nghiệm thay cho cổng thật (VNPay / MoMo). Không có tiền thật được chuyển.</p>
        <div className="gateway-amount" data-testid="gateway-amount">{formatPrice(data.amount)}</div>
        <div className="gateway-desc">{data.description}</div>
        {data.way && <div className="gateway-desc" data-testid="gateway-way">Hình thức: {data.way}</div>}
        <div className="gateway-timer">Giao dịch hết hạn sau <strong>{mmss(left)}</strong></div>
        {error && <div className="gateway-error" role="alert">{error}</div>}
        {data.status === 'Initiated' && left > 0 ? (
          <div className="gateway-actions">
            <button className="gateway-ok" disabled={busy} onClick={() => press('success')} data-testid="gateway-success">Thành công</button>
            <button className="gateway-fail" disabled={busy} onClick={() => press('fail')} data-testid="gateway-fail">Thất bại</button>
            <button className="gateway-leave" disabled={busy} onClick={() => navigate(result)} data-testid="gateway-abandon">Bỏ đi</button>
          </div>
        ) : (
          <Link to={result} className="gateway-leave">Quay về ShopHub</Link>
        )}
      </div>
    </div>
  );
};

/** /thanh-toan/ket-qua/:checkoutId — shows what the server knows (the gateway's webhook decides, not this page). */
export const PaymentResultPage = () => {
  const { checkoutId = '' } = useParams();
  const navigate = useNavigate();
  const [error, setError] = useState('');
  const { data, refetch } = useQuery({
    queryKey: ['checkout', checkoutId],
    queryFn: () => checkoutApi.get(checkoutId),
    staleTime: 0,
    // Poll while waiting for the gateway's notification
    refetchInterval: (q) => (q.state.data?.status === 'AwaitingPayment' && q.state.data.payment?.status === 'Initiated' ? 2000 : false),
  });
  const left = useSecondsLeft(data?.paymentExpiresAt);

  if (!data) return <div className="page-loader"><div className="loading-spinner" /></div>;

  const retry = async () => {
    setError('');
    try {
      const r = await checkoutApi.retryPayment(checkoutId);
      if (r.payment?.redirectUrl) goTo(navigate, r.payment.redirectUrl);
      else void refetch();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Không tạo được giao dịch mới.');
    }
  };

  const paid = data.status === 'Placed';
  const expired = data.status === 'Expired';
  return (
    <div className="container payment-result" data-testid="payment-result">
      {paid && (
        <>
          <div className="payment-result-icon ok">✓</div>
          <h1 data-testid="payment-state">Thanh toán thành công</h1>
        </>
      )}
      {expired && (
        <>
          <div className="payment-result-icon fail">✕</div>
          <h1 data-testid="payment-state">Đơn hàng đã huỷ do quá hạn thanh toán</h1>
          <p>Hàng đã giữ, voucher và xu đã được trả lại.</p>
        </>
      )}
      {!paid && !expired && (
        <>
          <div className="payment-result-icon wait">…</div>
          <h1 data-testid="payment-state">
            {data.payment?.status === 'Failed' ? 'Thanh toán không thành công' : 'Đang chờ thanh toán'}
          </h1>
          <p>
            Vui lòng thanh toán trước <strong>{data.paymentExpiresAt ? formatDateTime(data.paymentExpiresAt) : ''}</strong> (còn {mmss(left)}),
            sau thời gian này đơn hàng sẽ tự huỷ.
          </p>
          {error && <div className="gateway-error" role="alert">{error}</div>}
          <button className="payment-retry" onClick={retry} data-testid="retry-payment">Thanh toán lại</button>
        </>
      )}
      <ul className="payment-result-orders">
        {data.orders.map((o) => (
          <li key={o.id} data-testid="result-order">
            <span>Đơn <strong>{o.code}</strong> · {o.shopName}</span>
            <span>{formatPrice(o.grandTotal)}</span>
          </li>
        ))}
      </ul>
      <div className="payment-result-actions">
        <Link to="/tai-khoan/don-mua" className="payment-result-btn">Xem đơn hàng</Link>
        <Link to="/" className="payment-result-btn ghost">Tiếp tục mua sắm</Link>
      </div>
    </div>
  );
};

/** /dat-hang-thanh-cong?checkout= — COD orders: real order codes, one per shop. */
export const OrderSuccessPage = () => {
  const [params] = useSearchParams();
  const checkoutId = params.get('checkout') ?? '';
  const { data, isError } = useQuery({ queryKey: ['checkout', checkoutId], queryFn: () => checkoutApi.get(checkoutId), enabled: !!checkoutId, staleTime: 0 });

  if (!checkoutId || isError) {
    return (
      <div className="container payment-result">
        <p>Không tìm thấy đơn hàng.</p>
        <Link to="/" className="payment-result-btn">Về trang chủ</Link>
      </div>
    );
  }
  if (!data) return <div className="page-loader"><div className="loading-spinner" /></div>;

  return (
    <div className="container payment-result" data-testid="order-success">
      <div className="payment-result-icon ok">✓</div>
      <h1>Đặt hàng thành công</h1>
      <p>
        {data.orders.length} đơn hàng · Tổng thanh toán <strong data-testid="success-total">{formatPrice(data.grandTotal)}</strong>
        {data.paymentMethod === 'Cod' && ' (thanh toán khi nhận hàng)'}
      </p>
      <ul className="payment-result-orders">
        {data.orders.map((o) => (
          <li key={o.id} data-testid="result-order">
            <span>
              Đơn <Link to={`/tai-khoan/don-mua/${o.code}`}><strong>{o.code}</strong></Link> · {o.shopName}
            </span>
            <span data-testid="result-order-total">{formatPrice(o.grandTotal)}</span>
          </li>
        ))}
      </ul>
      <div className="payment-result-actions">
        <Link to="/tai-khoan/don-mua" className="payment-result-btn" data-testid="view-orders">Xem đơn hàng</Link>
        <Link to="/" className="payment-result-btn ghost">Tiếp tục mua sắm</Link>
      </div>
    </div>
  );
};
