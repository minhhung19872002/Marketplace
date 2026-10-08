import { useState } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { notificationsApi, type AppNotification, type NotificationCategory } from '../api/commerce';
import { useAuth } from '../context/AuthContext';
import { useUnreadNotifications } from '../context/NotificationsContext';
import { formatDateTime } from '../lib/datetime';
import QueryState from '../components/QueryState';
import './Notifications.css';
import { Bell, Gift, Package, Wallet, type LucideIcon } from 'lucide-react';

const TABS: { key: NotificationCategory | null; label: string }[] = [
  { key: null, label: 'Tất Cả' },
  { key: 'Order', label: 'Cập Nhật Đơn Hàng' },
  { key: 'Promotion', label: 'Khuyến Mãi' },
  { key: 'Wallet', label: 'Cập Nhật Ví' },
  { key: 'Activity', label: 'Hoạt Động' },
];

const ICONS: Record<NotificationCategory, LucideIcon> = { Order: Package, Promotion: Gift, Wallet: Wallet, Activity: Bell };

const Notifications = () => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { isLoggedIn, isChecking } = useAuth();
  const [tab, setTab] = useState<NotificationCategory | null>(null);
  const [page, setPage] = useState(1);
  const list = useQuery({
    queryKey: ['notifications', tab, page],
    queryFn: () => notificationsApi.list(tab, page),
    enabled: isLoggedIn,
    placeholderData: keepPreviousData,
  });
  const unread = useUnreadNotifications(isLoggedIn);

  if (isChecking) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (!isLoggedIn) return <Navigate to="/dang-nhap" replace state={{ from: '/thong-bao' }} />;

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['notifications'] });
  const open = async (n: AppNotification) => {
    if (!n.isRead) {
      await notificationsApi.read(n.id).catch(() => undefined);
      refresh();
    }
    // Seller-centre links are a different app (full page load)
    if (n.link?.startsWith('/seller/')) window.location.assign(n.link);
    else if (n.link) navigate(n.link);
  };
  const pages = list.data ? Math.max(1, Math.ceil(list.data.totalCount / list.data.pageSize)) : 1;

  return (
    <div className="noti-page">
      <div className="container">
        <div className="noti-head">
          <h1 className="noti-title">Thông Báo</h1>
          {(unread.data?.total ?? 0) > 0 && (
            <button className="noti-read-all" onClick={() => notificationsApi.readAll().then(refresh)} data-testid="noti-read-all">
              Đánh dấu đã đọc tất cả
            </button>
          )}
        </div>

        <div className="noti-tabs">
          {TABS.map((t) => {
            const count = t.key ? unread.data?.byCategory[t.key] ?? 0 : unread.data?.total ?? 0;
            return (
              <button key={t.label} className={`noti-tab ${tab === t.key ? 'active' : ''}`} onClick={() => { setTab(t.key); setPage(1); }}>
                {t.label}{count > 0 && <span className="noti-tab-count">{count}</span>}
              </button>
            );
          })}
        </div>

        <div className="noti-list" data-testid="noti-list">
          <QueryState query={list} isEmpty={(d) => d.items.length === 0}
            emptyText={<p className="noti-empty" data-testid="noti-empty">Chưa có thông báo nào.</p>}>
            {(d) => d.items.map((n) => (
            <button key={n.id} className={`noti-item ${n.isRead ? '' : 'unread'}`} onClick={() => open(n)} data-testid="noti-item">
              <span className="noti-icon">{(() => { const Icon = ICONS[n.category]; return <Icon size={22} aria-hidden />; })()}</span>
              <span className="noti-body">
                <span className="noti-item-title">{n.title}</span>
                <span className="noti-item-desc">{n.body}</span>
                <span className="noti-item-date">{formatDateTime(n.createdAt)}</span>
              </span>
            </button>
          ))}
          </QueryState>
        </div>

        {pages > 1 && (
          <div className="noti-foot">
            <button disabled={page <= 1} onClick={() => setPage(page - 1)}>‹</button>
            <span>{page}/{pages}</span>
            <button disabled={page >= pages} onClick={() => setPage(page + 1)}>›</button>
          </div>
        )}
        <div className="noti-foot">
          <Link to="/" className="noti-back">← Về trang chủ</Link>
        </div>
      </div>
    </div>
  );
};

export default Notifications;
