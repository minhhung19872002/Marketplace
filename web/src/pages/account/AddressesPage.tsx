import { lazy, Suspense, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi, type Address, type AddressInput, type AddressType } from '../../api/account';
import { ApiError } from '../../api/http';
import { ConfirmButton } from '../../components/ConfirmDialog';
import QueryState from '../../components/QueryState';
import SearchSelect from '../../components/ui/SearchSelect';
import { TriangleAlert } from 'lucide-react';

// Leaflet only loads when someone opens the map
const MapPin = lazy(() => import('../../components/MapPin'));

const EMPTY: AddressInput = {
  receiverName: '',
  phone: '',
  provinceCode: '',
  wardCode: '',
  street: '',
  type: 'Home',
  isDefault: false,
  lat: null,
  lng: null,
};

/** Add / edit one address — also used inline on the checkout page (II.7), which then picks the new address. */
export const AddressForm = ({ initial, onDone }: { initial: Address | null; onDone: (saved?: Address) => void }) => {
  const queryClient = useQueryClient();
  // An address saved on units that no longer exist starts with the province / ward to re-pick
  const [form, setForm] = useState<AddressInput>(initial
    ? { receiverName: initial.receiverName, phone: initial.phone, provinceCode: initial.needsUpdate ? '' : initial.provinceCode,
        wardCode: initial.needsUpdate ? '' : initial.wardCode, street: initial.street, type: initial.type, isDefault: initial.isDefault,
        lat: initial.lat, lng: initial.lng }
    : EMPTY);
  const [showMap, setShowMap] = useState(false);
  const set = <K extends keyof AddressInput>(key: K, value: AddressInput[K]) => setForm((f) => ({ ...f, [key]: value }));

  const provinces = useQuery({ queryKey: ['divisions', ''], queryFn: () => accountApi.divisions(), staleTime: Infinity });
  const wards = useQuery({
    queryKey: ['divisions', form.provinceCode],
    queryFn: () => accountApi.divisions(form.provinceCode),
    enabled: Boolean(form.provinceCode),
    staleTime: Infinity,
  });

  const save = useMutation({
    mutationFn: () => (initial ? accountApi.updateAddress(initial.id, form) : accountApi.createAddress(form)),
    onSuccess: async (result) => {
      await queryClient.invalidateQueries({ queryKey: ['addresses'] });
      onDone(result.data);
    },
  });
  const errors = save.error instanceof ApiError ? save.error.fieldErrors.map((f) => f.message) : [];
  const errorMessage = save.isError
    ? errors.length > 0 ? errors.join(' ') : save.error instanceof ApiError ? save.error.message : 'Đã có lỗi xảy ra.'
    : '';

  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <form className="account-form address-form" onSubmit={onSubmit} data-testid="address-form">
      <h2 className="account-card-title">{initial ? 'Cập nhật địa chỉ' : 'Địa chỉ mới'}</h2>
      <div className="address-grid">
        <input className="account-input" placeholder="Họ và tên" value={form.receiverName}
          onChange={(e) => set('receiverName', e.target.value)} aria-label="Tên người nhận" />
        <input className="account-input" placeholder="Số điện thoại" value={form.phone}
          onChange={(e) => set('phone', e.target.value)} aria-label="Số điện thoại người nhận" />
      </div>
      {initial?.needsUpdate && (
        <div className="address-legacy-note" role="note">
          <TriangleAlert size={16} aria-hidden /> Địa chỉ này theo đơn vị hành chính cũ ({[initial.wardName, initial.districtName, initial.provinceName].filter(Boolean).join(', ')}).
          Vui lòng chọn lại Tỉnh/Thành phố và Phường/Xã theo danh mục mới.
        </div>
      )}
      {/* Two levels since 2025-07-01: 34 provinces → wards */}
      <div className="address-grid">
        <SearchSelect label="Tỉnh/Thành phố" placeholder="Tỉnh/Thành phố" value={form.provinceCode} testId="address-province"
          options={(provinces.data ?? []).map((p) => ({ value: p.code, label: p.name }))}
          onChange={(v) => setForm((f) => ({ ...f, provinceCode: v, wardCode: '' }))} />
        <SearchSelect label="Phường/Xã" placeholder="Phường/Xã" value={form.wardCode} testId="address-ward" disabled={!form.provinceCode}
          options={(wards.data ?? []).map((w) => ({ value: w.code, label: w.name }))} onChange={(v) => set('wardCode', v)} />
      </div>
      <input className="account-input" placeholder="Địa chỉ cụ thể (số nhà, tên đường)" value={form.street}
        onChange={(e) => set('street', e.target.value)} aria-label="Địa chỉ cụ thể" />
      <div className="address-pin">
        <button type="button" className="account-btn-outline" onClick={() => setShowMap((v) => !v)} data-testid="address-pin-toggle">
          {showMap ? 'Ẩn bản đồ' : form.lat != null ? 'Sửa vị trí đã ghim' : 'Ghim vị trí trên bản đồ (tuỳ chọn)'}
        </button>
        {form.lat != null && form.lng != null && (
          <span className="address-pin-coords" data-testid="address-pin-coords">
            Đã ghim {form.lat.toFixed(5)}, {form.lng.toFixed(5)}
            <button type="button" className="account-btn-outline address-pin-clear" onClick={() => setForm((f) => ({ ...f, lat: null, lng: null }))}>Bỏ ghim</button>
          </span>
        )}
        {showMap && (
          <Suspense fallback={<div className="map-pin-loading">Đang tải bản đồ…</div>}>
            <MapPin lat={form.lat} lng={form.lng} onPick={(lat, lng) => setForm((f) => ({ ...f, lat, lng }))} />
          </Suspense>
        )}
      </div>
      <div className="account-radios">
        {(['Home', 'Office'] as AddressType[]).map((t) => (
          <label key={t} className="account-radio">
            <input type="radio" name="address-type" checked={form.type === t} onChange={() => set('type', t)} />
            {t === 'Home' ? 'Nhà riêng' : 'Văn phòng'}
          </label>
        ))}
      </div>
      <label className="account-radio">
        <input type="checkbox" checked={form.isDefault} onChange={(e) => set('isDefault', e.target.checked)} />
        Đặt làm địa chỉ mặc định
      </label>
      {errorMessage && <div className="account-error" role="alert">{errorMessage}</div>}
      <div className="account-actions">
        <button type="button" className="account-btn-outline" onClick={() => onDone()}>Trở lại</button>
        <button type="submit" className="account-btn-primary" disabled={save.isPending} data-testid="address-save">Hoàn thành</button>
      </div>
    </form>
  );
};

const AddressesPage = () => {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<Address | 'new' | null>(null);
  const addresses = useQuery({ queryKey: ['addresses'], queryFn: accountApi.addresses });

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['addresses'] });
  const setDefault = useMutation({ mutationFn: accountApi.setDefaultAddress, onSuccess: refresh });
  const remove = useMutation({ mutationFn: accountApi.deleteAddress, onSuccess: refresh });

  return (
    <div className="account-card">
      <div className="account-card-head account-card-head-row">
        <h1 className="account-card-title">Địa chỉ của tôi</h1>
        {!editing && (
          <button className="account-btn-primary" onClick={() => setEditing('new')} data-testid="address-add">
            + Thêm địa chỉ mới
          </button>
        )}
      </div>

      {editing && <AddressForm initial={editing === 'new' ? null : editing} onDone={() => setEditing(null)} />}

      <QueryState query={addresses} loading={<div className="account-skeleton" aria-busy="true" />}
        isEmpty={(d) => d.length === 0 && !editing} emptyText={<div className="account-empty">Bạn chưa có địa chỉ nào.</div>}>
        {(list) => (
      <ul className="address-list">
        {list.map((a) => (
          <li key={a.id} className="address-item" data-testid="address-item">
            <div className="address-main">
              <div>
                <strong>{a.receiverName}</strong> <span className="address-phone">| {a.phone}</span>
              </div>
              <div className="address-line">{a.street}</div>
              <div className="address-line">{[a.wardName, a.districtName, a.provinceName].filter(Boolean).join(', ')}</div>
              {a.isDefault && <span className="address-default">Mặc định</span>}
              {a.needsUpdate && (
                <span className="address-legacy" data-testid="address-needs-update">
                  <TriangleAlert size={12} aria-hidden /> Cần cập nhật theo đơn vị hành chính mới
                </span>
              )}
            </div>
            <div className="address-actions">
              <button className="account-link" onClick={() => setEditing(a)}>Cập nhật</button>
              {!a.isDefault && (
                <>
                  <ConfirmButton className="account-link" message="Xoá địa chỉ này khỏi sổ địa chỉ?" confirmLabel="Xoá" onConfirm={() => remove.mutate(a.id)}
                    testId="address-remove">Xoá</ConfirmButton>
                  <button className="account-btn-outline" onClick={() => setDefault.mutate(a.id)}>Thiết lập mặc định</button>
                </>
              )}
            </div>
          </li>
        ))}
      </ul>
        )}
      </QueryState>
    </div>
  );
};

export default AddressesPage;
