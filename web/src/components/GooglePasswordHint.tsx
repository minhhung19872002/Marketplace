import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { accountApi } from '../api/account';

/**
 * An account made by "Đăng nhập Google" has a password nobody knows (L143): changing it or deleting the account asks
 * for it, so point to "Quên mật khẩu", which sets one through a code sent to the account's e-mail.
 */
const GooglePasswordHint = () => {
  const me = useQuery({ queryKey: ['me'], queryFn: accountApi.me });
  if (!me.data?.hasGoogle) return null;
  return (
    <div className="account-notice" data-testid="google-password-hint">
      Bạn đăng nhập bằng Google? Nếu chưa từng đặt mật khẩu, hãy{' '}
      <Link to="/quen-mat-khau">đặt mật khẩu qua mã gửi tới email</Link> rồi quay lại đây.
    </div>
  );
};

export default GooglePasswordHint;
