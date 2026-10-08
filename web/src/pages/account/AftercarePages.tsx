import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  REVIEW_TAGS,
  RETURN_REASONS,
  reviewsApi,
  returnsApi,
  uploadMedia,
  type MediaPurpose,
  type ReturnReason,
  type ReturnType,
  type ReviewableItem,
} from '../../api/aftercare';
import { ApiError } from '../../api/http';
import { formatCount, formatPrice } from '../../lib/money';
import { formatDateTime } from '../../lib/datetime';
import { ContactShopButton } from '../../components/chat/Chat';
import { handleImgError, imageOrPlaceholder } from '../../lib/image';
import { ConfirmButton } from '../../components/ConfirmDialog';
import QueryState from '../../components/QueryState';
import { Star, Play } from 'lucide-react';
import { Stars as StarsView } from '../../components/ui';

/** Pick photos / a short video; each file is uploaded right away and kept as an asset id. */
const MediaPicker = ({ purpose, value, onChange, max, testId }: {
  purpose: MediaPurpose;
  value: { id: string; preview: string }[];
  onChange: (next: { id: string; preview: string }[]) => void;
  max: number;
  testId: string;
}) => {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const pick = async (files: FileList | null) => {
    if (!files) return;
    setBusy(true);
    setError('');
    const next = [...value];
    try {
      for (const file of Array.from(files).slice(0, max - value.length)) {
        const asset = await uploadMedia(purpose, file);
        next.push({ id: asset.id, preview: asset.url ?? '' });
      }
      onChange(next);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Tải tệp thất bại.');
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="media-picker" data-testid={testId}>
      {value.map((m) => (
        <span key={m.id} className="media-picker-item">
          <img src={m.preview} alt="" onError={handleImgError} />
          <button type="button" aria-label="Bỏ ảnh" onClick={() => onChange(value.filter((x) => x.id !== m.id))}>×</button>
        </span>
      ))}
      {value.length < max && (
        <label className="media-picker-add">
          {busy ? 'Đang tải…' : '+ Ảnh / Video'}
          <input type="file" accept="image/*,video/mp4" multiple hidden onChange={(e) => pick(e.target.files)} data-testid={`${testId}-input`} />
        </label>
      )}
      {error && <span className="account-error">{error}</span>}
    </div>
  );
};

const Stars = ({ value, onChange }: { value: number; onChange: (n: number) => void }) => (
  <span className="review-stars" role="radiogroup" aria-label="Số sao">
    {[1, 2, 3, 4, 5].map((n) => (
      <button key={n} type="button" className={n <= value ? 'on' : ''} onClick={() => onChange(n)} aria-label={`${n} sao`} data-testid={`star-${n}`}><Star size={28} fill="currentColor" strokeWidth={0} aria-hidden /></button>
    ))}
  </span>
);

const ReviewForm = ({ code, item, onDone }: { code: string; item: ReviewableItem; onDone: () => void }) => {
  const existing = item.review;
  const [rating, setRating] = useState(existing?.rating ?? 5);
  const [content, setContent] = useState(existing?.content ?? '');
  const [tags, setTags] = useState<string[]>(existing?.tags ?? []);
  const [anonymous, setAnonymous] = useState(false);
  const [media, setMedia] = useState<{ id: string; preview: string }[]>([]);
  const [message, setMessage] = useState('');
  const submit = async () => {
    setMessage('');
    const input = { rating, content, tags, anonymous, mediaAssetIds: media.map((m) => m.id) };
    try {
      const r = existing ? await reviewsApi.edit(existing.id, input) : await reviewsApi.write(code, item.orderItemId, input);
      setMessage(r.message);
      onDone();
    } catch (e) {
      setMessage(e instanceof ApiError ? e.message : 'Không gửi được đánh giá.');
    }
  };
  return (
    <div className="review-form" data-testid="review-form">
      <Stars value={rating} onChange={setRating} />
      <div className="review-tags">
        {REVIEW_TAGS.map((t) => (
          <button key={t} type="button" className={tags.includes(t) ? 'active' : ''}
            onClick={() => setTags(tags.includes(t) ? tags.filter((x) => x !== t) : [...tags, t])}>{t}</button>
        ))}
      </div>
      <textarea rows={4} maxLength={1000} value={content} onChange={(e) => setContent(e.target.value)}
        placeholder="Hãy chia sẻ cảm nhận của bạn (từ 50 ký tự kèm ảnh để nhận ShopHub Xu)" aria-label="Nội dung đánh giá" />
      <MediaPicker purpose="review" value={media} onChange={setMedia} max={7} testId="review-media" />
      <label className="account-radio">
        <input type="checkbox" checked={anonymous} onChange={(e) => setAnonymous(e.target.checked)} /> Đánh giá ẩn danh
      </label>
      {message && <div className="account-message" role="status">{message}</div>}
      <button className="account-btn" onClick={submit} data-testid="review-submit">{existing ? 'Lưu thay đổi' : 'Gửi đánh giá'}</button>
    </div>
  );
};

/** /tai-khoan/don-mua/:code/danh-gia */
export const OrderReviewPage = () => {
  const { code = '' } = useParams();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<string | null>(null);
  const reviewable = useQuery({ queryKey: ['reviewable', code], queryFn: () => reviewsApi.reviewable(code), staleTime: 0 });
  const data = reviewable.data;
  if (!data) return <QueryState query={reviewable}>{() => null}</QueryState>;
  const refresh = () => {
    setEditing(null);
    void queryClient.invalidateQueries({ queryKey: ['reviewable', code] });
    void queryClient.invalidateQueries({ queryKey: ['order', code] });
  };
  return (
    <div className="account-card" data-testid="order-review-page">
      <div className="order-detail-head">
        <Link to={`/tai-khoan/don-mua/${code}`}>‹ Trở lại đơn {code}</Link>
        <span>Đánh giá sản phẩm</span>
      </div>
      {data.map((item) => (
        <div key={item.orderItemId} className="review-item" data-testid="review-item">
          <div className="order-card-item">
            <img src={imageOrPlaceholder(item.imageUrl)} alt="" onError={handleImgError} />
            <span>
              <span className="order-card-name">{item.name}</span>
              {item.variant && <span className="order-card-variant">Phân loại: {item.variant}</span>}
            </span>
          </div>
          {item.review && editing !== item.orderItemId ? (
            <div className="review-done" data-testid="review-done">
              <StarsView value={item.review.rating} size={14} />
              <p>{item.review.content}</p>
              {item.canEdit && <button className="account-btn-outline" onClick={() => setEditing(item.orderItemId)}>Sửa đánh giá (1 lần)</button>}
            </div>
          ) : item.canReview || editing === item.orderItemId ? (
            <ReviewForm code={code} item={item} onDone={refresh} />
          ) : (
            <p className="account-empty">Đã hết hạn đánh giá.</p>
          )}
        </div>
      ))}
    </div>
  );
};

/** /tai-khoan/don-mua/:code/tra-hang */
export const ReturnFormPage = () => {
  const { code = '' } = useParams();
  const navigate = useNavigate();
  const returnable = useQuery({ queryKey: ['returnable', code], queryFn: () => returnsApi.returnable(code), staleTime: 0 });
  const data = returnable.data;
  const [type, setType] = useState<ReturnType>('RefundOnly');
  const [reason, setReason] = useState<ReturnReason>('Damaged');
  const [description, setDescription] = useState('');
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  const [evidence, setEvidence] = useState<{ id: string; preview: string }[]>([]);
  const [error, setError] = useState('');
  if (!data) return <QueryState query={returnable}>{() => null}</QueryState>;
  if (!data.canReturn) return <div className="account-card"><p>{data.reason}</p><Link to={`/tai-khoan/don-mua/${code}`}>‹ Trở lại</Link></div>;

  const lines = Object.entries(quantities).filter(([, q]) => q > 0).map(([orderItemId, quantity]) => ({ orderItemId, quantity }));
  const estimate = data.lines.reduce((s, l) => s + l.unitRefundEstimate * (quantities[l.orderItemId] ?? 0), 0);
  const submit = async () => {
    setError('');
    try {
      const r = await returnsApi.create(code, { type, reason, description, lines, evidenceAssetIds: evidence.map((e) => e.id) });
      navigate(`/tai-khoan/tra-hang/${r.data.code}`);
    } catch (e) {
      setError(e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : 'Không gửi được yêu cầu.');
    }
  };
  return (
    <div className="account-card" data-testid="return-form">
      <div className="order-detail-head">
        <Link to={`/tai-khoan/don-mua/${code}`}>‹ Trở lại đơn {code}</Link>
        <span>Yêu cầu trả hàng / hoàn tiền {data.deadline && `· hạn ${formatDateTime(data.deadline)}`}</span>
      </div>
      <h3 className="account-subtitle">Chọn sản phẩm và số lượng</h3>
      {data.lines.map((l) => (
        <div key={l.orderItemId} className="order-card-item" data-testid="return-line">
          <img src={imageOrPlaceholder(l.imageUrl)} alt="" onError={handleImgError} />
          <span>
            <span className="order-card-name">{l.name}</span>
            {l.variant && <span className="order-card-variant">Phân loại: {l.variant}</span>}
            <span>Có thể trả: {l.returnable} / {l.quantity}</span>
          </span>
          <input type="number" min={0} max={l.returnable} value={quantities[l.orderItemId] ?? 0} disabled={l.returnable === 0}
            onChange={(e) => setQuantities({ ...quantities, [l.orderItemId]: Math.max(0, Math.min(l.returnable, Number(e.target.value) || 0)) })}
            aria-label={`Số lượng trả ${l.name}`} data-testid="return-qty" />
        </div>
      ))}
      <div className="account-form">
        <label className="account-label">Hình thức</label>
        <div className="account-radios">
          <label className="account-radio"><input type="radio" checked={type === 'RefundOnly'} onChange={() => setType('RefundOnly')} /> Chỉ hoàn tiền</label>
          <label className="account-radio"><input type="radio" checked={type === 'ReturnAndRefund'} onChange={() => setType('ReturnAndRefund')} data-testid="type-return" /> Trả hàng & hoàn tiền</label>
        </div>
        <label className="account-label" htmlFor="return-reason">Lý do</label>
        <select id="return-reason" className="account-input" value={reason} onChange={(e) => setReason(e.target.value as ReturnReason)} data-testid="return-reason">
          {RETURN_REASONS.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
        </select>
        <label className="account-label" htmlFor="return-desc">Mô tả</label>
        <textarea id="return-desc" className="account-input" rows={3} value={description} onChange={(e) => setDescription(e.target.value)}
          placeholder="Mô tả vấn đề (ít nhất 10 ký tự)" data-testid="return-description" />
        <label className="account-label">Bằng chứng (ảnh / video)</label>
        <MediaPicker purpose="evidence" value={evidence} onChange={setEvidence} max={10} testId="return-evidence" />
      </div>
      <p>Số tiền hoàn dự kiến: <strong data-testid="return-estimate">{formatPrice(estimate)}</strong> (đúng phần bạn đã trả sau giảm giá)</p>
      {error && <div className="account-error" role="alert">{error}</div>}
      <button className="account-btn" disabled={lines.length === 0} onClick={submit} data-testid="return-submit">Gửi yêu cầu</button>
    </div>
  );
};

/** /tai-khoan/tra-hang */
export const ReturnsPage = () => {
  const [page, setPage] = useState(1);
  const returns = useQuery({ queryKey: ['returns', page], queryFn: () => returnsApi.mine(page), placeholderData: keepPreviousData });
  const data = returns.data;
  return (
    <div className="account-card" data-testid="returns-page">
      <h2 className="account-title">Trả hàng / Hoàn tiền</h2>
      <QueryState query={returns} isEmpty={(d) => d.items.length === 0} emptyText={<p className="account-empty">Bạn chưa có yêu cầu trả hàng nào.</p>}>
        {(d) => d.items.map((r) => (
        <Link key={r.id} to={`/tai-khoan/tra-hang/${r.code}`} className="order-card return-card" data-testid="return-card">
          <div className="order-card-head">
            <span>{r.code} · đơn {r.orderCode} · {r.shopName}</span>
            <span className="order-card-status">{r.statusLabel}</span>
          </div>
          <div className="order-card-foot">
            <span>{r.items.map((i) => `${i.name} ×${i.quantity}`).join(', ')}</span>
            <span>Hoàn: <strong>{formatPrice(r.refundAmount ?? r.requestedAmount)}</strong></span>
          </div>
        </Link>
        ))}
      </QueryState>
      {data && data.totalCount > data.pageSize && (
        <div className="account-pager">
          <button disabled={page <= 1} onClick={() => setPage(page - 1)}>‹</button>
          <span>{page}</span>
          <button disabled={page * data.pageSize >= data.totalCount} onClick={() => setPage(page + 1)}>›</button>
        </div>
      )}
    </div>
  );
};

/** /tai-khoan/tra-hang/:code */
export const ReturnDetailPage = () => {
  const { code = '' } = useParams();
  const queryClient = useQueryClient();
  const [disputing, setDisputing] = useState(false);
  const [disputeReason, setDisputeReason] = useState('');
  const [message, setMessage] = useState('');
  const { data, error } = useQuery({ queryKey: ['return', code], queryFn: () => returnsApi.get(code), retry: false, staleTime: 0 });
  if (error) return <div className="account-card"><p>{error instanceof ApiError ? error.message : 'Không tải được yêu cầu.'}</p></div>;
  if (!data) return <div className="page-loader"><div className="loading-spinner" /></div>;
  const act = async (operation: 'cancel' | 'accept-offer' | 'dispute') => {
    setMessage('');
    try {
      setMessage((await returnsApi.act(code, operation, disputeReason)).message);
      setDisputing(false);
      void queryClient.invalidateQueries({ queryKey: ['return', code] });
    } catch (e) {
      setMessage(e instanceof ApiError ? e.message : 'Không thực hiện được.');
    }
  };
  const canDispute = data.status === 'Rejected' || data.status === 'PartialOffered';
  const canCancel = ['Requested', 'PartialOffered', 'Rejected', 'AwaitingReturn'].includes(data.status);
  return (
    <div className="account-card" data-testid="return-detail">
      <div className="order-detail-head">
        <Link to="/tai-khoan/tra-hang">‹ Trở lại</Link>
        <span>Yêu cầu <strong>{data.code}</strong> · <span data-testid="return-status">{data.statusLabel}</span></span>
      </div>
      <p>Đơn {data.orderCode} · {data.shopName} · {data.type === 'RefundOnly' ? 'Chỉ hoàn tiền' : 'Trả hàng & hoàn tiền'}</p>
      <ContactShopButton shopId={data.shopId} orderCode={data.orderCode} className="account-btn-outline" />
      <p>Yêu cầu hoàn: <strong>{formatPrice(data.requestedAmount)}</strong>{data.requestedCoins > 0 && ` + ${formatCount(data.requestedCoins)} xu`} · Hoàn về: {data.refundDestination}</p>
      {data.offeredAmount !== null && data.status === 'PartialOffered' && (
        <div className="order-detail-alert" data-testid="partial-offer">
          Shop đề nghị hoàn {formatPrice(data.offeredAmount)}{data.shopNote && ` — ${data.shopNote}`}
          <button className="account-btn" onClick={() => act('accept-offer')} data-testid="accept-offer">Đồng ý</button>
        </div>
      )}
      {data.status === 'Rejected' && <div className="order-detail-alert">Shop từ chối: {data.shopNote}{data.respondBy && ` · Khiếu nại trước ${formatDateTime(data.respondBy)}`}</div>}
      {data.refundAmount !== null && <div className="order-detail-alert" data-testid="refund-amount">Đã hoàn {formatPrice(data.refundAmount)}{(data.refundCoins ?? 0) > 0 && ` + ${formatCount(data.refundCoins!)} xu`}</div>}
      {data.disputeDecision && <div className="order-detail-alert">Sàn phân xử: {data.disputeDecision === 'FavorBuyer' ? 'người mua đúng' : 'shop đúng'} — {data.disputeDecisionReason}</div>}
      {data.returnTrackingNo && <p>Mã vận đơn trả hàng: <Link to={`/tra-cuu-van-don/${data.returnTrackingNo}`}>{data.returnTrackingNo}</Link> (mang hàng ra bưu cục gần nhất)</p>}
      {message && <div className="account-message" role="status">{message}</div>}
      <div className="order-actions">
        {canDispute && !disputing && <button className="account-btn" onClick={() => setDisputing(true)} data-testid="open-dispute">Khiếu nại lên ShopHub</button>}
        {canCancel && (
          <ConfirmButton className="account-btn-outline" message="Huỷ yêu cầu trả hàng / hoàn tiền này?" confirmLabel="Huỷ yêu cầu" onConfirm={() => act('cancel')}
            testId="cancel-return">Huỷ yêu cầu</ConfirmButton>
        )}
        {disputing && (
          <div className="order-cancel-box">
            <textarea rows={3} value={disputeReason} onChange={(e) => setDisputeReason(e.target.value)} placeholder="Vì sao bạn không đồng ý với shop?" data-testid="dispute-reason" />
            <button className="account-btn" disabled={!disputeReason.trim()} onClick={() => act('dispute')} data-testid="dispute-submit">Gửi khiếu nại</button>
          </div>
        )}
      </div>
      <h3 className="account-subtitle">Sản phẩm</h3>
      {data.items.map((i) => (
        <div key={i.orderItemId} className="order-card-item">
          <img src={imageOrPlaceholder(i.imageUrl)} alt="" onError={handleImgError} />
          <span><span className="order-card-name">{i.name}</span><span>×{i.quantity}</span></span>
          <span className="order-item-price">{formatPrice(i.refundAmount)}</span>
        </div>
      ))}
      <h3 className="account-subtitle">Bằng chứng</h3>
      <div className="media-picker">
        {data.evidence.map((e, i) => (
          <a key={i} href={e.url} target="_blank" rel="noreferrer" className="media-picker-item" title={e.party === 'Buyer' ? 'Người mua' : 'Shop'}>
            {e.type === 'Image' ? <img src={e.url} alt="" onError={handleImgError} /> : <span><Play size={14} aria-hidden /> Video</span>}
          </a>
        ))}
      </div>
      <ol className="order-timeline">
        {data.history.map((h, i) => <li key={i}><strong>{h.label}</strong> <span>{formatDateTime(h.occurredAt)}</span>{h.note && <em> — {h.note}</em>}</li>)}
      </ol>
    </div>
  );
};
