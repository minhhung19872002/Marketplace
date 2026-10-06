import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import './Auth.css';

const Login = () => {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');

  const handleSubmit = (e) => {
    e.preventDefault();
    if (!username.trim() || !password.trim()) {
      setError('Vui lòng nhập tên đăng nhập và mật khẩu');
      return;
    }
    login(username.trim());
    navigate('/');
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
            <form onSubmit={handleSubmit} className="auth-form">
              {error && <div className="auth-error" data-testid="auth-error">{error}</div>}
              <input
                type="text"
                className="auth-input"
                placeholder="Email/Số điện thoại/Tên đăng nhập"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                aria-label="Tên đăng nhập"
              />
              <input
                type="password"
                className="auth-input"
                placeholder="Mật khẩu"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                aria-label="Mật khẩu"
              />
              <button type="submit" className="auth-submit" data-testid="login-submit">ĐĂNG NHẬP</button>
            </form>
            <div className="auth-links">
              <span>Quên mật khẩu</span>
              <span>Đăng nhập với SMS</span>
            </div>
            <div className="auth-divider"><span>HOẶC</span></div>
            <div className="auth-social">
              <button className="auth-social-btn facebook">f Facebook</button>
              <button className="auth-social-btn google">G Google</button>
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
