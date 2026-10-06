import { Link } from 'react-router-dom';
import { featureShortcuts } from '../data/home';
import './CategoryShortcuts.css';

// Quick links under the banner (each opens a real search / category)
const CategoryShortcuts = () => (
  <section className="feature-shortcuts">
    <div className="feature-shortcuts-grid">
      {featureShortcuts.map((f) => (
        <Link key={f.id} to={f.to} className="feature-shortcut" data-testid="feature-shortcut">
          <span className={`feature-shortcut-icon ${f.tone}`}>{f.icon}</span>
          <span className="feature-shortcut-label">{f.label}</span>
        </Link>
      ))}
    </div>
  </section>
);

export default CategoryShortcuts;
