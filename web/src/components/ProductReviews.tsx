import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { REVIEW_REPORT_REASONS, reviewsApi } from '../api/aftercare';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import { formatDate } from '../lib/datetime';
import { handleImgError } from '../lib/image';
import { Stars } from './ui';

type Filter = { rating?: number; withMedia?: boolean; withComment?: boolean; variant?: string };

/** "ĐÁNH GIÁ SẢN PHẨM" on the product page: real reviews, star summary, filters, shop replies. */
const ProductReviews = ({ productId }: { productId: string }) => {
  const { isLoggedIn } = useAuth();
  const [filter, setFilter] = useState<Filter>({});
  const [page, setPage] = useState(1);
  const [message, setMessage] = useState('');
  // The review being reported and the reason picked (L146: the reason used to be fixed)
  const [reporting, setReporting] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const { data } = useQuery({
    queryKey: ['product-reviews', productId, filter, page],
    queryFn: () => reviewsApi.forProduct(productId, { ...filter, page }),
    placeholderData: keepPreviousData,
  });
  if (!data) return null;
  const { summary, reviews } = data;
  const chips: { label: string; f: Filter }[] = [
    { label: `Tất cả (${summary.total})`, f: {} },
    ...[5, 4, 3, 2, 1].map((n) => ({ label: `${n} sao (${summary.byStar[String(n)] ?? 0})`, f: { rating: n } })),
    { label: `Có hình ảnh/video (${summary.withMedia})`, f: { withMedia: true } },
    { label: `Có bình luận (${summary.withComment})`, f: { withComment: true } },
    // "Theo phân loại" (spec 3.11): the variants buyers reviewed
    ...(summary.variants ?? []).map((v) => ({ label: `${v.variant} (${v.count})`, f: { variant: v.variant } })),
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
  const pages = Math.max(1, Math.ceil(reviews.totalCount / reviews.pageSize));
  return (
    <div className="pd-section" data-testid="reviews">
      <h2 className="pd-section-title">ĐÁNH GIÁ SẢN PHẨM</h2>
      <div className="pd-rating-summary">
        <div className="pd-rating-score">
          <span className="pd-rating-big" data-testid="rating-average">{summary.average.toFixed(1)}</span>
          <span className="pd-rating-outof">trên 5</span>
        </div>
        <div className="pd-review-filters">
          {chips.map((c) => (
            <button key={c.label} className={JSON.stringify(c.f) === JSON.stringify(filter) ? 'active' : ''} onClick={() => { setFilter(c.f); setPage(1); }}>
              {c.label}
            </button>
          ))}
        </div>
      </div>
      {message && <p className="pd-review-empty" role="status">{message}</p>}
      {reviews.items.length === 0 ? (
        <p className="pd-review-empty">Chưa có đánh giá nào{summary.total > 0 ? ' phù hợp bộ lọc' : ' cho sản phẩm này'}.</p>
      ) : (
        <div className="pd-reviews">
          {reviews.items.map((r) => (
            <div key={r.id} className="pd-review" data-testid="review">
              <div className="pd-review-avatar">{r.reviewerName.charAt(0).toUpperCase()}</div>
              <div className="pd-review-body">
                <div className="pd-review-name">{r.reviewerName}</div>
                <div className="pd-review-stars"><Stars value={r.rating} size={12} /></div>
                <div className="pd-review-date">{formatDate(r.createdAt)}{r.variant && ` | Phân loại: ${r.variant}`}{r.edited && ' · đã sửa'}</div>
                {r.tags.length > 0 && <div className="pd-review-tags">{r.tags.join(' · ')}</div>}
                {r.content && <p className="pd-review-text">{r.content}</p>}
                {r.media.length > 0 && (
                  <div className="pd-review-media">
                    {r.media.map((m, i) => m.type === 'Image'
                      ? <img key={i} src={m.url} alt="" onError={handleImgError} />
                      : <video key={i} src={m.url} controls preload="metadata" />)}
                  </div>
                )}
                {r.sellerReply && <div className="pd-review-reply"><strong>Phản hồi của người bán:</strong> {r.sellerReply}</div>}
                {isLoggedIn && (
                  <button className="pd-review-report" data-testid="review-report" onClick={() => setReporting(reporting === r.id ? null : r.id)}>Báo cáo</button>
                )}
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
          ))}
        </div>
      )}
      {pages > 1 && (
        <div className="search-pager">
          <button disabled={page <= 1} onClick={() => setPage(page - 1)}>‹</button>
          <span>{page}/{pages}</span>
          <button disabled={page >= pages} onClick={() => setPage(page + 1)}>›</button>
        </div>
      )}
    </div>
  );
};

export default ProductReviews;
