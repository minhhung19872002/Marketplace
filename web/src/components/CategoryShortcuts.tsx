import { useQuery } from '@tanstack/react-query';
import { marketingApi } from '../api/marketing';
import { handleImgError, isImageUrl } from '../lib/image';
import { BannerLink } from './Banner';
import './CategoryShortcuts.css';

const TONES = ['tone-mall', 'tone-primary', 'tone-amber', 'tone-orange', 'tone-teal', 'tone-tech', 'tone-violet'];

// Quick links under the banner, managed by the platform (icon = emoji or image)
const CategoryShortcuts = () => {
  const { data } = useQuery({ queryKey: ['home-banners'], queryFn: marketingApi.banners, staleTime: 60_000 });
  const shortcuts = data?.shortcuts ?? [];
  if (shortcuts.length === 0) return null;
  return (
    <section className="feature-shortcuts">
      <div className="feature-shortcuts-grid">
        {shortcuts.map((f, i) => (
          <BannerLink key={f.id} to={f.link} className="feature-shortcut" testId="feature-shortcut">
            <span className={`feature-shortcut-icon ${TONES[i % TONES.length]}`}>
              {isImageUrl(f.imageUrl) ? <img src={f.imageUrl} alt="" onError={handleImgError} /> : f.imageUrl}
            </span>
            <span className="feature-shortcut-label">{f.title}</span>
          </BannerLink>
        ))}
      </div>
    </section>
  );
};

export default CategoryShortcuts;
