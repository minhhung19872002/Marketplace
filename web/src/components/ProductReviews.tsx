import { useRef, useState } from 'react';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocation, useNavigate } from 'react-router-dom';
import { ThumbsUp } from 'lucide-react';
import { REVIEW_REPORT_REASONS, reviewsApi, type Review } from '../api/aftercare';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import { formatDate } from '../lib/datetime';
import { handleImgError } from '../lib/image';
import ImageLightbox from './ImageLightbox';
import { Pager, Stars } from './ui';
import './ProductReviews.css';

type Filter = { rating?: number; withMedia?: boolean; withComment?: boolean; variant?: string };

/** Share of the reviews with `n` stars, as a bar width (0–100). */
export const starShare = (byStar: Record<string, number>, n: number, total: number): number =>
  total > 0 ? Math.round(((byStar[String(n)] ?? 0) * 100) / total) : 0;

/** "Hữu ích" of one review: optimistic for the viewer, guests are sent to sign in. */
const HelpfulButton = ({ review, onChanged }: { review: Review; onChanged: () => void }) => {
  const { isLoggedIn } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [state, setState] = useState({ on: review.helpfulByMe, count: review.helpfulCount });
  const [busy, setBusy] = useState(false);
  const click = async () => {
    if (!isLoggedIn) {
      navigate('/dang-nhap', { state: { from: location.pathname } });
      return;
    }
    if (busy) return;
    setBusy(true);
    const on = !state.on;
    try {
      const { data } = await reviewsApi.helpful(review.id, on);
      setState({ on, count: data });
      onChanged();
    } catch {
      // the count stays as it was; the shared toast reports API errors
    } finally {
      setBusy(false);
    }
  };
  return (
    <button type="button" className={`pd-review-helpful ${state.on ? 'is-on' : ''}`} onClick={() => void click()} aria-pressed={state.on}
      data-testid="review-helpful">
      <ThumbsUp size={14} fill={state.on ? 'currentColor' : 'none'} aria-hidden />
      {state.count > 0 ? `Hữu ích (${state.count})` : 'Hữu ích?'}
    </button>
  );
};

/** "Đánh giá sản phẩm" on the product page (G2-B2): star distribution, filters, photos in a viewer, "Hữu ích", pager. */
const ProductReviews = ({ productId }: { productId: string }) => {
  const { isLoggedIn } = useAuth();
  const queryClient = useQueryClient();
  const top = useRef<HTMLDivElement>(null);
  const [filter, setFilter] = useState<Filter>({});
  const [page, setPage] = useState(1);
  const [message, setMessage] = useState('');
  // The review being reported and the reason picked (L146: the reason used to be fixed)
  const [reporting, setReporting] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [viewer, setViewer] = useState<{ images: string[]; index: number } | null>(null);
  const { data } = useQuery({
    queryKey: ['product-reviews', productId, filter, page, isLoggedIn],
    queryFn: () => reviewsApi.forProduct(productId, { ...filter, page }),
    placeholderData: keepPreviousData,
  });
  if (!data) return null;
  const { summary, reviews } = data;
  const chips: { label: string; f: Filter }[] = [
    { label: 'Tất Cả', f: {} },
    ...[5, 4, 3, 2, 1].map((n) => ({ label: `${n} Sao (${summary.byStar[String(n)] ?? 0})`, f: { rating: n } })),
    { label: `Có Hình Ảnh / Video (${summary.withMedia})`, f: { withMedia: true } },
    { label: `Có Bình Luận (${summary.withComment})`, f: { withComment: true } },
    // "Theo phân loại" (spec 3.11): the variants buyers reviewed
    ...(summary.variants ?? []).map((v) => ({ label: `Phân loại: ${v.variant} (${v.count})`, f: { variant: v.variant } })),
  ];
  const report = async (id: string) => {
    try {
      setMessage((await reviewsApi.report(id, reason)).message);
      setReporting(null);
      setReason('');
    } catch (e) {
      setMessage(e instanceof ApiError ? e.message : 'Không gửi được báo cáo.');
    }
  };
  const go = (p: number) => {
    setPage(p);
    top.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };
  const pages = Math.max(1, Math.ceil(reviews.totalCount / reviews.pageSize));
  return (
    <div className="pd-section" data-testid="reviews" ref={top}>
      <h2 className="pd-section-title">Đánh giá sản phẩm</h2>
      <div className="pd-rating-summary">
        <div className="pd-rating-score">
          <span><span className="pd-rating-big" data-testid="rating-average">{summary.average.toFixed(1)}</span> <span className="pd-rating-outof">trên 5</span></span>
          <Stars value={summary.average} size={18} />
          <span className="pd-rating-total">{summary.total} đánh giá</span>
        </div>
        <ul className="pd-rating-dist" aria-label="Phân bố số sao" data-testid="rating-dist">
          {[5, 4, 3, 2, 1].map((n) => (
            <li key={n}>
              <span className="pd-rating-dist-label">{n} sao</span>
              <span className="pd-rating-dist-track" aria-hidden><span style={{ width: `${starShare(summary.byStar, n, summary.total)}%` }} /></span>
              <span className="pd-rating-dist-count">{summary.byStar[String(n)] ?? 0}</span>
            </li>
          ))}
        </ul>
        <div className="pd-review-filters" role="group" aria-label="Lọc đánh giá">
          {chips.map((c) => {
            const active = JSON.stringify(c.f) === JSON.stringify(filter);
            return (
              <button key={c.label} type="button" className={active ? 'active' : ''} aria-pressed={active} onClick={() => { setFilter(c.f); setPage(1); }}>
                {c.label}
              </button>
            );
          })}
        </div>
      </div>
      {message && <p className="pd-review-empty" role="status">{message}</p>}
      {reviews.items.length === 0 ? (
        <p className="pd-review-empty">Chưa có đánh giá nào{summary.total > 0 ? ' phù hợp bộ lọc' : ' cho sản phẩm này'}.</p>
      ) : (
        <div className="pd-reviews">
          {reviews.items.map((r) => {
            const photos = r.media.filter((m) => m.type === 'Image').map((m) => m.url);
            return (
              <div key={r.id} className="pd-review" data-testid="review">
                <div className="pd-review-avatar" aria-hidden>{r.reviewerName.charAt(0).toUpperCase()}</div>
                <div className="pd-review-body">
                  <div className="pd-review-name">{r.reviewerName}</div>
                  <div className="pd-review-stars"><Stars value={r.rating} size={12} /></div>
                  <div className="pd-review-date">{formatDate(r.createdAt)}{r.variant && ` | Phân loại hàng: ${r.variant}`}{r.edited && ' · đã sửa'}</div>
                  {r.tags.length > 0 && <div className="pd-review-tags">{r.tags.join(' · ')}</div>}
                  {r.content && <p className="pd-review-text">{r.content}</p>}
                  {r.media.length > 0 && (
                    <div className="pd-review-media">
                      {r.media.map((m, i) => m.type === 'Image'
                        ? (
                          <button key={i} type="button" className="pd-review-photo" aria-label={`Xem ảnh ${photos.indexOf(m.url) + 1} của đánh giá`}
                            onClick={() => setViewer({ images: photos, index: photos.indexOf(m.url) })} data-testid="review-photo">
                            <img src={m.url} alt="" onError={handleImgError} loading="lazy" />
                          </button>
                        )
                        : <video key={i} src={m.url} controls preload="metadata" />)}
                    </div>
                  )}
                  {r.sellerReply && <div className="pd-review-reply"><strong>Phản hồi của người bán:</strong> {r.sellerReply}</div>}
                  <div className="pd-review-actions">
                    <HelpfulButton review={r} onChanged={() => void queryClient.invalidateQueries({ queryKey: ['product-reviews', productId] })} />
                    {isLoggedIn && (
                      <button type="button" className="pd-review-report" data-testid="review-report" onClick={() => setReporting(reporting === r.id ? null : r.id)}>Báo cáo</button>
                    )}
                  </div>
                  {reporting === r.id && (
                    <form className="pd-review-report-form" onSubmit={(e) => { e.preventDefault(); if (reason) void report(r.id); }}>
                      <select value={reason} onChange={(e) => setReason(e.target.value)} aria-label="Lý do báo cáo đánh giá" data-testid="review-report-reason">
                        <option value="">Chọn lý do…</option>
                        {REVIEW_REPORT_REASONS.map((x) => <option key={x}>{x}</option>)}
                      </select>
                      <button type="submit" disabled={!reason} data-testid="review-report-send">Gửi báo cáo</button>
                    </form>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}
      <Pager page={page} total={pages} onPage={go} testId="reviews-pager" label="Trang đánh giá" />
      {viewer && <ImageLightbox images={viewer.images} index={viewer.index} alt="Ảnh đánh giá" onIndex={(i) => setViewer({ ...viewer, index: i })}
        onClose={() => setViewer(null)} />}
    </div>
  );
};

export default ProductReviews;
