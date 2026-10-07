import { useState } from 'react';

/**
 * "Chia sẻ" (II.4): copy the link, Facebook's share page, and the device's own share sheet (Zalo, Messenger… on phones)
 * where the browser offers one.
 */
const ShareProduct = ({ title }: { title: string }) => {
  const [copied, setCopied] = useState(false);
  const url = typeof window === 'undefined' ? '' : window.location.href.split('#')[0];
  const canShare = typeof navigator !== 'undefined' && typeof navigator.share === 'function';

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  };

  return (
    <div className="pd-share" data-testid="pd-share">
      <span>Chia sẻ:</span>
      <button type="button" onClick={() => void copy()} data-testid="share-copy">{copied ? 'Đã sao chép' : 'Sao chép liên kết'}</button>
      <a href={`https://www.facebook.com/sharer/sharer.php?u=${encodeURIComponent(url)}`} target="_blank" rel="noopener noreferrer"
        data-testid="share-facebook">Facebook</a>
      {canShare && (
        <button type="button" onClick={() => void navigator.share({ title, url }).catch(() => undefined)} data-testid="share-native">
          Zalo / ứng dụng khác
        </button>
      )}
    </div>
  );
};

export default ShareProduct;
