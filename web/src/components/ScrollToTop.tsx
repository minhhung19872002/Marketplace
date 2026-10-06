import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

// Cuộn lên đầu trang mỗi khi đổi route
const ScrollToTop = () => {
  const { pathname } = useLocation();
  useEffect(() => {
    window.scrollTo(0, 0);
  }, [pathname]);
  return null;
};

export default ScrollToTop;
