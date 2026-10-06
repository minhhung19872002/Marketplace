import React from 'react';
import { Link, useLocation } from 'react-router-dom';
import { formatPrice } from '../data/products';
import './OrderSuccess.css';

const OrderSuccess = () => {
  const { state } = useLocation();
  const total = state?.total;
  const count = state?.count;
  const orderCode = 'SH' + String(Math.abs(((total || 0) * 7 + (count || 0) * 13) % 1000000)).padStart(6, '0');

  return (
    <div className="order-success">
      <div className="container">
        <div className="order-success-card" data-testid="order-success">
          <div className="order-success-icon">✓</div>
          <h1 className="order-success-title">Đặt Hàng Thành Công!</h1>
          <p className="order-success-sub">Cảm ơn bạn đã mua sắm tại ShopHub. Đơn hàng đang được xử lý.</p>

          <div className="order-success-info">
            <div className="order-success-row">
              <span>Mã đơn hàng</span>
              <strong>{orderCode}</strong>
            </div>
            {count != null && (
              <div className="order-success-row">
                <span>Số sản phẩm</span>
                <strong>{count}</strong>
              </div>
            )}
            {total != null && (
              <div className="order-success-row">
                <span>Tổng thanh toán</span>
                <strong className="order-success-total">{formatPrice(total)}</strong>
              </div>
            )}
            <div className="order-success-row">
              <span>Dự kiến giao</span>
              <strong>2 - 3 ngày tới</strong>
            </div>
          </div>

          <div className="order-success-actions">
            <Link to="/" className="order-success-btn-outline">Về Trang Chủ</Link>
            <Link to="/" className="order-success-btn">Tiếp Tục Mua Sắm</Link>
          </div>
        </div>
      </div>
    </div>
  );
};

export default OrderSuccess;
