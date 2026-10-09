import { useQuery } from '@tanstack/react-query';
import { Info } from 'lucide-react';
import { contentApi } from '../api/content';
import './DemoNotice.css';

/**
 * Thin strip above the header while the platform runs as a showcase (SITE.MODE = demo, G4-D): orders, payments and
 * deliveries are simulated, so nobody mistakes the demo for a real shop. Nothing is shown on a live site.
 */
const DemoNotice = () => {
  const site = useQuery({ queryKey: ['site'], queryFn: contentApi.site, staleTime: 3_600_000 });
  if (site.data?.mode !== 'demo') return null;
  return (
    <div className="demo-notice" role="note" data-testid="demo-notice">
      <Info size={14} aria-hidden />
      <span>Bản trình diễn: đơn hàng, thanh toán và vận chuyển đều là giả lập, không giao hàng thật.</span>
    </div>
  );
};

export default DemoNotice;
