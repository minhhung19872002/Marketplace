import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { authApi } from '../api/auth';
import { ApiError } from '../api/http';
import { useCountdown } from '../lib/useCountdown';
import './Auth.css';

type Step = 'target' | 'code' | 'password' | 'done';

const ForgotPassword = () => {
  const navigate = useNavigate();
  const [step, setStep] = useState<Step>('target');
  const [target, setTarget] = useState('');
  const [code, setCode] = useState('');
  const [ticket, setTicket] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [resendIn, startResend] = useCountdown();

  const run = async (action: () => Promise<void>) => {
    setError('');
    setBusy(true);
    try {
      await action();
    } catch (err) {
      setError(err instanceof ApiError ? err.fieldErrors[0]?.message ?? err.message : 'Đã có lỗi xảy ra, vui lòng thử lại.');
    } finally {
      setBusy(false);
    }
  };

  const sendCode = () =>
    run(async () => {
      const issued = await authApi.forgotPassword(target.trim());
      startResend(issued.resendAfterSeconds);
      setStep('code');
    });

  const submitTarget = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    void sendCode();
  };

  const submitCode = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    void run(async () => {
      setTicket((await authApi.verifyOtp(target.trim(), 'ResetPassword', code.trim())).ticket);
      setStep('password');
    });
  };

  const submitPassword = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    void run(async () => {
      await authApi.resetPassword(target.trim(), ticket, password);
      setStep('done');
    });
  };

  return (
    <div className="auth-page">
      <div className="auth-hero">
        <div className="container auth-hero-inner">
          <div className="auth-hero-brand">
            <h1>ShopHub</h1>
            <p>Đặt lại mật khẩu</p>
          </div>

          <div className="auth-card">
            <h2 className="auth-title">Đặt Lại Mật Khẩu</h2>
            {error && <div className="auth-error" data-testid="auth-error">{error}</div>}

            {step === 'target' && (
              <form onSubmit={submitTarget} className="auth-form">
                <input
                  className="auth-input"
                  placeholder="Số điện thoại hoặc email"
                  value={target}
                  onChange={(e) => setTarget(e.target.value)}
                  aria-label="Số điện thoại hoặc email"
                />
                <button type="submit" className="auth-submit" disabled={busy || !target.trim()}>TIẾP THEO</button>
              </form>
            )}

            {step === 'code' && (
              <form onSubmit={submitCode} className="auth-form">
                <div className="auth-info">Nếu tài khoản tồn tại, mã xác thực đã được gửi tới {target}.</div>
                <div className="auth-otp-row">
                  <input
                    inputMode="numeric"
                    maxLength={6}
                    className="auth-input"
                    placeholder="Mã xác thực 6 số"
                    value={code}
                    onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
                    aria-label="Mã xác thực"
                    autoComplete="one-time-code"
                  />
                  <button type="button" className="auth-otp-btn" onClick={sendCode} disabled={busy || resendIn > 0}>
                    {resendIn > 0 ? `Gửi lại (${resendIn}s)` : 'Gửi lại'}
                  </button>
                </div>
                <button type="submit" className="auth-submit" disabled={busy || code.length !== 6}>XÁC NHẬN</button>
              </form>
            )}

            {step === 'password' && (
              <form onSubmit={submitPassword} className="auth-form">
                <input
                  type="password"
                  className="auth-input"
                  placeholder="Mật khẩu mới (≥ 8 ký tự, có chữ và số)"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  aria-label="Mật khẩu mới"
                  autoComplete="new-password"
                />
                <button type="submit" className="auth-submit" disabled={busy}>ĐẶT LẠI MẬT KHẨU</button>
              </form>
            )}

            {step === 'done' && (
              <div className="auth-form">
                <div className="auth-info">Đã đặt lại mật khẩu. Mọi thiết bị đã được đăng xuất.</div>
                <button type="button" className="auth-submit" onClick={() => navigate('/dang-nhap')}>ĐĂNG NHẬP</button>
              </div>
            )}

            <div className="auth-footer">
              <Link to="/dang-nhap">Quay lại đăng nhập</Link>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ForgotPassword;
