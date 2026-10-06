import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import './Auth.css';

const Register = () => {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [username, setUsername] = useState('');
  const [phone, setPhone] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');

  const handleSubmit = (e) => {
    e.preventDefault();
    if (!username.trim() || !phone.trim() || !password.trim()) {
      setError('Vui lòng điền đầy đủ thông tin');
      return;
    }
    // Đăng ký xong tự đăng nhập
    login(username.trim());
    navigate('/');
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
            <form onSubmit={handleSubmit} className="auth-form">
              {error && <div className="auth-error" data-testid="auth-error">{error}</div>}
              <input
                type="text"
                className="auth-input"
                placeholder="Tên đăng nhập"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                aria-label="Tên đăng nhập"
              />
              <input
                type="tel"
                className="auth-input"
                placeholder="Số điện thoại"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                aria-label="Số điện thoại"
              />
              <input
                type="password"
                className="auth-input"
                placeholder="Mật khẩu"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                aria-label="Mật khẩu"
              />
              <button type="submit" className="auth-submit" data-testid="register-submit">ĐĂNG KÝ</button>
            </form>
            <div className="auth-terms">
              Bằng việc đăng ký, bạn đồng ý với ShopHub về Điều khoản dịch vụ &amp; Chính sách bảo mật.
            </div>
            <div className="auth-divider"><span>HOẶC</span></div>
            <div className="auth-social">
              <button className="auth-social-btn facebook">f Facebook</button>
              <button className="auth-social-btn google">G Google</button>
            </div>
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
