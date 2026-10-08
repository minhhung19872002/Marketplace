import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { ChevronDown, Search } from 'lucide-react';
import { removeTones } from '../../lib/text';
import './ui.css';

export interface SearchSelectOption {
  value: string;
  label: string;
}

interface Props {
  value: string;
  options: SearchSelectOption[];
  onChange: (value: string) => void;
  placeholder: string;
  /** Accessible name of the field (also used by tests) */
  label: string;
  disabled?: boolean;
  testId?: string;
}

const fold = (s: string) => removeTones(s).toLowerCase();

/**
 * A select with a filter box (accent-insensitive: "ba dinh" finds "Phường Ba Đình"). Keyboard: ↑ ↓ to move, Enter to
 * pick, Esc to close. Long lists (≈ 100 wards of a province) stay usable without scrolling through them.
 */
const SearchSelect = ({ value, options, onChange, placeholder, label, disabled, testId }: Props) => {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const root = useRef<HTMLDivElement>(null);
  const listId = useId();
  const selected = options.find((o) => o.value === value);

  const shown = useMemo(() => {
    const q = fold(query.trim());
    return q ? options.filter((o) => fold(o.label).includes(q)) : options;
  }, [options, query]);

  useEffect(() => {
    if (!open) return undefined;
    const onDown = (e: MouseEvent) => {
      if (root.current && !root.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [open]);

  useEffect(() => setActive(0), [query, open]);

  const pick = (v: string) => {
    onChange(v);
    setOpen(false);
    setQuery('');
  };

  const onKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive((i) => Math.min(i + 1, shown.length - 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((i) => Math.max(i - 1, 0)); }
    else if (e.key === 'Enter') { e.preventDefault(); if (shown[active]) pick(shown[active].value); }
    else if (e.key === 'Escape') setOpen(false);
  };

  return (
    <div className={`sh-select ${open ? 'is-open' : ''}`} ref={root} data-testid={testId}>
      <button type="button" className="sh-select__trigger" onClick={() => setOpen((v) => !v)} disabled={disabled}
        aria-haspopup="listbox" aria-expanded={open} aria-label={`${label}: ${selected?.label ?? placeholder}`}>
        <span className={selected ? '' : 'sh-select__placeholder'}>{selected?.label ?? placeholder}</span>
        <ChevronDown size={16} aria-hidden />
      </button>
      {open && (
        <div className="sh-select__panel">
          <label className="sh-select__search">
            <Search size={14} aria-hidden />
            <input autoFocus value={query} onChange={(e) => setQuery(e.target.value)} onKeyDown={onKey}
              placeholder="Tìm nhanh…" aria-label={`Tìm ${label}`} aria-controls={listId} role="combobox" aria-expanded />
          </label>
          <ul className="sh-select__list" role="listbox" id={listId} aria-label={label}>
            {shown.length === 0 && <li className="sh-select__empty">Không có kết quả</li>}
            {shown.map((o, i) => (
              <li key={o.value} role="option" aria-selected={o.value === value}
                className={`sh-select__option ${i === active ? 'is-active' : ''} ${o.value === value ? 'is-selected' : ''}`}
                onMouseEnter={() => setActive(i)} onMouseDown={(e) => { e.preventDefault(); pick(o.value); }}>
                {o.label}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
};

export default SearchSelect;
