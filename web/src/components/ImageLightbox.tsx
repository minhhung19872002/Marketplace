import { useEffect, useRef } from 'react';
import { handleImgError } from '../lib/image';
import './ImageLightbox.css';
import { X } from 'lucide-react';

interface Props {
  images: string[];
  index: number;
  alt: string;
  onIndex: (i: number) => void;
  onClose: () => void;
}

/** Full-screen photo viewer (II.4, E4): arrows / keyboard / swipe to move, Esc or the backdrop to close. */
const ImageLightbox = ({ images, index, alt, onIndex, onClose }: Props) => {
  const touchX = useRef<number | null>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const count = images.length;
  const go = (delta: number) => onIndex((index + delta + count) % count);

  useEffect(() => {
    closeRef.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      else if (e.key === 'ArrowRight') onIndex((index + 1) % count);
      else if (e.key === 'ArrowLeft') onIndex((index - 1 + count) % count);
    };
    document.addEventListener('keydown', onKey);
    const overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = overflow;
    };
  }, [index, count, onIndex, onClose]);

  return (
    <div className="lightbox" role="dialog" aria-modal="true" aria-label={`Ảnh ${index + 1}/${count}: ${alt}`} data-testid="lightbox"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
      onTouchStart={(e) => { touchX.current = e.touches[0]?.clientX ?? null; }}
      onTouchEnd={(e) => {
        const start = touchX.current;
        const end = e.changedTouches[0]?.clientX;
        touchX.current = null;
        if (start != null && end != null && Math.abs(end - start) > 40 && count > 1) go(end < start ? 1 : -1);
      }}>
      <button ref={closeRef} type="button" className="lightbox-close" onClick={onClose} aria-label="Đóng" data-testid="lightbox-close"><X size={22} aria-hidden /></button>
      {count > 1 && <button type="button" className="lightbox-nav lightbox-prev" onClick={() => go(-1)} aria-label="Ảnh trước">‹</button>}
      <img className="lightbox-image" src={images[index]} alt={alt} onError={handleImgError} data-testid="lightbox-image" />
      {count > 1 && <button type="button" className="lightbox-nav lightbox-next" onClick={() => go(1)} aria-label="Ảnh sau" data-testid="lightbox-next">›</button>}
      {count > 1 && <div className="lightbox-count">{index + 1} / {count}</div>}
    </div>
  );
};

export default ImageLightbox;
