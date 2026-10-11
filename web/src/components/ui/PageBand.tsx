import type { ReactNode } from 'react';
import { Search } from 'lucide-react';
import './PageBand.css';

/** The bright brand band at the top of a service page (G5-B): help, parcel tracking, app download. */
export const PageBand = ({ children, className }: { children: ReactNode; className?: string }) => (
  <div className={['sh-band', className].filter(Boolean).join(' ')}>
    <div className="container sh-band__inner">{children}</div>
  </div>
);

interface BandSearchProps {
  value: string;
  onChange: (value: string) => void;
  onSubmit: () => void;
  placeholder: string;
  label: string;
  button?: string;
  testId?: string;
}

/** A search box like the header's (white field, orange button) for a band. */
export const BandSearch = ({ value, onChange, onSubmit, placeholder, label, button = 'Tìm', testId }: BandSearchProps) => (
  <form className="sh-band__search" role="search" onSubmit={(e) => { e.preventDefault(); onSubmit(); }}>
    <input value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} aria-label={label} data-testid={testId} />
    {/* Phones show the icon only: the label keeps the button's name */}
    <button type="submit" aria-label={button}><Search size={18} aria-hidden /> <span>{button}</span></button>
  </form>
);
