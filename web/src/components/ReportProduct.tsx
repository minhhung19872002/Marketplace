import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { REPORT_REASONS, contentApi, type ProductReportReason } from '../api/content';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import { Flag } from 'lucide-react';

/** "Báo cáo sản phẩm vi phạm" (spec II.4): signed-in buyers, one open report per product. */
const ReportProduct = ({ productId }: { productId: string }) => {
  const { isLoggedIn } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState<ProductReportReason>('Counterfeit');
  const [details, setDetails] = useState('');
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null);

  const submit = async () => {
    try {
      const r = await contentApi.reportProduct(productId, reason, details.trim() || null);
      setResult({ ok: true, text: r.message });
    } catch (e) {
      setResult({ ok: false, text: e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : 'Không gửi được báo cáo.' });
    }
  };

  return (
    <>
      <button type="button" className="pd-report" data-testid="report-product"
        onClick={() => (isLoggedIn ? setOpen(true) : navigate('/dang-nhap', { state: { from: location.pathname } }))}>
        <Flag size={13} aria-hidden /> Báo cáo sản phẩm
      </button>
      {open && (
        <div className="pd-report-backdrop" role="dialog" aria-modal="true" aria-label="Báo cáo sản phẩm">
          <div className="pd-report-box">
            <h3>Báo cáo sản phẩm này</h3>
            {result?.ok ? (
              <p role="status">{result.text}</p>
            ) : (
              <>
                {REPORT_REASONS.map((r) => (
                  <label key={r.value} className="pd-report-reason">
                    <input type="radio" name="report-reason" checked={reason === r.value} onChange={() => setReason(r.value)} /> {r.label}
                  </label>
                ))}
                <textarea value={details} onChange={(e) => setDetails(e.target.value)} maxLength={1000} rows={3} placeholder="Mô tả thêm (bắt buộc khi chọn Khác)"
                  aria-label="Mô tả" />
                {result && <p className="pd-report-error" role="alert">{result.text}</p>}
              </>
            )}
            <div className="pd-report-actions">
              <button type="button" onClick={() => { setOpen(false); setResult(null); }}>{result?.ok ? 'Đóng' : 'Huỷ'}</button>
              {!result?.ok && <button type="button" className="primary" onClick={submit} data-testid="report-submit">Gửi báo cáo</button>}
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export default ReportProduct;
