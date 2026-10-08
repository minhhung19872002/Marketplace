import { useQuery } from '@tanstack/react-query';
import { marketingApi } from '../api/marketing';
import { BannerLink } from './Banner';
import CategoryIcon from './CategoryIcon';
import './CategoryShortcuts.css';

const TONES = ['primary', 'mall', 'amber', 'teal', 'violet', 'tech'];

// Quick links under the banner, managed by the platform (icon = image URL or an icon code, drawn as SVG)
const CategoryShortcuts = () => {
  const { data } = useQuery({ queryKey: ['home-banners'], queryFn: marketingApi.banners, staleTime: 60_000 });
  const shortcuts = data?.shortcuts ?? [];
  // Holds its height while loading (CLS)
  if (!data) return <section className="feature-shortcuts feature-shortcuts--loading" aria-hidden />;
  if (shortcuts.length === 0) return null;
  return (
    <section className="feature-shortcuts" aria-label="Lối tắt">
      <div className="feature-shortcuts-grid">
        {shortcuts.map((f, i) => (
          <BannerLink key={f.id} to={f.link} className="feature-shortcut" testId="feature-shortcut">
            <span className={`feature-shortcut-icon feature-shortcut-icon--${TONES[i % TONES.length]}`}>
              <CategoryIcon icon={f.imageUrl} size={24} />
            </span>
            <span className="feature-shortcut-label">{f.title}</span>
          </BannerLink>
        ))}
      </div>
    </section>
  );
};

export default CategoryShortcuts;
