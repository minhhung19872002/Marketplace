import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import './Notifications.css';

const TABS = ['Cập Nhật Đơn Hàng', 'Khuyến Mãi', 'Cập Nhật Ví'];

const DATA = [
  { id: 1, tab: 0, icon: '🚚', title: 'Đơn hàng SH204819 đang giao', desc: 'Đơn hàng của bạn đang trên đường giao đến. Vui lòng chú ý điện thoại.', date: 'Hôm nay 09:24', unread: true },
  { id: 2, tab: 1, icon: '🎁', title: 'Voucher 50.000đ vừa được tặng', desc: 'Nhập mã SHOPHUB50 khi thanh toán để được giảm ngay 50.000đ cho đơn từ 0đ.', date: 'Hôm nay 08:10', unread: true },
  { id: 3, tab: 0, icon: '✅', title: 'Đơn hàng SH198233 đã giao thành công', desc: 'Cảm ơn bạn đã mua sắm. Đừng quên đánh giá sản phẩm để nhận ShopHub Xu nhé!', date: 'Hôm qua 17:45', unread: false },
  { id: 4, tab: 1, icon: '⚡', title: 'Flash Sale 12h đang diễn ra', desc: 'Hàng ngàn sản phẩm giảm đến 50%. Săn ngay kẻo lỡ!', date: 'Hôm qua 12:00', unread: false },
  { id: 5, tab: 2, icon: '💰', title: 'Hoàn tiền 12.000đ vào Ví ShopHub', desc: 'Bạn vừa nhận được 12.000đ hoàn tiền từ đơn hàng SH198233.', date: '2 ngày trước', unread: false },
];

const Notifications = () => {
  const [tab, setTab] = useState(-1); // -1 = tất cả

  const list = tab === -1 ? DATA : DATA.filter((n) => n.tab === tab);

  return (
    <div className="noti-page">
      <div className="container">
        <h1 className="noti-title">Thông Báo</h1>

        <div className="noti-tabs">
          <button className={`noti-tab ${tab === -1 ? 'active' : ''}`} onClick={() => setTab(-1)}>
            Tất Cả
          </button>
          {TABS.map((t, i) => (
            <button key={t} className={`noti-tab ${tab === i ? 'active' : ''}`} onClick={() => setTab(i)}>
              {t}
            </button>
          ))}
        </div>

        <div className="noti-list" data-testid="noti-list">
          {list.map((n) => (
            <div key={n.id} className={`noti-item ${n.unread ? 'unread' : ''}`} data-testid="noti-item">
              <span className="noti-icon">{n.icon}</span>
              <div className="noti-body">
                <div className="noti-item-title">{n.title}</div>
                <div className="noti-item-desc">{n.desc}</div>
                <div className="noti-item-date">{n.date}</div>
              </div>
            </div>
          ))}
        </div>

        <div className="noti-foot">
          <Link to="/" className="noti-back">← Về trang chủ</Link>
        </div>
      </div>
    </div>
  );
};

export default Notifications;
