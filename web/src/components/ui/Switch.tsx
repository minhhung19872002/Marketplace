import type { ButtonHTMLAttributes } from 'react';
import './Switch.css';

interface SwitchProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'onChange' | 'role' | 'type'> {
  checked: boolean;
  onChange: (next: boolean) => void;
  /** accessible name when there is no visible <label> pointing at it */
  label?: string;
}

/** Toggle switch (G3): a real button with role="switch", so Space / Enter and focus work like any button. */
export const Switch = ({ checked, onChange, label, className, disabled, ...rest }: SwitchProps) => (
  <button
    {...rest}
    type="button"
    role="switch"
    aria-checked={checked}
    aria-label={label ?? rest['aria-label']}
    disabled={disabled}
    className={['sh-switch', checked && 'is-on', className].filter(Boolean).join(' ')}
    onClick={() => onChange(!checked)}
  >
    <span className="sh-switch__thumb" aria-hidden />
  </button>
);

export default Switch;
