import React, { useEffect, useMemo, useState } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { products, categories, removeTones } from '../data/products';
import ProductGrid from '../components/ProductGrid';
import './SearchResults.css';

const SORTS = [
  { key: 'relevant', label: 'Liên Quan' },
  { key: 'newest', label: 'Mới Nhất' },
  { key: 'sold', label: 'Bán Chạy' },
];

const SearchResults = () => {
  const [params] = useSearchParams();
  const q = params.get('q') || '';
  const category = params.get('category') || '';
  // ?sort=discount: "Xem tất cả" của Flash Sale -> giảm giá sâu nhất lên đầu
  const sortParam = params.get('sort') || 'relevant';

  const [sort, setSort] = useState(sortParam);
  const [priceSort, setPriceSort] = useState(''); // '', 'asc', 'desc'
  const [selectedCats, setSelectedCats] = useState(category ? [category] : []);
  const [minPrice, setMinPrice] = useState('');
  const [maxPrice, setMaxPrice] = useState('');
  const [minRating, setMinRating] = useState(0);

  // Đồng bộ bộ lọc khi URL đổi mà component không remount (breadcrumb, link danh mục khác)
  useEffect(() => {
    setSelectedCats(category ? [category] : []);
  }, [category]);

  useEffect(() => {
    setSort(sortParam);
    setPriceSort('');
  }, [sortParam]);

  const toggleCat = (id) => {
    setSelectedCats((prev) =>
      prev.includes(id) ? prev.filter((c) => c !== id) : [...prev, id]
    );
  };

  const clearFilters = () => {
    setSelectedCats([]);
    setMinPrice('');
    setMaxPrice('');
    setMinRating(0);
  };

  const results = useMemo(() => {
    let list = products;

    if (q) {
      const needle = removeTones(q.toLowerCase());
      list = list.filter((p) => removeTones(p.name.toLowerCase()).includes(needle));
    }
    if (selectedCats.length > 0) {
      list = list.filter((p) => selectedCats.includes(p.categoryId));
    }
    if (minPrice) list = list.filter((p) => p.price >= Number(minPrice));
    if (maxPrice) list = list.filter((p) => p.price <= Number(maxPrice));
    if (minRating > 0) list = list.filter((p) => p.rating >= minRating);

    const sorted = [...list];
    if (priceSort === 'asc') sorted.sort((a, b) => a.price - b.price);
    else if (priceSort === 'desc') sorted.sort((a, b) => b.price - a.price);
    else if (sort === 'newest') sorted.sort((a, b) => b.id - a.id);
    else if (sort === 'sold') sorted.sort((a, b) => b.sold - a.sold);
    else if (sort === 'discount') sorted.sort((a, b) => b.discount - a.discount);

    return sorted;
  }, [q, selectedCats, minPrice, maxPrice, minRating, sort, priceSort]);

  const categoryName = categories.find((c) => c.id === category)?.name;
  const heading = q
    ? `Kết quả tìm kiếm cho "${q}"`
    : categoryName
      ? `Danh mục: ${categoryName}`
      : sortParam === 'discount'
        ? 'Flash Sale - Giảm giá sốc'
        : 'Tất cả sản phẩm';

  return (
    <div className="search-results">
      <div className="container search-layout">
        {/* Sidebar bộ lọc */}
        <aside className="search-sidebar" data-testid="filter-sidebar">
          <div className="filter-title">
            <span>☰ BỘ LỌC TÌM KIẾM</span>
          </div>

          <div className="filter-group">
            <h4 className="filter-group-title">Theo Danh Mục</h4>
            <div className="filter-cats">
              {categories.slice(0, 10).map((c) => (
                <label key={c.id} className="filter-cat">
                  <input
                    type="checkbox"
                    checked={selectedCats.includes(c.id)}
                    onChange={() => toggleCat(c.id)}
                    data-testid="filter-cat"
                  />
                  <span>{c.icon} {c.name}</span>
                </label>
              ))}
            </div>
          </div>

          <div className="filter-group">
            <h4 className="filter-group-title">Khoảng Giá</h4>
            <div className="filter-price">
              <input
                type="number"
                placeholder="₫ TỪ"
                value={minPrice}
                onChange={(e) => setMinPrice(e.target.value)}
                aria-label="Giá từ"
              />
              <span className="filter-price-sep">—</span>
              <input
                type="number"
                placeholder="₫ ĐẾN"
                value={maxPrice}
                onChange={(e) => setMaxPrice(e.target.value)}
                aria-label="Giá đến"
              />
            </div>
          </div>

          <div className="filter-group">
            <h4 className="filter-group-title">Đánh Giá</h4>
            <div className="filter-ratings">
              {[5, 4, 3].map((r) => (
                <button
                  key={r}
                  className={`filter-rating ${minRating === r ? 'active' : ''}`}
                  onClick={() => setMinRating(minRating === r ? 0 : r)}
                  data-testid="filter-rating"
                >
                  <span className="filter-rating-stars">{'★'.repeat(r)}{'☆'.repeat(5 - r)}</span>
                  <span>trở lên</span>
                </button>
              ))}
            </div>
          </div>

          <button className="filter-clear" onClick={clearFilters} data-testid="filter-clear">
            XÓA TẤT CẢ
          </button>
        </aside>

        {/* Nội dung */}
        <div className="search-content">
          <div className="search-results-head">
            <h1 className="search-results-title" data-testid="search-heading">{heading}</h1>
            <span className="search-results-count">{results.length} sản phẩm</span>
          </div>

          <div className="search-sort-bar">
            <span className="search-sort-label">Sắp xếp theo</span>
            {SORTS.map((s) => (
              <button
                key={s.key}
                className={`search-sort-btn ${sort === s.key && !priceSort ? 'active' : ''}`}
                onClick={() => {
                  setSort(s.key);
                  setPriceSort('');
                }}
              >
                {s.label}
              </button>
            ))}
            <button
              className={`search-sort-btn search-sort-price ${priceSort ? 'active' : ''}`}
              onClick={() => setPriceSort((p) => (p === 'asc' ? 'desc' : 'asc'))}
            >
              Giá {priceSort === 'asc' ? '↑' : priceSort === 'desc' ? '↓' : '⇅'}
            </button>
          </div>

          {results.length === 0 ? (
            <div className="search-empty">
              <p>Không tìm thấy sản phẩm nào phù hợp.</p>
              <Link to="/" className="search-empty-btn">Về trang chủ</Link>
            </div>
          ) : (
            <ProductGrid title="" products={results} />
          )}
        </div>
      </div>
    </div>
  );
};

export default SearchResults;
