import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { notificationPrefsApi, type NotificationCategory, type NotificationChannel, type NotificationPref } from '../../api/chat';
import { ApiError } from '../../api/http';
import QueryState from '../../components/QueryState';

const CATEGORIES: { value: NotificationCategory; label: string }[] = [
  { value: 'Order', label: 'Cập nhật đơn hàng' },
  { value: 'Promotion', label: 'Khuyến mãi' },
  { value: 'Wallet', label: 'Ví & ShopHub Xu' },
  { value: 'Activity', label: 'Hoạt động (shop, tài khoản)' },
];
const CHANNELS: { value: NotificationChannel; label: string }[] = [
  { value: 'InApp', label: 'Trong app' },
  { value: 'Email', label: 'Email' },
  { value: 'Sms', label: 'SMS' },
  { value: 'Push', label: 'Thông báo đẩy' },
];

/** Per-category, per-channel notification switches (spec II.11). */
const NotificationSettingsPage = () => {
  const queryClient = useQueryClient();
  const prefs = useQuery({ queryKey: ['notification-prefs'], queryFn: notificationPrefsApi.get });
  const [rows, setRows] = useState<NotificationPref[]>([]);
  const [message, setMessage] = useState('');
  useEffect(() => {
    if (prefs.data) setRows(prefs.data.prefs);
  }, [prefs.data]);
  const save = useMutation({
    mutationFn: () => notificationPrefsApi.save(rows.map(({ category, channel, enabled }) => ({ category, channel, enabled }))),
    onSuccess: (r) => {
      setMessage(r.message);
      void queryClient.invalidateQueries({ queryKey: ['notification-prefs'] });
    },
    onError: (e) => setMessage(e instanceof ApiError ? e.message : 'Không lưu được cài đặt.'),
  });
  const find = (c: NotificationCategory, ch: NotificationChannel) => rows.find((r) => r.category === c && r.channel === ch);
  const toggle = (c: NotificationCategory, ch: NotificationChannel) =>
    setRows((list) => list.map((r) => (r.category === c && r.channel === ch && !r.locked ? { ...r, enabled: !r.enabled } : r)));

  return (
    <div className="account-card">
      <div className="account-card-head">
        <h1 className="account-card-title">Cài Đặt Thông Báo</h1>
        <p className="account-card-sub">Chọn kênh nhận thông báo cho từng loại. Thông báo đơn hàng, ví và tài khoản luôn hiện trong app; khuyến mãi có thể tắt.</p>
      </div>
      <QueryState query={prefs} loading={<div className="account-skeleton" aria-busy="true" />}>
        {(loaded) => (
        <>
          {!loaded.hasEmail && <p className="account-card-sub">Bạn chưa có email — thông báo qua email sẽ không được gửi.</p>}
          <table className="notif-prefs" data-testid="notification-prefs">
            <thead>
              <tr>
                <th>Loại thông báo</th>
                {CHANNELS.map((ch) => <th key={ch.value}>{ch.label}</th>)}
              </tr>
            </thead>
            <tbody>
              {CATEGORIES.map((c) => (
                <tr key={c.value}>
                  <td>{c.label}</td>
                  {CHANNELS.map((ch) => {
                    const p = find(c.value, ch.value);
                    return (
                      <td key={ch.value}>
                        <input type="checkbox" checked={p?.enabled ?? false} disabled={!p || p.locked}
                          onChange={() => toggle(c.value, ch.value)} aria-label={`${c.label} — ${ch.label}`} />
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
          <button className="account-btn-primary" onClick={() => save.mutate()} disabled={save.isPending}>Lưu</button>
          {message && <p className="account-card-sub" role="status">{message}</p>}
        </>
        )}
      </QueryState>
    </div>
  );
};

export default NotificationSettingsPage;
