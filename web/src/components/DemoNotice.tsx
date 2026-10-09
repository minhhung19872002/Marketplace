import { useQuery } from '@tanstack/react-query';
import { contentApi } from '../api/content';
import './DemoNotice.css';

const TEXT = 'Bản trình diễn: đơn hàng, thanh toán và vận chuyển đều là giả lập, không giao hàng thật.';

/**
 * Showcase notice while the platform runs as a demo (SITE.MODE = demo, G4-D), so nobody mistakes it for a real shop.
 * It never pushes the page down (a strip above the header did: CLS 0.12): `badge` sits in the header's top bar, whose
 * height is fixed, `line` in the footer. Nothing is shown on a live site.
 */
const DemoNotice = ({ variant }: { variant: 'badge' | 'line' }) => {
  const site = useQuery({ queryKey: ['site'], queryFn: contentApi.site, staleTime: 3_600_000 });
  if (site.data?.mode !== 'demo') return null;
  return variant === 'badge'
    ? <span className="demo-badge" title={TEXT} data-testid="demo-notice">Bản trình diễn</span>
    : <p className="demo-line" role="note" data-testid="demo-notice-footer">{TEXT}</p>;
};

export default DemoNotice;
