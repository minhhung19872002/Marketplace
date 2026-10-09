import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { contentApi } from '../api/content';
import { toast } from '../lib/toast';
import { Check, Facebook, Link2, Share2 } from 'lucide-react';

const ZALO_SDK = 'https://sp.zalo.me/plugins/sdk.js';

declare global {
  interface Window {
    ZaloSocialSDK?: { reload: () => void };
  }
}

/** Loads Zalo's social SDK once; it turns every ".zalo-share-button" into the official share button. */
const useZaloSdk = (enabled: boolean) => {
  useEffect(() => {
    if (!enabled) return;
    if (window.ZaloSocialSDK) {
      window.ZaloSocialSDK.reload();
      return;
    }
    if (document.querySelector(`script[src="${ZALO_SDK}"]`)) return;
    const script = document.createElement('script');
    script.src = ZALO_SDK;
    script.async = true;
    document.body.appendChild(script);
  }, [enabled]);
};

/**
 * "Chia sẻ" (II.4): copy the link, Facebook's share page, Zalo's official share button (needs the platform's Zalo
 * Official Account — SITE.ZALO_OA_ID; hidden without one, E4) and the device's own share sheet where the browser has one.
 */
const ShareProduct = ({ title }: { title: string }) => {
  const [copied, setCopied] = useState(false);
  const site = useQuery({ queryKey: ['site'], queryFn: contentApi.site, staleTime: 3_600_000 });
  const zaloOaId = site.data?.zaloOaId ?? null;
  useZaloSdk(!!zaloOaId);
  const url = typeof window === 'undefined' ? '' : window.location.href.split('#')[0];
  const canShare = typeof navigator !== 'undefined' && typeof navigator.share === 'function';

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      toast.success('Đã sao chép liên kết');
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
      toast.error('Không sao chép được liên kết, vui lòng thử lại.');
    }
  };

  return (
    <div className="pd-share" data-testid="pd-share">
      <span className="pd-share-label">Chia sẻ:</span>
      {/* Round icon buttons (G3-C7); the label is the accessible name and the hover tooltip */}
      <button type="button" className={`pd-share-btn pd-share-copy ${copied ? 'is-done' : ''}`} onClick={() => void copy()} data-testid="share-copy"
        aria-label={copied ? 'Đã sao chép liên kết' : 'Sao chép liên kết'} title={copied ? 'Đã sao chép liên kết' : 'Sao chép liên kết'}>
        {copied ? <Check size={16} aria-hidden /> : <Link2 size={16} aria-hidden />}
      </button>
      <a className="pd-share-btn pd-share-facebook" href={`https://www.facebook.com/sharer/sharer.php?u=${encodeURIComponent(url)}`} target="_blank"
        rel="noopener noreferrer" aria-label="Chia sẻ lên Facebook" title="Chia sẻ lên Facebook" data-testid="share-facebook">
        <Facebook size={16} fill="currentColor" strokeWidth={0} aria-hidden />
      </a>
      {zaloOaId && (
        <div className="zalo-share-button" data-href={url} data-oaid={zaloOaId} data-layout="1" data-color="blue" data-customize="false"
          data-testid="share-zalo" />
      )}
      {canShare && (
        <button type="button" className="pd-share-btn" onClick={() => void navigator.share({ title, url }).catch(() => undefined)} data-testid="share-native"
          aria-label="Chia sẻ qua ứng dụng khác" title="Chia sẻ qua ứng dụng khác">
          <Share2 size={16} aria-hidden />
        </button>
      )}
    </div>
  );
};

export default ShareProduct;
