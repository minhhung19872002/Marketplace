import { useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { authApi } from '../api/auth';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import { useCountdown } from '../lib/useCountdown';
import './Auth.css';

type Mode = 'password' | 'otp';

const Login = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { signIn } = useAuth();
  const [mode, setMode] = useState<Mode>('password');
  const [identifier, setIdentifier] = useState('');
  const [password, setPassword] = useState('');
  const [phone, setPhone] = useState('');
  const [code, setCode] = useState('');
  const [error, setError] = useState('');
  const [info, setInfo] = useState('');
  const [busy, setBusy] = useState(false);
  const [resendIn, startResend] = useCountdown();

  const redirectTo = (location.state as { from?: string } | null)?.from ?? '/';

  const run = async (action: () => Promise<void>) => {
    setError('');
    setInfo('');
    setBusy(true);
    try {
      await action();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Đã có lỗi xảy ra, vui lòng thử lại.');
    } finally {
      setBusy(false);
    }
  };

  const submitPassword = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!identifier.trim() || !password) {
      setError('Vui lòng nhập tên đăng nhập và mật khẩu');
      return;
    }
    void run(async () => {
      signIn(await authApi.login(identifier.trim(), password));
      navigate(redirectTo, { replace: true });
    });
  };

  const sendCode = () =>
    run(async () => {
      const issued = await authApi.sendOtp(phone.trim(), 'Login');
      startResend(issued.resendAfterSeconds);
      setInfo('Nếu số điện thoại đã đăng ký, mã xác thực đã được gửi qua SMS.');
    });

  const submitOtp = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    void run(async () => {
      signIn(await authApi.loginWithOtp(phone.trim(), code.trim()));
      navigate(redirectTo, { replace: true });
    });
  };

  return (
    <div className="auth-page">
      <div className="auth-hero">
        <div className="container auth-hero-inner">
          <div className="auth-hero-brand">
            <h1>ShopHub</h1>
            <p>Đăng nhập</p>
            <span>Mua sắm tại ShopHub, sàn thương mại điện tử uy tín hàng đầu.</span>
          </div>

          <div className="auth-card">
            <h2 className="auth-title">Đăng Nhập</h2>
            <div className="auth-tabs" role="tablist">
              <button
                type="button"
                role="tab"
                aria-selected={mode === 'password'}
                className={`auth-tab ${mode === 'password' ? 'active' : ''}`}
                onClick={() => setMode('password')}
              >
                Mật khẩu
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={mode === 'otp'}
                className={`auth-tab ${mode === 'otp' ? 'active' : ''}`}
                onClick={() => setMode('otp')}
                data-testid="login-otp-tab"
              >
                Mã SMS
              </button>
            </div>

            {error && <div className="auth-error" data-testid="auth-error">{error}</div>}
            {info && <div className="auth-info">{info}</div>}

            {mode === 'password' ? (
              <form onSubmit={submitPassword} className="auth-form">
                <input
                  type="text"
                  className="auth-input"
                  placeholder="Số điện thoại / Email / Tên đăng nhập"
                  value={identifier}
                  onChange={(e) => setIdentifier(e.target.value)}
                  aria-label="Tên đăng nhập"
                  autoComplete="username"
                />
                <input
                  type="password"
                  className="auth-input"
                  placeholder="Mật khẩu"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  aria-label="Mật khẩu"
                  autoComplete="current-password"
                />
                <button type="submit" className="auth-submit" data-testid="login-submit" disabled={busy}>
                  {busy ? 'ĐANG ĐĂNG NHẬP…' : 'ĐĂNG NHẬP'}
                </button>
              </form>
            ) : (
              <form onSubmit={submitOtp} className="auth-form">
                <input
                  type="tel"
                  className="auth-input"
                  placeholder="Số điện thoại"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  aria-label="Số điện thoại"
                  autoComplete="tel"
                />
                <div className="auth-otp-row">
                  <input
                    type="text"
                    inputMode="numeric"
                    maxLength={6}
                    className="auth-input"
                    placeholder="Mã xác thực 6 số"
                    value={code}
                    onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
                    aria-label="Mã xác thực"
                    autoComplete="one-time-code"
                  />
                  <button type="button" className="auth-otp-btn" onClick={sendCode} disabled={busy || resendIn > 0 || !phone.trim()}>
                    {resendIn > 0 ? `Gửi lại (${resendIn}s)` : 'Gửi mã'}
                  </button>
                </div>
                <button type="submit" className="auth-submit" disabled={busy || code.length !== 6}>
                  ĐĂNG NHẬP
                </button>
              </form>
            )}

            <div className="auth-links">
              <Link to="/quen-mat-khau">Quên mật khẩu</Link>
            </div>
            <div className="auth-footer">
              Bạn mới biết đến ShopHub? <Link to="/dang-ky">Đăng ký</Link>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default Login;
