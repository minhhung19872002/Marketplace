import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { authApi } from '../api/auth';
import { ApiError } from '../api/http';
import { useAuth } from '../context/AuthContext';
import { useCountdown } from '../lib/useCountdown';
import GoogleSignIn from '../components/GoogleSignIn';
import './Auth.css';

type Step = 'phone' | 'code' | 'details';
// Sign up with a phone number (OTP by SMS) or an email address (OTP by mail) — spec I.1, E1
type Channel = 'phone' | 'email';

const Register = () => {
  const navigate = useNavigate();
  const { signIn } = useAuth();
  const [step, setStep] = useState<Step>('phone');
  const [channel, setChannel] = useState<Channel>('phone');
  // The phone number or the email address, as typed
  const [phone, setPhone] = useState('');
  const [code, setCode] = useState('');
  const [ticket, setTicket] = useState('');
  const [fullName, setFullName] = useState('');
  const [password, setPassword] = useState('');
  const [acceptTerms, setAcceptTerms] = useState(false);
  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [resendIn, startResend] = useCountdown();

  const run = async (action: () => Promise<void>) => {
    setError('');
    setFieldErrors({});
    setBusy(true);
    try {
      await action();
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
        setFieldErrors(Object.fromEntries(err.fieldErrors.map((f) => [f.field, f.message])));
      } else {
        setError('Đã có lỗi xảy ra, vui lòng thử lại.');
      }
    } finally {
      setBusy(false);
    }
  };

  const sendCode = () =>
    run(async () => {
      const issued = await authApi.sendOtp(phone.trim(), 'Register');
      startResend(issued.resendAfterSeconds);
      setStep('code');
    });

  const submitPhone = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!phone.trim()) {
      setError(channel === 'email' ? 'Vui lòng nhập email' : 'Vui lòng nhập số điện thoại');
      return;
    }
    void sendCode();
  };

  const submitCode = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    void run(async () => {
      const result = await authApi.verifyOtp(phone.trim(), 'Register', code.trim());
      setTicket(result.ticket);
      setStep('details');
    });
  };

  const submitDetails = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    void run(async () => {
      signIn(await authApi.register({ target: phone.trim(), ticket, password, fullName: fullName.trim(), acceptTerms }));
      navigate('/', { replace: true });
    });
  };

  return (
    <div className="auth-page">
      <div className="auth-hero">
        <div className="container auth-hero-inner">
          <div className="auth-hero-brand">
            <h1>ShopHub</h1>
            <p>Đăng ký</p>
            <span>Tạo tài khoản để mua sắm và nhận ưu đãi độc quyền.</span>
          </div>

          <div className="auth-card">
            <h2 className="auth-title">Đăng Ký</h2>
            <ol className="auth-steps" aria-label="Các bước đăng ký">
              <li className={step === 'phone' ? 'active' : ''}>{channel === 'email' ? 'Email' : 'Số điện thoại'}</li>
              <li className={step === 'code' ? 'active' : ''}>Xác thực</li>
              <li className={step === 'details' ? 'active' : ''}>Thông tin</li>
            </ol>
            {error && <div className="auth-error" data-testid="auth-error">{error}</div>}

            {step === 'phone' && (
              <form onSubmit={submitPhone} className="auth-form">
                <div className="auth-channel" role="tablist" aria-label="Đăng ký bằng">
                  {(['phone', 'email'] as const).map((c) => (
                    <button key={c} type="button" role="tab" aria-selected={channel === c} className={`auth-channel-btn ${channel === c ? 'active' : ''}`}
                      onClick={() => { setChannel(c); setPhone(''); setError(''); }} data-testid={`register-channel-${c}`}>
                      {c === 'phone' ? 'Số điện thoại' : 'Email'}
                    </button>
                  ))}
                </div>
                <input
                  type={channel === 'email' ? 'email' : 'tel'}
                  className="auth-input"
                  placeholder={channel === 'email' ? 'Email' : 'Số điện thoại'}
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  aria-label={channel === 'email' ? 'Email' : 'Số điện thoại'}
                  autoComplete={channel === 'email' ? 'email' : 'tel'}
                />
                <button type="submit" className="auth-submit" data-testid="register-send-otp" disabled={busy}>
                  TIẾP THEO
                </button>
              </form>
            )}

            {step === 'code' && (
              <form onSubmit={submitCode} className="auth-form">
                <div className="auth-info">
                  {channel === 'email'
                    ? `Mã xác thực đã được gửi tới hộp thư ${phone.trim()} (xem cả mục thư rác).`
                    : `Mã xác thực đã được gửi tới ${phone}.`}
                </div>
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
                  <button type="button" className="auth-otp-btn" onClick={sendCode} disabled={busy || resendIn > 0}>
                    {resendIn > 0 ? `Gửi lại (${resendIn}s)` : 'Gửi lại'}
                  </button>
                </div>
                <button type="submit" className="auth-submit" data-testid="register-verify" disabled={busy || code.length !== 6}>
                  XÁC NHẬN
                </button>
                <button type="button" className="auth-link-btn" onClick={() => setStep('phone')}>
                  {channel === 'email' ? 'Đổi email' : 'Đổi số điện thoại'}
                </button>
              </form>
            )}

            {step === 'details' && (
              <form onSubmit={submitDetails} className="auth-form">
                <input
                  type="text"
                  className="auth-input"
                  placeholder="Họ và tên"
                  value={fullName}
                  onChange={(e) => setFullName(e.target.value)}
                  aria-label="Họ và tên"
                  autoComplete="name"
                />
                {fieldErrors.fullName && <div className="auth-field-error">{fieldErrors.fullName}</div>}
                <input
                  type="password"
                  className="auth-input"
                  placeholder="Mật khẩu (≥ 8 ký tự, có chữ và số)"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  aria-label="Mật khẩu"
                  autoComplete="new-password"
                />
                {fieldErrors.password && <div className="auth-field-error">{fieldErrors.password}</div>}
                <label className="auth-consent">
                  <input
                    type="checkbox"
                    checked={acceptTerms}
                    onChange={(e) => setAcceptTerms(e.target.checked)}
                    aria-label="Đồng ý điều khoản"
                  />
                  <span>
                    Tôi đồng ý với Điều khoản sử dụng và Chính sách xử lý dữ liệu cá nhân của ShopHub (Nghị định 13/2023/NĐ-CP).
                  </span>
                </label>
                {fieldErrors.acceptTerms && <div className="auth-field-error">{fieldErrors.acceptTerms}</div>}
                <button type="submit" className="auth-submit" data-testid="register-submit" disabled={busy}>
                  ĐĂNG KÝ
                </button>
              </form>
            )}

            <GoogleSignIn text="signup_with" onSignedIn={(r) => { signIn(r); navigate('/', { replace: true }); }} />
            <div className="auth-footer">
              Bạn đã có tài khoản? <Link to="/dang-nhap">Đăng nhập</Link>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default Register;
