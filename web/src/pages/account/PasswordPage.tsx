import { useState, type FormEvent } from 'react';
import { useMutation } from '@tanstack/react-query';
import { accountApi } from '../../api/account';
import { ApiError } from '../../api/http';
import GooglePasswordHint from '../../components/GooglePasswordHint';

const PasswordPage = () => {
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [notice, setNotice] = useState('');
  const [mismatch, setMismatch] = useState(false);

  const change = useMutation({
    mutationFn: () => accountApi.changePassword(current, next),
    onSuccess: (res) => {
      setNotice(res.message);
      setCurrent('');
      setNext('');
      setConfirm('');
    },
  });
  const fieldError = (name: string) => (change.error instanceof ApiError ? change.error.field(name) : undefined);

  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setNotice('');
    setMismatch(next !== confirm);
    if (next === confirm) change.mutate();
  };

  return (
    <div className="account-card">
      <div className="account-card-head">
        <h1 className="account-card-title">Đổi mật khẩu</h1>
        <p className="account-card-sub">Để bảo mật tài khoản, vui lòng không chia sẻ mật khẩu cho người khác. Các thiết bị khác sẽ bị đăng xuất.</p>
      </div>
      {notice && <div className="account-notice" role="status">{notice}</div>}
      <GooglePasswordHint />
      <form className="account-form" onSubmit={onSubmit}>
        <label className="account-row">
          <span className="account-label">Mật khẩu hiện tại</span>
          <input type="password" className="account-input" value={current} onChange={(e) => setCurrent(e.target.value)}
            aria-label="Mật khẩu hiện tại" autoComplete="current-password" />
        </label>
        {fieldError('currentPassword') && <div className="account-error">{fieldError('currentPassword')}</div>}
        <label className="account-row">
          <span className="account-label">Mật khẩu mới</span>
          <input type="password" className="account-input" value={next} onChange={(e) => setNext(e.target.value)}
            aria-label="Mật khẩu mới" autoComplete="new-password" />
        </label>
        {fieldError('newPassword') && <div className="account-error">{fieldError('newPassword')}</div>}
        <label className="account-row">
          <span className="account-label">Xác nhận mật khẩu</span>
          <input type="password" className="account-input" value={confirm} onChange={(e) => setConfirm(e.target.value)}
            aria-label="Xác nhận mật khẩu mới" autoComplete="new-password" />
        </label>
        {mismatch && <div className="account-error">Mật khẩu xác nhận không khớp.</div>}
        {change.isError && !fieldError('currentPassword') && !fieldError('newPassword') && (
          <div className="account-error">{change.error instanceof ApiError ? change.error.message : 'Đã có lỗi xảy ra.'}</div>
        )}
        <div className="account-row">
          <span className="account-label" />
          <button type="submit" className="account-btn-primary" disabled={change.isPending}>Xác nhận</button>
        </div>
      </form>
    </div>
  );
};

export default PasswordPage;
