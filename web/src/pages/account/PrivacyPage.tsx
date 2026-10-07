import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { accountApi } from '../../api/account';
import { ApiError } from '../../api/http';
import { useAuth } from '../../context/AuthContext';
import { formatDateTime } from '../../lib/datetime';

const errorText = (err: unknown) => (err instanceof ApiError ? err.fieldErrors[0]?.message ?? err.message : 'Đã có lỗi xảy ra.');

/** Quyền riêng tư (spec I.2, Nghị định 13/2023): download my data, ask to delete the account. */
const PrivacyPage = () => {
  const { logout } = useAuth();
  const navigate = useNavigate();
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [password, setPassword] = useState('');
  const [understood, setUnderstood] = useState(false);

  const download = async () => {
    setError('');
    setBusy(true);
    try {
      const data = await accountApi.exportData();
      const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
      const link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = `du-lieu-shophub-${data.exportedAt.slice(0, 10)}.json`;
      link.click();
      URL.revokeObjectURL(link.href);
      setNotice(`Đã tải dữ liệu của bạn (lập lúc ${formatDateTime(data.exportedAt)}).`);
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      await accountApi.deleteAccount(password);
      await logout();
      navigate('/', { replace: true });
    } catch (err) {
      setError(errorText(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <div className="account-card">
        <div className="account-card-head">
          <h1 className="account-card-title">Quyền Riêng Tư</h1>
          <p className="account-card-sub">Dữ liệu cá nhân của bạn được xử lý theo Chính sách bảo mật của ShopHub</p>
        </div>
        {notice && <div className="account-notice" role="status">{notice}</div>}
        {error && <div className="account-error" role="alert">{error}</div>}
        <div className="account-form">
          <h2 className="account-section-title">Tải dữ liệu của tôi</h2>
          <p>Một tệp JSON gồm hồ sơ, sổ địa chỉ, thiết bị đăng nhập, đơn hàng, đánh giá, sản phẩm yêu thích, shop theo dõi, số dư Ví ShopHub và lịch sử Xu.</p>
          <div className="account-actions">
            <button type="button" className="account-btn-outline" onClick={() => void download()} disabled={busy} data-testid="privacy-export">
              Tải dữ liệu
            </button>
          </div>
        </div>
      </div>

      <div className="account-card">
        <div className="account-card-head">
          <h2 className="account-card-title">Xoá tài khoản</h2>
          <p className="account-card-sub">
            Thông tin cá nhân sẽ được ẩn danh, không khôi phục được. Đơn hàng cũ vẫn được giữ (ẩn danh) cho nghĩa vụ kế toán.
            Bạn cần hoàn tất mọi đơn hàng, yêu cầu trả hàng và rút hết số dư Ví ShopHub trước; Xu còn lại sẽ mất.
          </p>
        </div>
        {!confirming ? (
          <div className="account-actions">
            <button type="button" className="account-btn-outline" onClick={() => setConfirming(true)} data-testid="privacy-delete">Yêu cầu xoá tài khoản</button>
          </div>
        ) : (
          <form className="account-form" onSubmit={remove}>
            <label className="account-row">
              <span className="account-label">Mật khẩu</span>
              <input type="password" className="account-input" value={password} onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password" aria-label="Mật khẩu xác nhận xoá" data-testid="privacy-password" />
            </label>
            <label className="account-radio">
              <input type="checkbox" checked={understood} onChange={(e) => setUnderstood(e.target.checked)} data-testid="privacy-understood" />
              Tôi hiểu việc xoá tài khoản là vĩnh viễn
            </label>
            <div className="account-actions">
              <button type="button" className="account-btn-outline" onClick={() => setConfirming(false)} data-confirm="local">Huỷ</button>
              <button type="submit" className="account-btn-primary" disabled={!password || !understood || busy} data-testid="privacy-delete-confirm"
                data-confirm="dialog">
                Xoá vĩnh viễn
              </button>
            </div>
          </form>
        )}
      </div>
    </>
  );
};

export default PrivacyPage;
