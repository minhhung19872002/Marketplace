import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi } from '../../api/account';
import { formatDateTime } from '../../lib/datetime';
import QueryState from '../../components/QueryState';

const DevicesPage = () => {
  const queryClient = useQueryClient();
  const sessions = useQuery({ queryKey: ['sessions'], queryFn: accountApi.sessions });
  const revoke = useMutation({
    mutationFn: accountApi.revokeSession,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sessions'] }),
  });

  return (
    <div className="account-card">
      <div className="account-card-head">
        <h1 className="account-card-title">Thiết bị đăng nhập</h1>
        <p className="account-card-sub">Đăng xuất từ xa các thiết bị bạn không nhận ra.</p>
      </div>
      <QueryState query={sessions} loading={<div className="account-skeleton" aria-busy="true" />}>
        {(list) => (
      <ul className="device-list">
        {list.map((s) => (
          <li key={s.id} className="device-item" data-testid="device-item">
            <div>
              <div className="device-name">
                {s.device || 'Thiết bị không xác định'}
                {s.isCurrent && <span className="address-default">Thiết bị này</span>}
              </div>
              <div className="device-meta">
                IP {s.ip ?? '—'} · Đăng nhập {formatDateTime(s.signedInAt)} · Hoạt động {formatDateTime(s.lastActiveAt)}
              </div>
            </div>
            {!s.isCurrent && (
              <button className="account-btn-outline" onClick={() => revoke.mutate(s.id)} disabled={revoke.isPending}>
                Đăng xuất
              </button>
            )}
          </li>
        ))}
      </ul>
        )}
      </QueryState>
    </div>
  );
};

export default DevicesPage;
