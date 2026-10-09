import { useState, type MouseEvent } from 'react';
import { Play } from 'lucide-react';
import ImageLightbox from './ImageLightbox';
import Carousel from './ui/Carousel';
import { handleImgError, imageSrcSet } from '../lib/image';
import './ProductGallery.css';

interface Props {
  images: string[];
  alt: string;
  active: number;
  onActive: (i: number) => void;
  videoUrl?: string;
}

/**
 * Product photos (G2-B2): the main photo magnifies under the pointer on desktop and opens the full-screen viewer on
 * click; the thumbnail row only shows when there is more than one photo (a lone thumbnail repeats the main photo).
 */
const ProductGallery = ({ images, alt, active, onActive, videoUrl }: Props) => {
  const [open, setOpen] = useState(false);
  const [lens, setLens] = useState<{ x: number; y: number } | null>(null);
  const [showVideo, setShowVideo] = useState(false);
  const current = images[Math.min(active, images.length - 1)] ?? images[0];

  const move = (e: MouseEvent<HTMLButtonElement>) => {
    const r = e.currentTarget.getBoundingClientRect();
    setLens({ x: ((e.clientX - r.left) / r.width) * 100, y: ((e.clientY - r.top) / r.height) * 100 });
  };

  const many = images.length > 1 || !!videoUrl;

  return (
    <div className="pd-gallery">
      {showVideo && videoUrl ? (
        <video className="pd-gallery-main pd-gallery-video" src={videoUrl} controls autoPlay preload="metadata" data-testid="pd-video">
          Trình duyệt không hỗ trợ video.
        </video>
      ) : (
        <button type="button" className={`pd-gallery-main ${lens ? 'is-zooming' : ''}`} onClick={() => setOpen(true)}
          onMouseMove={move} onMouseLeave={() => setLens(null)} aria-label="Phóng to ảnh" data-testid="pd-zoom">
          <img src={current} srcSet={imageSrcSet(current)} sizes="(max-width: 768px) 100vw, 450px" alt={alt} onError={handleImgError} data-testid="pd-main-image" width={600} height={600} />
          {lens && <span className="pd-gallery-lens" aria-hidden
            style={{ backgroundImage: `url("${current}")`, backgroundPosition: `${lens.x}% ${lens.y}%` }} />}
        </button>
      )}
      {open && <ImageLightbox images={images} index={Math.min(active, images.length - 1)} alt={alt} onIndex={onActive} onClose={() => setOpen(false)} />}

      {many && (
        <div className="pd-gallery-thumbs">
          <Carousel label="Ảnh sản phẩm" className="pd-gallery-strip">
            {videoUrl && (
              <button type="button" className={`pd-thumb pd-thumb-video ${showVideo ? 'active' : ''}`} onClick={() => setShowVideo(true)} aria-label="Xem video">
                <img src={images[0]} alt="" onError={handleImgError} />
                <span className="pd-thumb-play"><Play size={18} fill="currentColor" aria-hidden /></span>
              </button>
            )}
            {images.map((src, i) => (
              <button key={src} type="button" className={`pd-thumb ${!showVideo && i === active ? 'active' : ''}`}
                onMouseEnter={() => { setShowVideo(false); onActive(i); }} onClick={() => { setShowVideo(false); onActive(i); }}
                aria-label={`Ảnh ${i + 1}`} aria-current={!showVideo && i === active}>
                <img src={src} alt="" onError={handleImgError} loading="lazy" />
              </button>
            ))}
          </Carousel>
        </div>
      )}
    </div>
  );
};

export default ProductGallery;
