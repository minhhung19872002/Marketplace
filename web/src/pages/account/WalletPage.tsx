import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { walletApi } from '../../api/wallet';
import { ApiError } from '../../api/http';
import { formatPrice } from '../../lib/money';
import { formatDateTime } from '../../lib/datetime';
import { goTo } from '../../lib/navigation';
import { ConfirmButton } from '../../components/ConfirmDialog';
import QueryState from '../../components/QueryState';

type Panel = 'topup' | 'pin' | 'bank' | 'withdraw' | null;

const message = (e: unknown, fallback: string) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : fallback);

/** Sends the finance OTP to the user's own phone; shows the countdown before another code can be asked for. */
const OtpField = ({ value, onChange }: { value: string; onChange: (v: string) => void }) => {
  const [wait, setWait] = useState(0);
  const [note, setNote] = useState('');
  useEffect(() => {
    if (wait <= 0) return undefined;
    const t = setTimeout(() => setWait(wait - 1), 1000);
    return () => clearTimeout(t);
  }, [wait]);
  const send = async () => {
    try {
      const r = await walletApi.otp();
      setNote(r.message);
      setWait(r.data.resendAfterSeconds);
    } catch (e) {
      setNote(message(e, 'Không gửi được mã.'));
    }
  };
  return (
    <div className="wallet-otp">
      <input className="account-input" inputMode="numeric" maxLength={6} placeholder="Mã xác thực (OTP)" value={value}
        onChange={(e) => onChange(e.target.value.replace(/\D/g, ''))} data-testid="wallet-otp" />
      <button type="button" className="account-btn-outline" disabled={wait > 0} onClick={send} data-testid="wallet-otp-send">
        {wait > 0 ? `Gửi lại sau ${wait}s` : 'Gửi mã'}
      </button>
      {note && <small>{note}</small>}
    </div>
  );
};

/** /tai-khoan/vi — Ví ShopHub: balance (sum of its ledger), history, top-up via the gateway, PIN, bank accounts, withdrawals. */
const WalletPage = () => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [page, setPage] = useState(1);
  const [panel, setPanel] = useState<Panel>(null);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const [amount, setAmount] = useState('');
  const [gateway, setGateway] = useState<string | null>(null);
  const gateways = useQuery({ queryKey: ['topup-gateways'], queryFn: walletApi.topupGateways, enabled: panel === 'topup', staleTime: 600_000 });
  const [pin, setPin] = useState('');
  const [otp, setOtp] = useState('');
  const banks = useQuery({ queryKey: ['banks'], queryFn: walletApi.banks, staleTime: 3_600_000 });
  const bankName = (code: string) => banks.data?.find((b) => b.code === code)?.name ?? code;
  const [bank, setBank] = useState({ bankCode: '', accountNo: '', accountName: '' });
  // First bank of the catalogue preselected once it arrives
  useEffect(() => {
    if (!bank.bankCode && banks.data?.length) setBank((b) => ({ ...b, bankCode: banks.data[0].code }));
  }, [banks.data, bank.bankCode]);
  const [bankId, setBankId] = useState('');
  const wallet = useQuery({ queryKey: ['wallet', page], queryFn: () => walletApi.get(page), placeholderData: keepPreviousData, staleTime: 0 });
  const data = wallet.data;

  // Back from the gateway: show what the server recorded for that top-up (the webhook decides, not the redirect)
  const topupId = params.get('topup');
  const topup = useQuery({
    queryKey: ['topup', topupId],
    queryFn: () => walletApi.getTopup(topupId!),
    enabled: !!topupId,
    refetchInterval: (q) => (q.state.data?.status === 'Pending' ? 2000 : false),
  });
  useEffect(() => {
    if (topup.data?.status === 'Succeeded') void queryClient.invalidateQueries({ queryKey: ['wallet'] });
  }, [topup.data?.status, queryClient]);

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['wallet'] });
  const run = async (work: () => Promise<string>) => {
    setError('');
    setNotice('');
    try {
      setNotice(await work());
      setPanel(null);
      setPin('');
      setOtp('');
      setAmount('');
      refresh();
    } catch (e) {
      setError(message(e, 'Không thực hiện được, vui lòng thử lại.'));
    }
  };

  if (!data) return <QueryState query={wallet}>{() => null}</QueryState>;
  const pages = Math.max(1, Math.ceil(data.history.totalCount / data.history.pageSize));
  const money = Number(amount) || 0;

  return (
    <div className="account-card" data-testid="wallet-page">
      <h2 className="account-title">Ví ShopHub</h2>
      {topup.data && (
        <div className="order-detail-alert" data-testid="topup-status">
          {topup.data.status === 'Succeeded' ? `Đã nạp ${formatPrice(topup.data.amount)} vào ví.`
            : topup.data.status === 'Pending' ? 'Đang chờ cổng thanh toán xác nhận…' : 'Nạp tiền không thành công.'}
          <button className="account-btn-outline" onClick={() => setParams({})}>Đóng</button>
        </div>
      )}
      <div className="wallet-balance">
        <div>
          <span>Số dư</span>
          <strong data-testid="wallet-balance">{formatPrice(data.balance)}</strong>
          {data.pendingWithdrawals > 0 && <small>Đang rút: {formatPrice(data.pendingWithdrawals)}</small>}
        </div>
        <div className="wallet-actions">
          <button className="account-btn" onClick={() => setPanel('topup')} data-testid="wallet-topup">Nạp tiền</button>
          <button className="account-btn-outline" onClick={() => setPanel('withdraw')} disabled={!data.hasPin || data.bankAccounts.length === 0}
            data-testid="wallet-withdraw">Rút về ngân hàng</button>
          <button className="account-btn-outline" onClick={() => setPanel('pin')} data-testid="wallet-pin-open">
            {data.hasPin ? 'Đổi mật khẩu ví' : 'Tạo mật khẩu ví'}
          </button>
          <button className="account-btn-outline" onClick={() => setPanel('bank')} data-testid="wallet-bank-open">Thêm ngân hàng</button>
        </div>
      </div>
      {!data.hasPin && <p className="account-empty">Tạo mật khẩu ví (6 số) để thanh toán đơn hàng bằng Ví ShopHub và rút tiền.</p>}
      {data.locked && <p className="account-error">Ví đang tạm khoá do nhập sai mật khẩu nhiều lần, vui lòng thử lại sau 15 phút.</p>}
      {notice && <div className="account-message" role="status" data-testid="wallet-notice">{notice}</div>}
      {error && <div className="account-error" role="alert" data-testid="wallet-error">{error}</div>}

      {panel === 'topup' && (
        <div className="account-form wallet-panel">
          <label className="account-label" htmlFor="topup-amount">Số tiền nạp (₫)</label>
          <input id="topup-amount" className="account-input" inputMode="numeric" value={amount}
            onChange={(e) => setAmount(e.target.value.replace(/\D/g, ''))} data-testid="topup-amount" />
          {(gateways.data?.length ?? 0) > 1 && (
            <div className="account-radios" role="radiogroup" aria-label="Cổng thanh toán">
              {gateways.data!.map((g) => (
                <label key={g.method} className="account-radio">
                  <input type="radio" name="topup-gateway" checked={(gateway ?? gateways.data![0].method) === g.method}
                    onChange={() => setGateway(g.method)} data-testid={`topup-gateway-${g.method}`} />
                  {g.name}
                </label>
              ))}
            </div>
          )}
          <button className="account-btn" disabled={money <= 0 || gateways.data?.length === 0} data-testid="topup-submit" onClick={async () => {
            setError('');
            try {
              const r = await walletApi.topup(money, gateway ?? undefined);
              goTo(navigate, r.redirectUrl);
            } catch (e) {
              setError(message(e, 'Không nạp được tiền.'));
            }
          }}>Thanh toán qua cổng</button>
        </div>
      )}

      {panel === 'pin' && (
        <div className="account-form wallet-panel">
          <OtpField value={otp} onChange={setOtp} />
          <label className="account-label" htmlFor="new-pin">Mật khẩu ví mới (6 số)</label>
          <input id="new-pin" className="account-input" type="password" inputMode="numeric" maxLength={6} autoComplete="off" value={pin}
            onChange={(e) => setPin(e.target.value.replace(/\D/g, ''))} data-testid="new-pin" />
          <button className="account-btn" disabled={otp.length !== 6 || pin.length !== 6} data-testid="pin-submit"
            onClick={() => run(async () => (await walletApi.setPin(otp, pin)).message)}>Lưu mật khẩu ví</button>
        </div>
      )}

      {panel === 'bank' && (
        <div className="account-form wallet-panel">
          <label className="account-label" htmlFor="bank-code">Ngân hàng</label>
          <select id="bank-code" className="account-input" value={bank.bankCode} onChange={(e) => setBank({ ...bank, bankCode: e.target.value })}>
            {(banks.data ?? []).map((b) => <option key={b.code} value={b.code}>{b.name}</option>)}
          </select>
          <label className="account-label" htmlFor="bank-no">Số tài khoản</label>
          <input id="bank-no" className="account-input" inputMode="numeric" value={bank.accountNo}
            onChange={(e) => setBank({ ...bank, accountNo: e.target.value.replace(/\D/g, '') })} data-testid="bank-no" />
          <label className="account-label" htmlFor="bank-name">Tên chủ tài khoản</label>
          <input id="bank-name" className="account-input" value={bank.accountName} onChange={(e) => setBank({ ...bank, accountName: e.target.value })}
            data-testid="bank-name" />
          <OtpField value={otp} onChange={setOtp} />
          <button className="account-btn" disabled={otp.length !== 6 || !bank.accountNo || !bank.accountName.trim()} data-testid="bank-submit"
            onClick={() => run(async () => (await walletApi.addBank({ ...bank, otpCode: otp })).message)}>Thêm tài khoản</button>
        </div>
      )}

      {panel === 'withdraw' && (
        <div className="account-form wallet-panel">
          <label className="account-label" htmlFor="withdraw-bank">Tài khoản nhận</label>
          <select id="withdraw-bank" className="account-input" value={bankId} onChange={(e) => setBankId(e.target.value)} data-testid="withdraw-bank">
            <option value="">— Chọn tài khoản —</option>
            {data.bankAccounts.map((b) => <option key={b.id} value={b.id}>{b.bankCode} ***{b.accountNoLast4} · {b.accountName}</option>)}
          </select>
          <label className="account-label" htmlFor="withdraw-amount">Số tiền (₫)</label>
          <input id="withdraw-amount" className="account-input" inputMode="numeric" value={amount}
            onChange={(e) => setAmount(e.target.value.replace(/\D/g, ''))} data-testid="withdraw-amount" />
          <label className="account-label" htmlFor="withdraw-pin">Mật khẩu ví</label>
          <input id="withdraw-pin" className="account-input" type="password" inputMode="numeric" maxLength={6} autoComplete="off" value={pin}
            onChange={(e) => setPin(e.target.value.replace(/\D/g, ''))} data-testid="withdraw-pin" />
          <button className="account-btn" disabled={!bankId || money <= 0 || pin.length !== 6} data-testid="withdraw-submit"
            onClick={() => run(async () => (await walletApi.withdraw(bankId, money, pin)).message)}>Rút tiền</button>
        </div>
      )}

      {data.bankAccounts.length > 0 && (
        <>
          <h3 className="account-subtitle">Tài khoản ngân hàng</h3>
          <ul className="wallet-banks">
            {data.bankAccounts.map((b) => (
              <li key={b.id}>
                {bankName(b.bankCode)} ***{b.accountNoLast4} · {b.accountName}
                <ConfirmButton className="account-btn-outline" message={`Xoá tài khoản ${bankName(b.bankCode)} ***${b.accountNoLast4}?`} confirmLabel="Xoá"
                  onConfirm={() => run(async () => (await walletApi.removeBank(b.id)).message)} testId="wallet-bank-remove">Xoá</ConfirmButton>
              </li>
            ))}
          </ul>
        </>
      )}

      {data.withdrawals.length > 0 && (
        <>
          <h3 className="account-subtitle">Lệnh rút tiền</h3>
          <table className="wallet-table">
            <tbody>
              {data.withdrawals.map((w) => (
                <tr key={w.id} data-testid="wallet-withdrawal">
                  <td>{formatDateTime(w.createdAt)}</td>
                  <td>{w.bankCode} ***{w.accountLast4}</td>
                  <td>{formatPrice(w.amount)}</td>
                  <td>{w.statusLabel}{w.rejectReason && ` — ${w.rejectReason}`}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}

      <h3 className="account-subtitle">Lịch sử giao dịch</h3>
      {data.history.items.length === 0 ? <p className="account-empty">Chưa có giao dịch.</p> : (
        <table className="wallet-table">
          <tbody>
            {data.history.items.map((e) => (
              <tr key={e.id} data-testid="wallet-entry">
                <td>{formatDateTime(e.postedAt)}</td>
                <td>{e.description}</td>
                <td className={e.direction === 'Credit' ? 'wallet-in' : 'wallet-out'}>
                  {e.direction === 'Credit' ? '+' : '−'}{formatPrice(e.amount)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {pages > 1 && (
        <div className="account-pager">
          <button disabled={page <= 1} onClick={() => setPage(page - 1)}>‹</button>
          <span>{page}/{pages}</span>
          <button disabled={page >= pages} onClick={() => setPage(page + 1)}>›</button>
        </div>
      )}
    </div>
  );
};

export default WalletPage;
