import { describe, expect, it } from 'vitest';
import { orderExportBody, orderListQuery, type OrderFilter } from '../api/orders';

// D6: the order list sends its date / carrier / payment filters, and "Xuất Excel" carries exactly the same ones
describe('order filters', () => {
  const filter: OrderFilter = { tab: 'ToShip', q: 'SH26', from: '2026-10-01T00:00:00.000Z', to: '2026-10-02T17:00:00.000Z', carrier: 'SPX_SIM', paymentMethod: 'Cod' };

  it('the list query has every filter that is set', () => {
    expect(Object.fromEntries(new URLSearchParams(orderListQuery(filter, 2, 20)))).toEqual({
      tab: 'ToShip', q: 'SH26', from: filter.from, to: filter.to, carrier: 'SPX_SIM', paymentMethod: 'Cod', page: '2', pageSize: '20',
    });
    expect(orderListQuery({ tab: 'All' }, 1, 20)).toBe('tab=All&page=1&pageSize=20');
  });

  it('the export body is the same filter, without paging', () => {
    const { page, pageSize, ...listed } = Object.fromEntries(new URLSearchParams(orderListQuery(filter, 1, 20)));
    expect([page, pageSize]).toEqual(['1', '20']);
    expect(orderExportBody(filter)).toEqual(listed);
  });
});
