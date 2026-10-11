import type { ReactNode } from 'react';
import { Percent, Ticket as TicketIcon, Truck } from 'lucide-react';
import './Ticket.css';

interface TicketProps {
  /** The coloured stub on the left: an icon and a short label (who gives it, or FREESHIP) */
  stub: ReactNode;
  /** Free-shipping vouchers take the green stub, as in the voucher picker */
  freeship?: boolean;
  /** The "Lưu" button / link on the right */
  action?: ReactNode;
  className?: string;
  testId?: string;
  children: ReactNode;
}

/** A voucher as a ticket (G5-B): coloured stub with notches, the conditions, the action — campaign and shop pages. */
export const Ticket = ({ stub, freeship = false, action, className, testId, children }: TicketProps) => (
  <div className={['sh-ticket', freeship ? 'is-freeship' : '', className].filter(Boolean).join(' ')} data-testid={testId}>
    <div className="sh-ticket__stub" aria-hidden>{stub}</div>
    <div className="sh-ticket__body">{children}</div>
    {action && <div className="sh-ticket__action">{action}</div>}
  </div>
);

/** The stub of a voucher of the given type: its icon over a short label. */
export const VoucherStub = ({ type, label }: { type: string; label: string }) => {
  const Icon = type === 'FreeShipping' ? Truck : type === 'Percent' ? Percent : TicketIcon;
  return (
    <>
      <Icon size={24} />
      <span className="sh-ticket__label">{type === 'FreeShipping' ? 'FREESHIP' : label}</span>
    </>
  );
};
