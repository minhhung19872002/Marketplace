import { beforeEach, describe, expect, it } from 'vitest';
import { toastFor, useToasts } from '../lib/toast';

// F4: every write the buyer makes says how it went, through one shared toast system
describe('toast', () => {
  beforeEach(() => useToasts.setState({ items: [] }));

  it('a write with a server message is a success toast; a failed write an error toast', () => {
    expect(toastFor('POST', '/account/addresses', true, 'Đã thêm địa chỉ.')).toEqual({ kind: 'success', text: 'Đã thêm địa chỉ.' });
    expect(toastFor('DELETE', '/wallet/bank-accounts/1', false, 'Không tìm thấy tài khoản.')).toEqual({ kind: 'error', text: 'Không tìm thấy tài khoản.' });
  });

  it('reads, silent writes and empty messages say nothing; sign-in forms show their own errors', () => {
    expect(toastFor('GET', '/orders', true, 'x')).toBeNull();
    expect(toastFor('POST', '/checkout/quote', false, 'x')).toBeNull();
    expect(toastFor('POST', '/products/abc/views?source=home', true, 'x')).toBeNull();
    expect(toastFor('PUT', '/cart/selection', true, '')).toBeNull();
    expect(toastFor('POST', '/auth/login', false, 'Sai mật khẩu')).toBeNull();
  });

  it('the store keeps the latest toasts and drops one by id', () => {
    const { push, dismiss } = useToasts.getState();
    const id = push('success', 'Đã lưu.');
    push('error', 'Lỗi.');
    expect(useToasts.getState().items.map((t) => t.text)).toEqual(['Đã lưu.', 'Lỗi.']);
    dismiss(id);
    expect(useToasts.getState().items.map((t) => t.text)).toEqual(['Lỗi.']);
  });
});
