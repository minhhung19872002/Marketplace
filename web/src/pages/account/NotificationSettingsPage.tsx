import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { notificationPrefsApi, type NotificationCategory, type NotificationChannel, type NotificationPref } from '../../api/chat';
import { ApiError } from '../../api/http';
import QueryState from '../../components/QueryState';
import { Switch } from '../../components/ui/Switch';

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
        <h1 className="account-card-title">Cài đặt thông báo</h1>
        <p className="account-card-sub">Chọn kênh nhận thông báo cho từng loại. Thông báo đơn hàng, ví và tài khoản luôn hiện trong app; khuyến mãi có thể tắt.</p>
      </div>
      <QueryState query={prefs} loading={<div className="account-skeleton" aria-busy="true" />}>
        {(loaded) => (
        <>
          {!loaded.hasEmail && <p className="account-card-sub">Bạn chưa có email — thông báo qua email sẽ không được gửi.</p>}
          <div data-testid="notification-prefs">
            <table className="notif-prefs notif-prefs-table">
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
                          <Switch checked={p?.enabled ?? false} disabled={!p || p.locked} onChange={() => toggle(c.value, ch.value)}
                            label={`${c.label} — ${ch.label}`} title={p?.locked ? 'Luôn bật' : undefined} />
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
            {/* Phones: one card per notification type, one switch row per channel */}
            <ul className="notif-prefs-cards">
              {CATEGORIES.map((c) => (
                <li key={c.value} className="notif-prefs-card">
                  <h2>{c.label}</h2>
                  {CHANNELS.map((ch) => {
                    const p = find(c.value, ch.value);
                    const id = `notif-${c.value}-${ch.value}`;
                    return (
                      <div key={ch.value} className="notif-prefs-row">
                        <label htmlFor={id}>
                          {ch.label}
                          {p?.locked && <small>Luôn bật</small>}
                        </label>
                        <Switch id={id} checked={p?.enabled ?? false} disabled={!p || p.locked} onChange={() => toggle(c.value, ch.value)} />
                      </div>
                    );
                  })}
                </li>
              ))}
            </ul>
          </div>
          <button className="account-btn-primary" onClick={() => save.mutate()} disabled={save.isPending}>Lưu</button>
          {message && <p className="account-card-sub" role="status">{message}</p>}
        </>
        )}
      </QueryState>
    </div>
  );
};

export default NotificationSettingsPage;
