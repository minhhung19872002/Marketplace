import { useEffect, useState } from 'react'
import { Alert, App, Button, Card, Dropdown, Empty, Input, Select, Space, Tag, Typography, Upload } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { designApi, type BlockType, type Decoration, type DecorationBlockInput } from '../api/design'
import { uploadMedia, type MediaAsset } from '../api/seller'
import ProductPicker, { type PickedProduct } from '../components/ProductPicker'
import { formatDateTime } from '../lib/datetime'

interface EditorImage { key: string; asset?: MediaAsset; url?: string; link: string }

interface EditorBlock {
  key: string
  type: BlockType
  title: string
  images: EditorImage[]
  products: PickedProduct[]
  shopCategoryId: string | null
  video: { asset?: MediaAsset; url?: string } | null
  text: string
}

const TYPE_LABELS: Record<BlockType, string> = {
  Banner: 'Banner',
  Products: 'Sản phẩm nổi bật',
  Category: 'Danh mục của shop',
  Video: 'Video',
  Text: 'Đoạn chữ',
}

let seq = 0
const newKey = () => `b${++seq}`

const fromServer = (d: Decoration): EditorBlock[] => {
  const products = new Map(d.products.map((p) => [p.id, p]))
  return d.blocks.map((b) => ({
    key: newKey(),
    type: b.type,
    title: b.title ?? '',
    images: (b.images ?? []).map((i) => ({ key: newKey(), url: i.url, link: i.link ?? '' })),
    products: (b.productIds ?? []).map((id) => ({ id, name: products.get(id)?.name ?? 'Sản phẩm đã xoá', imageUrl: products.get(id)?.imageUrl ?? null })),
    shopCategoryId: b.shopCategoryId,
    video: b.videoUrl ? { url: b.videoUrl } : null,
    text: b.text ?? '',
  }))
}

const toInput = (b: EditorBlock): DecorationBlockInput => ({
  type: b.type,
  title: b.title.trim() || null,
  images: b.type === 'Banner' ? b.images.map((i) => ({ assetId: i.asset?.id, url: i.asset ? undefined : i.url, link: i.link.trim() || null })) : undefined,
  productIds: b.type === 'Products' ? b.products.map((p) => p.id) : undefined,
  shopCategoryId: b.type === 'Category' ? b.shopCategoryId : undefined,
  videoAssetId: b.type === 'Video' ? b.video?.asset?.id : undefined,
  videoUrl: b.type === 'Video' && !b.video?.asset ? b.video?.url ?? null : undefined,
  text: b.type === 'Text' ? b.text : undefined,
})

/** Thiết lập shop → Trang trí shop: the blocks of the shop's "Dạo" tab, arranged by drag and drop. */
const DecorationPage = ({ shopId, shopSlug }: { shopId: string; shopSlug: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const decoration = useQuery({ queryKey: ['decoration', shopId], queryFn: () => designApi.decoration(shopId) })
  const categories = useQuery({ queryKey: ['shop-categories', shopId], queryFn: () => designApi.categories(shopId) })
  const [blocks, setBlocks] = useState<EditorBlock[]>([])
  const [dirty, setDirty] = useState(false)
  const [dragging, setDragging] = useState<string | null>(null)
  const [picking, setPicking] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)

  useEffect(() => {
    if (decoration.data) { setBlocks(fromServer(decoration.data)); setDirty(false) }
  }, [decoration.data])

  const save = useMutation({
    mutationFn: () => designApi.saveDecoration(shopId, blocks.map(toInput)),
    onSuccess: (r) => { message.success(r.message); void queryClient.invalidateQueries({ queryKey: ['decoration', shopId] }) },
    onError: (e) => message.error(e instanceof ApiError ? e.message : 'Không đăng được trang trí.'),
  })

  const update = (key: string, patch: Partial<EditorBlock>) => {
    setBlocks((cur) => cur.map((b) => (b.key === key ? { ...b, ...patch } : b)))
    setDirty(true)
  }
  const move = (from: number, to: number) => {
    if (to < 0 || to >= blocks.length || from === to) return
    setBlocks((cur) => {
      const next = [...cur]
      const [item] = next.splice(from, 1)
      next.splice(to, 0, item)
      return next
    })
    setDirty(true)
  }
  const add = (type: BlockType) => {
    setBlocks((cur) => [...cur, { key: newKey(), type, title: '', images: [], products: [], shopCategoryId: null, video: null, text: '' }])
    setDirty(true)
  }
  const upload = async (file: File, kind: 'Image' | 'Video') => {
    setUploading(true)
    try {
      const asset = await uploadMedia('shop', file)
      if (asset.kind !== kind) throw new ApiError(400, kind === 'Video' ? 'Hãy chọn một tệp video MP4.' : 'Hãy chọn một ảnh.')
      return asset
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : 'Tải tệp thất bại.')
      return null
    } finally {
      setUploading(false)
    }
  }

  if (decoration.isError) return <Alert type="error" showIcon message={decoration.error instanceof ApiError ? decoration.error.message : 'Không tải được.'} />
  const limits = decoration.data
  const picked = blocks.find((b) => b.key === picking)

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%', maxWidth: 900 }}>
      <Card
        title="Trang trí shop"
        extra={
          <Space>
            <a href={`/shop/${shopSlug}?tab=dao`} target="_blank" rel="noreferrer">Xem trang shop ↗</a>
            <Dropdown disabled={!limits || blocks.length >= limits.maxBlocks}
              menu={{ items: (Object.keys(TYPE_LABELS) as BlockType[]).map((t) => ({ key: t, label: TYPE_LABELS[t] })), onClick: (e) => add(e.key as BlockType) }}>
              <Button data-testid="block-add">+ Thêm khối</Button>
            </Dropdown>
            <Button type="primary" onClick={() => save.mutate()} loading={save.isPending} disabled={!dirty || uploading} data-testid="decoration-save">
              Đăng
            </Button>
          </Space>
        }
      >
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
          Các khối hiện ở tab "Dạo" của trang shop theo đúng thứ tự dưới đây. Kéo thả để đổi thứ tự (hoặc dùng nút ↑ ↓).
          {limits && ` Tối đa ${limits.maxBlocks} khối.`}
          {limits?.publishedAt && ` Đăng lần cuối ${formatDateTime(limits.publishedAt)}.`}
        </Typography.Paragraph>
      </Card>

      {blocks.length === 0 && !decoration.isLoading && <Card><Empty description="Chưa có khối nào — trang shop đang hiện tab Tất cả sản phẩm." /></Card>}

      {blocks.map((b, i) => (
        <Card
          key={b.key}
          size="small"
          data-testid="decoration-block"
          draggable
          onDragStart={() => setDragging(b.key)}
          onDragOver={(e) => e.preventDefault()}
          onDrop={() => { if (dragging) move(blocks.findIndex((x) => x.key === dragging), i); setDragging(null) }}
          onDragEnd={() => setDragging(null)}
          style={{ cursor: 'grab', opacity: dragging === b.key ? 0.5 : 1 }}
          title={<Space><span aria-hidden>⠿</span><Tag>{i + 1}</Tag>{TYPE_LABELS[b.type]}</Space>}
          extra={
            <Space>
              <Button size="small" onClick={() => move(i, i - 1)} disabled={i === 0} aria-label="Lên">↑</Button>
              <Button size="small" onClick={() => move(i, i + 1)} disabled={i === blocks.length - 1} aria-label="Xuống">↓</Button>
              <Button size="small" danger onClick={() => { setBlocks(blocks.filter((x) => x.key !== b.key)); setDirty(true) }} data-confirm="local">Xoá</Button>
            </Space>
          }
        >
          <Space direction="vertical" style={{ width: '100%' }}>
            {b.type !== 'Banner' && (
              <Input placeholder={b.type === 'Category' ? 'Tiêu đề (để trống = tên danh mục)' : 'Tiêu đề (không bắt buộc)'} maxLength={60}
                value={b.title} onChange={(e) => update(b.key, { title: e.target.value })} />
            )}

            {b.type === 'Banner' && (
              <>
                {b.images.map((img, n) => (
                  <Space key={img.key} align="center" wrap>
                    <img src={img.asset?.thumbnailUrl ?? img.asset?.url ?? img.url} alt={`Ảnh ${n + 1}`} style={{ width: 160, height: 60, objectFit: 'cover' }} />
                    <Input placeholder="Liên kết trong ShopHub, vd. /tim-kiem?q=ao" style={{ width: 300 }} value={img.link}
                      onChange={(e) => update(b.key, { images: b.images.map((x) => (x.key === img.key ? { ...x, link: e.target.value } : x)) })} />
                    <Button size="small" onClick={() => update(b.key, { images: b.images.filter((x) => x.key !== img.key) })}>Bỏ ảnh</Button>
                  </Space>
                ))}
                {(!limits || b.images.length < limits.maxBannerImages) && (
                  <Upload accept="image/*" showUploadList={false} beforeUpload={async (file) => {
                    const asset = await upload(file, 'Image')
                    if (asset) update(b.key, { images: [...b.images, { key: newKey(), asset, link: '' }] })
                    return false
                  }}>
                    <Button loading={uploading} data-testid="banner-upload">+ Ảnh banner (ngang, ~1200 × 400)</Button>
                  </Upload>
                )}
              </>
            )}

            {b.type === 'Products' && (
              <>
                <Space wrap>{b.products.map((p) => <Tag key={p.id}>{p.name}</Tag>)}</Space>
                <Button onClick={() => setPicking(b.key)} data-testid="block-pick-products">Chọn sản phẩm ({b.products.length}/{limits?.maxProducts ?? 12})</Button>
              </>
            )}

            {b.type === 'Category' && (
              <Select placeholder="Chọn danh mục của shop" style={{ width: 320 }} value={b.shopCategoryId ?? undefined}
                onChange={(v: string) => update(b.key, { shopCategoryId: v })} notFoundContent="Chưa có danh mục — tạo ở mục Danh mục của shop"
                options={(categories.data ?? []).map((c) => ({ value: c.id, label: `${c.name} (${c.productCount})${c.isVisible ? '' : ' — đang ẩn'}` }))} />
            )}

            {b.type === 'Video' && (
              <Space wrap>
                {(b.video?.asset?.url ?? b.video?.url) && <video src={b.video?.asset?.url ?? b.video?.url} style={{ width: 240 }} controls preload="metadata" />}
                <Upload accept="video/mp4" showUploadList={false} beforeUpload={async (file) => {
                  const asset = await upload(file, 'Video')
                  if (asset) update(b.key, { video: { asset } })
                  return false
                }}>
                  <Button loading={uploading}>{b.video ? 'Đổi video' : '+ Video MP4 (≤ 30 giây)'}</Button>
                </Upload>
              </Space>
            )}

            {b.type === 'Text' && (
              <Input.TextArea rows={3} maxLength={1000} showCount placeholder="Nội dung" value={b.text}
                onChange={(e) => update(b.key, { text: e.target.value })} data-testid="block-text" />
            )}
          </Space>
        </Card>
      ))}

      {picked && limits && (
        <ProductPicker shopId={shopId} open title="Sản phẩm nổi bật" value={picked.products} max={limits.maxProducts}
          onClose={() => setPicking(null)} onPick={(products) => { update(picked.key, { products }); setPicking(null) }} />
      )}
    </Space>
  )
}

export default DecorationPage
