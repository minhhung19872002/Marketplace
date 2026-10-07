import { useEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import './ConfirmDialog.css';

interface DialogProps {
  message: ReactNode;
  confirmLabel?: string;
  onConfirm: () => void;
  onCancel: () => void;
}

/** The one "are you sure?" box of the buyer site (spec 6.5, F2): Esc / "Không" closes, focus starts on the safe choice. */
export const ConfirmDialog = ({ message, confirmLabel = 'Đồng ý', onConfirm, onCancel }: DialogProps) => {
  const cancelRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    cancelRef.current?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onCancel(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onCancel]);
  return createPortal(
    <div className="confirm-backdrop" onClick={(e) => { if (e.target === e.currentTarget) onCancel(); }}>
      <div className="confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby="confirm-dialog-message" data-testid="confirm-dialog">
        <p id="confirm-dialog-message" className="confirm-message">{message}</p>
        <div className="confirm-actions">
          <button ref={cancelRef} type="button" className="confirm-cancel" onClick={onCancel} data-testid="confirm-no">Không</button>
          <button type="button" className="confirm-ok" onClick={onConfirm} data-testid="confirm-yes">{confirmLabel}</button>
        </div>
      </div>
    </div>,
    document.body,
  );
};

interface ButtonProps {
  message: ReactNode;
  confirmLabel?: string;
  onConfirm: () => void;
  className?: string;
  disabled?: boolean;
  testId?: string;
  children: ReactNode;
}

/** A delete / cancel button that asks first — every "Xoá" / "Huỷ" of the buyer site goes through it (scanned). */
export const ConfirmButton = ({ message, confirmLabel, onConfirm, className, disabled, testId, children }: ButtonProps) => {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button type="button" className={className} disabled={disabled} onClick={() => setOpen(true)} data-testid={testId}>{children}</button>
      {open && (
        <ConfirmDialog message={message} confirmLabel={confirmLabel} onCancel={() => setOpen(false)}
          onConfirm={() => { setOpen(false); onConfirm(); }} />
      )}
    </>
  );
};
