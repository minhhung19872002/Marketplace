import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { authApi } from '../api/auth';
import { ApiError } from '../api/http';
import type { AuthResult } from '../stores/auth';

interface GoogleIdentityServices {
  accounts: {
    id: {
      initialize(options: { client_id: string; callback: (response: { credential: string }) => void; ux_mode?: 'popup' }): void;
      renderButton(element: HTMLElement, options: { theme?: 'outline' | 'filled_blue'; size?: 'large' | 'medium'; text?: 'signin_with' | 'signup_with'; width?: number; locale?: string }): void;
    };
  };
}

declare global {
  interface Window {
    google?: GoogleIdentityServices;
  }
}

const SCRIPT = 'https://accounts.google.com/gsi/client';
let loading: Promise<GoogleIdentityServices> | null = null;

const loadGoogle = () => {
  loading ??= new Promise<GoogleIdentityServices>((resolve, reject) => {
    if (window.google) return resolve(window.google);
    const script = document.createElement('script');
    script.src = SCRIPT;
    script.async = true;
    script.onload = () => (window.google ? resolve(window.google) : reject(new Error('Google Identity Services unavailable')));
    script.onerror = () => {
      loading = null;
      reject(new Error('Google Identity Services unavailable'));
    };
    document.head.appendChild(script);
  });
  return loading;
};

/**
 * "Đăng nhập bằng Google" (spec I.1, tuỳ chọn): shown only when the server has a Google client id. The ID token goes to the
 * API, which verifies it with Google's keys; a new account asks for the consent to the terms first.
 */
const GoogleSignIn = ({ onSignedIn, text = 'signin_with' }: { onSignedIn: (result: AuthResult) => void; text?: 'signin_with' | 'signup_with' }) => {
  const providers = useQuery({ queryKey: ['auth-providers'], queryFn: authApi.providers, staleTime: 3_600_000 });
  const clientId = providers.data?.googleClientId ?? null;
  const target = useRef<HTMLDivElement>(null);
  const [pending, setPending] = useState<string | null>(null);
  const [accepted, setAccepted] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (credential: string, acceptTerms: boolean) => {
    setBusy(true);
    setError('');
    try {
      onSignedIn(await authApi.google(credential, acceptTerms));
    } catch (e) {
      if (e instanceof ApiError && e.status === 409 && !acceptTerms) setPending(credential);
      else setError(e instanceof ApiError ? e.message : 'Không đăng nhập được bằng Google.');
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    if (!clientId || !target.current) return;
    let cancelled = false;
    loadGoogle()
      .then((google) => {
        if (cancelled || !target.current) return;
        google.accounts.id.initialize({ client_id: clientId, callback: (r) => void submit(r.credential, false), ux_mode: 'popup' });
        google.accounts.id.renderButton(target.current, { theme: 'outline', size: 'large', text, width: 320, locale: 'vi' });
      })
      .catch(() => setError('Không tải được nút đăng nhập Google.'));
    return () => {
      cancelled = true;
    };
    // submit only reads setters and the onSignedIn prop, stable for the life of the page
  }, [clientId, text]);

  if (!clientId) return null;
  return (
    <div className="auth-google" data-testid="google-signin">
      <div className="auth-divider"><span>HOẶC</span></div>
      <div ref={target} className="auth-google-button" />
      {pending && (
        <div className="auth-google-consent" data-testid="google-consent">
          <label>
            <input type="checkbox" checked={accepted} onChange={(e) => setAccepted(e.target.checked)} />
            Tôi đồng ý với Điều khoản sử dụng và Chính sách xử lý dữ liệu cá nhân của ShopHub
          </label>
          <button type="button" className="auth-submit" disabled={!accepted || busy} onClick={() => void submit(pending, true)}>
            TẠO TÀI KHOẢN VỚI GOOGLE
          </button>
        </div>
      )}
      {error && <div className="auth-error" role="alert">{error}</div>}
    </div>
  );
};

export default GoogleSignIn;
