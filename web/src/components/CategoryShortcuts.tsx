import { useQuery } from '@tanstack/react-query';
import { marketingApi } from '../api/marketing';
import { BannerLink } from './Banner';
import ShortcutIcon from './ShortcutIcon';
import Carousel from './ui/Carousel';
import './CategoryShortcuts.css';

// Quick links under the banner, managed by the platform (icon = image URL or an icon code): 45 px app-style tiles —
// white rounded square, light grey border, multi-colour glyph (G-VIS) — and a 13 px label of up to two lines
const CategoryShortcuts = () => {
  const { data } = useQuery({ queryKey: ['home-banners'], queryFn: marketingApi.banners, staleTime: 60_000 });
  const shortcuts = data?.shortcuts ?? [];
  // Holds its height while loading (CLS)
  if (!data) return <section className="feature-shortcuts feature-shortcuts--loading" aria-hidden />;
  if (shortcuts.length === 0) return null;
  return (
    <section className="feature-shortcuts" aria-label="Lối tắt">
      <Carousel label="Lối tắt" className="feature-shortcuts-grid">
        {shortcuts.map((f) => (
          <BannerLink key={f.id} to={f.link} className="feature-shortcut" testId="feature-shortcut">
            <span className="feature-shortcut-icon"><ShortcutIcon icon={f.imageUrl} /></span>
            <span className="feature-shortcut-label">{f.title}</span>
          </BannerLink>
        ))}
      </Carousel>
    </section>
  );
};

export default CategoryShortcuts;
