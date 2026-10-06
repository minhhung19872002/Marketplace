import { useEffect, useRef, useState } from 'react';
import { uploadMedia } from '../api/aftercare';
import { accountApi } from '../api/account';
import { ApiError } from '../api/http';
import { useAuthStore } from '../stores/auth';

const OUTPUT = 512;
const PREVIEW = 240;
const MAX_BYTES = 1024 * 1024;

/** The signed-in user's avatar: their photo, or the first letter of their name. */
export const UserAvatar = ({ className }: { className: string }) => {
  const user = useAuthStore((s) => s.user);
  if (!user) return null;
  return user.avatarUrl
    ? <img className={className} src={user.avatarUrl} alt="" />
    : <span className={className}>{user.fullName.charAt(0).toUpperCase()}</span>;
};

const toBlob = (canvas: HTMLCanvasElement, quality: number) =>
  new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));

/**
 * Hồ sơ → ảnh đại diện (spec I.2: cắt ảnh, ≤ 1 MB): pick a photo, zoom and move the square, the browser crops it to
 * 512 × 512 JPEG under 1 MB; the server checks the bytes again and strips EXIF / GPS.
 */
const AvatarEditor = ({ onSaved }: { onSaved: (message: string) => void }) => {
  const user = useAuthStore((s) => s.user);
  const setUser = useAuthStore((s) => s.setUser);
  const [image, setImage] = useState<HTMLImageElement | null>(null);
  const [zoom, setZoom] = useState(1);
  const [x, setX] = useState(50);
  const [y, setY] = useState(50);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const preview = useRef<HTMLCanvasElement>(null);

  // The square of the source image that ends up in the avatar, from zoom and the two position sliders (0–100)
  const crop = (img: HTMLImageElement) => {
    const side = Math.min(img.naturalWidth, img.naturalHeight) / zoom;
    return { side, sx: (img.naturalWidth - side) * (x / 100), sy: (img.naturalHeight - side) * (y / 100) };
  };

  useEffect(() => {
    const canvas = preview.current;
    if (!canvas || !image) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const { side, sx, sy } = crop(image);
    ctx.clearRect(0, 0, PREVIEW, PREVIEW);
    ctx.drawImage(image, sx, sy, side, side, 0, 0, PREVIEW, PREVIEW);
  });

  const pick = (file: File | undefined) => {
    setError('');
    if (!file) return;
    if (!file.type.startsWith('image/')) {
      setError('Vui lòng chọn một tệp ảnh.');
      return;
    }
    // A data: URL, not blob: — the site's CSP allows images from data: only
    const reader = new FileReader();
    reader.onload = () => {
      const img = new Image();
      img.onload = () => { setImage(img); setZoom(1); setX(50); setY(50); };
      img.onerror = () => setError('Không đọc được ảnh này.');
      img.src = String(reader.result);
    };
    reader.onerror = () => setError('Không đọc được ảnh này.');
    reader.readAsDataURL(file);
  };

  const save = async () => {
    if (!image || !user) return;
    setBusy(true);
    setError('');
    try {
      const canvas = document.createElement('canvas');
      canvas.width = OUTPUT;
      canvas.height = OUTPUT;
      const { side, sx, sy } = crop(image);
      canvas.getContext('2d')?.drawImage(image, sx, sy, side, side, 0, 0, OUTPUT, OUTPUT);
      let blob: Blob | null = null;
      for (const quality of [0.9, 0.75, 0.6]) {
        blob = await toBlob(canvas, quality);
        if (blob && blob.size <= MAX_BYTES) break;
      }
      if (!blob || blob.size > MAX_BYTES) throw new ApiError(400, 'Ảnh sau khi cắt vẫn lớn hơn 1 MB.');
      const asset = await uploadMedia('avatar', new File([blob], 'avatar.jpg', { type: 'image/jpeg' }));
      const res = await accountApi.setAvatar(asset.id);
      setUser({ ...user, avatarUrl: res.data });
      setImage(null);
      onSaved(res.message);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Không lưu được ảnh đại diện.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="avatar-editor">
      {image ? (
        <canvas ref={preview} width={PREVIEW} height={PREVIEW} className="avatar-crop" aria-label="Xem trước ảnh đại diện" />
      ) : (
        <UserAvatar className="avatar-current" />
      )}
      {image && (
        <div className="avatar-controls">
          <label>Phóng to <input type="range" min={1} max={3} step={0.05} value={zoom} onChange={(e) => setZoom(Number(e.target.value))} /></label>
          <label>Ngang <input type="range" min={0} max={100} value={x} onChange={(e) => setX(Number(e.target.value))} /></label>
          <label>Dọc <input type="range" min={0} max={100} value={y} onChange={(e) => setY(Number(e.target.value))} /></label>
        </div>
      )}
      <div className="account-actions">
        <label className="account-btn-outline avatar-pick">
          Chọn ảnh
          <input type="file" accept="image/jpeg,image/png,image/webp" hidden data-testid="avatar-file" onChange={(e) => pick(e.target.files?.[0])} />
        </label>
        {image && (
          <button type="button" className="account-btn-primary" onClick={() => void save()} disabled={busy} data-testid="avatar-save">
            {busy ? 'Đang lưu…' : 'Lưu ảnh'}
          </button>
        )}
      </div>
      <small className="account-card-sub">Dung lượng tối đa 1 MB · JPG, PNG, WebP</small>
      {error && <div className="account-error" role="alert">{error}</div>}
    </div>
  );
};

export default AvatarEditor;
