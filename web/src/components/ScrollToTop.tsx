import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { notePath } from '../lib/navigation';

// Cuộn lên đầu trang mỗi khi đổi route
const ScrollToTop = () => {
  const { pathname } = useLocation();
  useEffect(() => {
    window.scrollTo(0, 0);
    notePath(pathname);
  }, [pathname]);
  return null;
};

export default ScrollToTop;
