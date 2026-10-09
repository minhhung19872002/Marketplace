import { useEffect, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi, type Gender } from '../../api/account';
import { ApiError } from '../../api/http';
import { formatDate } from '../../lib/datetime';
import { useCountdown } from '../../lib/useCountdown';
import { useAuthStore } from '../../stores/auth';
import AvatarEditor from '../../components/AvatarEditor';
import QueryState from '../../components/QueryState';

const GENDERS: { value: Gender; label: string }[] = [
  { value: 'Male', label: 'Nam' },
  { value: 'Female', label: 'Nữ' },
  { value: 'Other', label: 'Khác' },
];

const errorText = (err: unknown) => (err instanceof ApiError ? err.fieldErrors[0]?.message ?? err.message : 'Đã có lỗi xảy ra.');

const ProfilePage = () => {
  const queryClient = useQueryClient();
  const setUser = useAuthStore((s) => s.setUser);
  const me = useQuery({ queryKey: ['me'], queryFn: accountApi.me });

  const [fullName, setFullName] = useState('');
  const [gender, setGender] = useState<Gender | null>(null);
  const [dateOfBirth, setDateOfBirth] = useState('');
  const [notice, setNotice] = useState('');

  useEffect(() => {
    if (!me.data) return;
    setFullName(me.data.fullName);
    setGender(me.data.gender);
    setDateOfBirth(me.data.dateOfBirth ?? '');
  }, [me.data]);

  const save = useMutation({
    mutationFn: () => accountApi.updateProfile({ fullName: fullName.trim(), gender, dateOfBirth: dateOfBirth || null }),
    onSuccess: async (res) => {
      setNotice(res.message);
      const fresh = await queryClient.fetchQuery({ queryKey: ['me'], queryFn: accountApi.me });
      const current = useAuthStore.getState().user;
      if (current) setUser({ ...current, fullName: fresh.fullName });
    },
  });

  // Change phone/email: OTP goes to the NEW contact
  const [newContact, setNewContact] = useState('');
  const [contactCode, setContactCode] = useState('');
  const [codeSent, setCodeSent] = useState(false);
  const [resendIn, startResend] = useCountdown();
  const sendContactCode = useMutation({
    mutationFn: () => accountApi.requestContactChange(newContact.trim()),
    onSuccess: (issued) => {
      setCodeSent(true);
      startResend(issued.resendAfterSeconds);
    },
  });
  const confirmContact = useMutation({
    mutationFn: () => accountApi.confirmContactChange(newContact.trim(), contactCode.trim()),
    onSuccess: (res) => {
      setNotice(res.message);
      setCodeSent(false);
      setNewContact('');
      setContactCode('');
      void queryClient.invalidateQueries({ queryKey: ['me'] });
    },
  });

  if (me.isPending) return <div className="account-card account-skeleton" aria-busy="true" />;
  // Not loaded: the error and "Thử lại" (F3)
  if (me.isError || !me.data) return <div className="account-card"><QueryState query={me}>{() => null}</QueryState></div>;

  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setNotice('');
    save.mutate();
  };

  return (
    <>
      <div className="account-card">
        <div className="account-card-head">
          <h1 className="account-card-title">Hồ sơ của tôi</h1>
          <p className="account-card-sub">Quản lý thông tin hồ sơ để bảo mật tài khoản</p>
        </div>
        {notice && <div className="account-notice" role="status">{notice}</div>}
        <div className="account-profile-grid">
        <form className="account-form" onSubmit={onSubmit}>
          <label className="account-row">
            <span className="account-label">Họ và tên</span>
            <input className="account-input" value={fullName} onChange={(e) => setFullName(e.target.value)} aria-label="Họ và tên" />
          </label>
          <div className="account-row">
            <span className="account-label">Số điện thoại</span>
            <span className="account-value">{me.data.phone ?? 'Chưa có'}</span>
          </div>
          <div className="account-row">
            <span className="account-label">Email</span>
            <span className="account-value">{me.data.email ?? 'Chưa có'}</span>
          </div>
          <div className="account-row">
            <span className="account-label">Giới tính</span>
            <div className="account-radios">
              {GENDERS.map((g) => (
                <label key={g.value} className="account-radio">
                  <input type="radio" name="gender" checked={gender === g.value} onChange={() => setGender(g.value)} />
                  {g.label}
                </label>
              ))}
            </div>
          </div>
          <label className="account-row">
            <span className="account-label">Ngày sinh</span>
            <input
              type="date"
              className="account-input"
              value={dateOfBirth}
              onChange={(e) => setDateOfBirth(e.target.value)}
              aria-label="Ngày sinh"
            />
          </label>
          <div className="account-row">
            <span className="account-label">Thành viên từ</span>
            <span className="account-value">{formatDate(me.data.createdAt)}</span>
          </div>
          {save.isError && <div className="account-error">{errorText(save.error)}</div>}
          <div className="account-row">
            <span className="account-label" />
            <button type="submit" className="account-btn-primary" disabled={save.isPending} data-testid="profile-save">
              Lưu
            </button>
          </div>
        </form>
        <AvatarEditor onSaved={setNotice} />
        </div>
      </div>

      <div className="account-card">
        <div className="account-card-head">
          <h2 className="account-card-title">Đổi số điện thoại / email</h2>
          <p className="account-card-sub">Mã xác thực sẽ được gửi tới số điện thoại hoặc email mới</p>
        </div>
        <div className="account-form">
          <label className="account-row">
            <span className="account-label">SĐT / Email mới</span>
            <input className="account-input" value={newContact} onChange={(e) => setNewContact(e.target.value)} aria-label="Liên hệ mới" />
          </label>
          {codeSent && (
            <label className="account-row">
              <span className="account-label">Mã xác thực</span>
              <input
                className="account-input"
                inputMode="numeric"
                maxLength={6}
                value={contactCode}
                onChange={(e) => setContactCode(e.target.value.replace(/\D/g, ''))}
                aria-label="Mã xác thực liên hệ"
              />
            </label>
          )}
          {(sendContactCode.isError || confirmContact.isError) && (
            <div className="account-error">{errorText(sendContactCode.error ?? confirmContact.error)}</div>
          )}
          <div className="account-row">
            <span className="account-label" />
            <div className="account-actions">
              <button
                type="button"
                className="account-btn-outline"
                disabled={!newContact.trim() || resendIn > 0 || sendContactCode.isPending}
                onClick={() => sendContactCode.mutate()}
              >
                {resendIn > 0 ? `Gửi lại (${resendIn}s)` : 'Gửi mã'}
              </button>
              {codeSent && (
                <button
                  type="button"
                  className="account-btn-primary"
                  disabled={contactCode.length !== 6 || confirmContact.isPending}
                  onClick={() => confirmContact.mutate()}
                >
                  Xác nhận
                </button>
              )}
            </div>
          </div>
        </div>
      </div>
    </>
  );
};

export default ProfilePage;
