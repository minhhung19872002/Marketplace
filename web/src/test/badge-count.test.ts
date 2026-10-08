import { describe, expect, it } from 'vitest';
import { badgeCount } from '../lib/text';

describe('badgeCount', () => {
  it('caps the header counters at 99+', () => {
    expect(badgeCount(7)).toBe('7');
    expect(badgeCount(99)).toBe('99');
    expect(badgeCount(390)).toBe('99+');
  });
});
