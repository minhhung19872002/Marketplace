import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  Alert, App as AntApp, Button, Card, Cascader, Checkbox, Col, Input, InputNumber, Radio, Row, Select, Space, Switch, Table, Tag, Typography, Upload,
} from 'antd'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  sellerApi, uploadMedia, type CategoryAttribute, type CategoryNode, type ProductInput,
} from '../api/seller'
import { logisticsApi } from '../api/logistics'
import { ApiError } from '../api/http'

interface MediaItem {
  assetId: string
  url: string
  type: 'Image' | 'Video'
}

interface Tier {
  name: string
  options: string[]
}

interface SkuRow {
  key: string
  option1: string | null
  option2: string | null
  sellerSku: string
  price: number | null
  originalPrice: number | null
  stock: number | null
  isActive: boolean
  // Own package size (mm) when it differs from the product's
  size: { l: number | null; w: number | null; h: number | null }
}

const MAX_IMAGES = 9

const comboKey = (a: string | null, b: string | null) => `${a ?? ''}§${b ?? ''}`

/** Rebuild the SKU table from the tiers, keeping values already typed for surviving combinations. */
function rebuildSkus(tiers: Tier[], previous: SkuRow[]): SkuRow[] {
  const t1 = tiers[0]?.options.length ? tiers[0].options : [null]
  const t2 = tiers[1]?.options.length ? tiers[1].options : [null]
  const byKey = new Map(previous.map((r) => [r.key, r]))
  const rows: SkuRow[] = []
  for (const a of t1) {
    for (const b of t2) {
      const key = comboKey(a, b)
      rows.push(byKey.get(key) ?? { key, option1: a, option2: b, sellerSku: '', price: null, originalPrice: null, stock: null, isActive: true,
        size: { l: null, w: null, h: null } })
    }
  }
  return rows
}

interface CascaderOption {
  value: string
  label: string
  children?: CascaderOption[]
}

const toCascader = (nodes: CategoryNode[]): CascaderOption[] =>
  nodes.map((n) => ({ value: n.id, label: n.name, children: n.children.length ? toCascader(n.children) : undefined }))

const findPath = (nodes: CategoryNode[], id: string, trail: string[] = []): string[] | null => {
  for (const n of nodes) {
    if (n.id === id) return [...trail, n.id]
    const sub = findPath(n.children, id, [...trail, n.id])
    if (sub) return sub
  }
  return null
}

const AttributeField = ({ def, value, onChange }: { def: CategoryAttribute; value: string[]; onChange: (v: string[]) => void }) => {
  const label = `${def.name}${def.isRequired ? ' *' : ''}`
  switch (def.inputType) {
    case 'SingleSelect':
      return <Select allowClear placeholder={label} aria-label={def.name} value={value[0]} style={{ width: '100%' }}
        options={def.options.map((o) => ({ value: o, label: o }))} onChange={(v?: string) => onChange(v ? [v] : [])} />
    case 'MultiSelect':
      return <Select mode="multiple" placeholder={label} aria-label={def.name} value={value} style={{ width: '100%' }}
        options={def.options.map((o) => ({ value: o, label: o }))} onChange={onChange} />
    case 'Number':
      return <InputNumber<string> placeholder={label} aria-label={def.name} value={value[0]} style={{ width: '100%' }} stringMode
        addonAfter={def.unit ?? undefined} onChange={(v) => onChange(v ? [String(v)] : [])} />
    default:
      return <Input placeholder={label} aria-label={def.name} value={value[0] ?? ''} maxLength={200} onChange={(e) => onChange(e.target.value ? [e.target.value] : [])} />
  }
}

const ProductEditorPage = ({ shopId }: { shopId: string }) => {
  const { id } = useParams()
  const isNew = !id || id === 'moi'
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { message } = AntApp.useApp()

  const categories = useQuery({ queryKey: ['categories'], queryFn: sellerApi.categories, staleTime: Infinity })
  const existing = useQuery({ queryKey: ['product', shopId, id], queryFn: () => sellerApi.product(shopId, id!), enabled: !isNew })

  const [categoryId, setCategoryId] = useState<string | null>(null)
  const [brandId, setBrandId] = useState<string | null>(null)
  const [brandQuery, setBrandQuery] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [condition, setCondition] = useState<'New' | 'Used'>('New')
  const [attributes, setAttributes] = useState<Record<string, string[]>>({})
  const [media, setMedia] = useState<MediaItem[]>([])
  const [hasVariants, setHasVariants] = useState(false)
  const [tiers, setTiers] = useState<Tier[]>([{ name: 'Màu sắc', options: [] }])
  const [skus, setSkus] = useState<SkuRow[]>(rebuildSkus([], []))
  const [bulk, setBulk] = useState<{ price: number | null; stock: number | null }>({ price: null, stock: null })
  const [weightG, setWeightG] = useState<number | null>(null)
  const [dims, setDims] = useState({ l: 0, w: 0, h: 0 })
  const [isPreorder, setIsPreorder] = useState(false)
  const [preorderDays, setPreorderDays] = useState(7)
  const [maxPerBuyer, setMaxPerBuyer] = useState<number | null>(null)
  const [warehouseId, setWarehouseId] = useState<string | null>(null)
  const [carrierCodes, setCarrierCodes] = useState<string[]>([])
  const [errors, setErrors] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const [uploading, setUploading] = useState(false)

  const attributeDefs = useQuery({
    queryKey: ['attributes', categoryId],
    queryFn: () => sellerApi.attributes(categoryId!),
    enabled: !!categoryId,
  })
  const brands = useQuery({ queryKey: ['brands', brandQuery], queryFn: () => sellerApi.brands(brandQuery) })
  const logistics = useQuery({ queryKey: ['logistics', shopId], queryFn: () => logisticsApi.get(shopId) })
  const suggestions = useQuery({
    queryKey: ['suggest', name],
    queryFn: () => sellerApi.suggestCategories(name),
    enabled: isNew && name.trim().length >= 4 && !categoryId,
  })

  // Load an existing product into the editor once
  useEffect(() => {
    const p = existing.data
    if (!p) return
    setCategoryId(p.categoryId)
    setBrandId(p.brandId)
    setName(p.name)
    setDescription(p.description)
    setCondition(p.condition)
    setAttributes(Object.fromEntries(p.attributes.map((a) => [a.attributeId, a.values])))
    setMedia(p.media.filter((m) => m.assetId).map((m) => ({ assetId: m.assetId!, url: m.url, type: m.type })))
    const loadedTiers = p.tiers.map((t) => ({ name: t.name, options: t.options.map((o) => o.value) }))
    setHasVariants(loadedTiers.length > 0)
    if (loadedTiers.length) setTiers(loadedTiers)
    setSkus(p.skus.map((s) => ({
      key: comboKey(s.option1, s.option2), option1: s.option1, option2: s.option2, sellerSku: s.sellerSku ?? '',
      price: s.price, originalPrice: s.originalPrice, stock: s.stock, isActive: s.isActive,
      size: { l: s.size?.lengthMm ?? null, w: s.size?.widthMm ?? null, h: s.size?.heightMm ?? null },
    })))
    setWeightG(p.weightG)
    setDims({ l: p.lengthMm, w: p.widthMm, h: p.heightMm })
    setIsPreorder(p.isPreorder)
    setPreorderDays(p.preorderDays || 7)
    setMaxPerBuyer(p.maxPerBuyer)
    setWarehouseId(p.warehouseId)
    setCarrierCodes(p.carrierCodes ?? [])
  }, [existing.data])

  const activeTiers = useMemo(() => (hasVariants ? tiers.filter((t) => t.name.trim() && t.options.length) : []), [hasVariants, tiers])
  useEffect(() => { setSkus((prev) => rebuildSkus(activeTiers, prev)) }, [activeTiers])

  const cascaderValue = useMemo(() => (categoryId && categories.data ? findPath(categories.data, categoryId) ?? undefined : undefined), [categoryId, categories.data])

  const updateSku = (key: string, patch: Partial<SkuRow>) => setSkus((rows) => rows.map((r) => (r.key === key ? { ...r, ...patch } : r)))

  const buildInput = (): ProductInput => ({
    categoryId: categoryId ?? '',
    brandId,
    name: name.trim(),
    description,
    condition,
    weightG: weightG ?? 0,
    lengthMm: dims.l,
    widthMm: dims.w,
    heightMm: dims.h,
    isPreorder,
    preorderDays: isPreorder ? preorderDays : 0,
    maxPerBuyer,
    warehouseId,
    carrierCodes,
    attributes: Object.entries(attributes).filter(([, v]) => v.length).map(([attributeId, values]) => ({ attributeId, values })),
    media: media.map((m) => ({ assetId: m.assetId, optionValue: null })),
    tiers: activeTiers.map((t) => ({ name: t.name.trim(), options: t.options.map((o) => ({ value: o, imageAssetId: null })) })),
    skus: skus.map((s) => ({
      option1: s.option1, option2: s.option2, sellerSku: s.sellerSku || null, price: s.price ?? 0,
      originalPrice: s.originalPrice ?? s.price ?? 0, stock: s.stock ?? 0, weightG: null, isActive: s.isActive,
      size: s.size.l && s.size.w && s.size.h ? { lengthMm: s.size.l, widthMm: s.size.w, heightMm: s.size.h } : null,
    })),
  })

  // Pre-flight check: list every problem at once before sending
  const localErrors = (): string[] => {
    const list: string[] = []
    if (!name.trim()) list.push('Nhập tên sản phẩm.')
    if (name.length > 120) list.push('Tên tối đa 120 ký tự.')
    if (!categoryId) list.push('Chọn danh mục cấp cuối.')
    if (!media.some((m) => m.type === 'Image')) list.push('Cần ít nhất 1 ảnh.')
    if (!weightG) list.push('Nhập cân nặng.')
    for (const def of attributeDefs.data ?? []) if (def.isRequired && !(attributes[def.id]?.length)) list.push(`Nhập ${def.name}.`)
    if (hasVariants && activeTiers.length === 0) list.push('Khai báo ít nhất một tầng phân loại có lựa chọn.')
    skus.forEach((s) => {
      const label = [s.option1, s.option2].filter(Boolean).join(' / ') || 'sản phẩm'
      if (!s.price) list.push(`Nhập giá cho ${label}.`)
      if (s.stock === null) list.push(`Nhập tồn kho cho ${label}.`)
    })
    return list
  }

  const save = async (submit: boolean) => {
    const problems = localErrors()
    setErrors(problems)
    if (problems.length) return
    setBusy(true)
    try {
      let productId = id
      if (isNew) {
        productId = (await sellerApi.createProduct(shopId, buildInput())).data
      } else {
        await sellerApi.updateProduct(shopId, id!, buildInput(), existing.data!.version)
      }
      if (submit) await sellerApi.productAction(shopId, productId!, 'submit')
      void message.success(submit ? 'Đã lưu và gửi duyệt.' : 'Đã lưu.')
      await queryClient.invalidateQueries({ queryKey: ['products', shopId] })
      await queryClient.invalidateQueries({ queryKey: ['product', shopId] })
      navigate('/san-pham')
    } catch (err) {
      setErrors(err instanceof ApiError ? (err.fieldErrors.length ? err.fieldErrors.map((f) => f.message) : [err.message]) : ['Lưu thất bại.'])
    } finally {
      setBusy(false)
    }
  }

  if (!isNew && existing.isPending) return <Card loading />
  if (!isNew && existing.isError) return <Alert type="error" message="Không tải được sản phẩm." />

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Title level={3} style={{ margin: 0 }}>{isNew ? 'Thêm sản phẩm' : 'Sửa sản phẩm'}</Typography.Title>
      {existing.data?.reviewNote && <Alert type="warning" showIcon message={`Sàn yêu cầu sửa: ${existing.data.reviewNote}`} />}
      {errors.length > 0 && (
        <Alert type="error" showIcon data-testid="editor-errors" message="Vui lòng kiểm tra lại"
          description={<ul style={{ margin: 0, paddingLeft: 18 }}>{errors.map((e) => <li key={e}>{e}</li>)}</ul>} />
      )}

      <Card title="Thông tin cơ bản">
        <Space direction="vertical" style={{ width: '100%' }}>
          <Typography.Text>Hình ảnh ({media.filter((m) => m.type === 'Image').length}/{MAX_IMAGES}) — ảnh đầu tiên là ảnh bìa</Typography.Text>
          <Space wrap>
            {media.map((m, i) => (
              <div key={m.assetId} className="media-tile">
                {m.type === 'Image' ? <img src={m.url} alt="" /> : <video src={m.url} muted />}
                <div className="media-actions">
                  {i > 0 && <Button size="small" onClick={() => setMedia((x) => { const c = [...x]; [c[i - 1], c[i]] = [c[i], c[i - 1]]; return c })}>←</Button>}
                  <Button size="small" danger onClick={() => setMedia((x) => x.filter((_, j) => j !== i))}>Xoá</Button>
                </div>
                {i === 0 && m.type === 'Image' && <Tag color="orange" className="cover-tag">Ảnh bìa</Tag>}
              </div>
            ))}
            {media.filter((m) => m.type === 'Image').length < MAX_IMAGES && (
              <div data-testid="product-upload">
              <Upload accept="image/*,video/mp4" multiple showUploadList={false}
                beforeUpload={async (file) => {
                  setUploading(true)
                  try {
                    const asset = await uploadMedia('product', file)
                    setMedia((x) => [...x, { assetId: asset.id, url: asset.thumbnailUrl ?? asset.url ?? '', type: asset.kind === 'Video' ? 'Video' : 'Image' }])
                  } catch (err) {
                    void message.error(err instanceof ApiError ? err.message : 'Tải ảnh thất bại.')
                  } finally {
                    setUploading(false)
                  }
                  return false
                }}>
                <div className="media-add">{uploading ? 'Đang tải…' : '+ Thêm ảnh / video'}</div>
              </Upload>
              </div>
            )}
          </Space>

          <Input placeholder="Tên sản phẩm" value={name} onChange={(e) => setName(e.target.value)} maxLength={120} showCount aria-label="Tên sản phẩm" />
          <Cascader
            style={{ width: '100%' }}
            placeholder="Chọn danh mục (cấp cuối)"
            options={categories.data ? toCascader(categories.data) : []}
            value={cascaderValue}
            showSearch
            onChange={(v) => {
              const leaf = (v as string[] | undefined)?.at(-1) ?? null
              setCategoryId(leaf)
              setAttributes({})
            }}
            aria-label="Danh mục"
          />
          {suggestions.data && suggestions.data.length > 0 && !categoryId && (
            <Space wrap>
              <Typography.Text type="secondary">Gợi ý:</Typography.Text>
              {suggestions.data.map((s) => (
                <Tag key={s.id} color="blue" style={{ cursor: 'pointer' }} onClick={() => setCategoryId(s.id)}>{s.path.join(' › ')}</Tag>
              ))}
            </Space>
          )}
          <Input.TextArea placeholder="Mô tả sản phẩm (có thể dùng HTML cơ bản: p, b, ul, li, img…)" rows={6} value={description}
            onChange={(e) => setDescription(e.target.value)} aria-label="Mô tả" />
        </Space>
      </Card>

      <Card title="Thuộc tính">
        {!categoryId ? (
          <Typography.Text type="secondary">Chọn danh mục để hiện các thuộc tính của ngành hàng.</Typography.Text>
        ) : (
          <Row gutter={[16, 16]}>
            <Col xs={24} md={12}>
              <Select allowClear showSearch placeholder="Thương hiệu (không bắt buộc)" style={{ width: '100%' }} value={brandId ?? undefined}
                filterOption={false} onSearch={setBrandQuery} onChange={(v?: string) => setBrandId(v ?? null)}
                options={brands.data?.map((b) => ({ value: b.id, label: b.name }))} />
            </Col>
            <Col xs={24} md={12}>
              <Radio.Group value={condition} onChange={(e) => setCondition(e.target.value)}
                options={[{ value: 'New', label: 'Mới' }, { value: 'Used', label: 'Đã sử dụng' }]} />
            </Col>
            {attributeDefs.data?.map((def) => (
              <Col xs={24} md={12} key={def.id}>
                <AttributeField def={def} value={attributes[def.id] ?? []} onChange={(v) => setAttributes((a) => ({ ...a, [def.id]: v }))} />
              </Col>
            ))}
          </Row>
        )}
      </Card>

      <Card title="Thông tin bán hàng">
        <Space direction="vertical" style={{ width: '100%' }}>
          <Checkbox checked={hasVariants} onChange={(e) => setHasVariants(e.target.checked)} data-testid="has-variants">
            Sản phẩm có phân loại (màu, size…)
          </Checkbox>
          {hasVariants && tiers.map((tier, ti) => (
            <Card key={ti} size="small" title={`Phân loại ${ti + 1}`}
              extra={ti === 1 && <Button size="small" onClick={() => setTiers((t) => t.slice(0, 1))}>Bỏ tầng 2</Button>}>
              <Space direction="vertical" style={{ width: '100%' }}>
                <Input placeholder="Tên phân loại (VD: Màu sắc, Size)" value={tier.name} maxLength={50}
                  aria-label={`Tên phân loại ${ti + 1}`}
                  onChange={(e) => setTiers((t) => t.map((x, j) => (j === ti ? { ...x, name: e.target.value } : x)))} />
                <Select mode="tags" placeholder="Gõ lựa chọn rồi Enter (tối đa 20)" value={tier.options} style={{ width: '100%' }}
                  aria-label={`Lựa chọn phân loại ${ti + 1}`} tokenSeparators={[',']} maxCount={20}
                  onChange={(v: string[]) => setTiers((t) => t.map((x, j) => (j === ti ? { ...x, options: [...new Set(v.map((s) => s.trim()).filter(Boolean))] } : x)))} />
              </Space>
            </Card>
          ))}
          {hasVariants && tiers.length < 2 && (
            <Button onClick={() => setTiers((t) => [...t, { name: 'Size', options: [] }])}>+ Thêm phân loại 2</Button>
          )}

          {skus.length > 1 && (
            <Space>
              <Typography.Text>Áp dụng cho tất cả:</Typography.Text>
              <InputNumber<number> placeholder="Giá" min={1000} step={1000} value={bulk.price} onChange={(v) => setBulk((b) => ({ ...b, price: v }))} />
              <InputNumber<number> placeholder="Tồn kho" min={0} value={bulk.stock} onChange={(v) => setBulk((b) => ({ ...b, stock: v }))} />
              <Button onClick={() => setSkus((rows) => rows.map((r) => ({
                ...r,
                price: bulk.price ?? r.price,
                originalPrice: bulk.price && (r.originalPrice ?? 0) < bulk.price ? bulk.price : r.originalPrice,
                stock: bulk.stock ?? r.stock,
              })))}>Áp dụng</Button>
            </Space>
          )}

          <Table<SkuRow>
            size="small"
            rowKey="key"
            dataSource={skus}
            pagination={false}
            scroll={{ x: true }}
            data-testid="sku-table"
            columns={[
              ...(activeTiers.length ? [{ title: activeTiers.map((t) => t.name).join(' / '), render: (_: unknown, r: SkuRow) => [r.option1, r.option2].filter(Boolean).join(' / ') }] : []),
              {
                title: 'Giá bán',
                render: (_, r) => <InputNumber<number> min={1000} step={1000} value={r.price} aria-label="Giá bán"
                  onChange={(v) => updateSku(r.key, { price: v, originalPrice: r.originalPrice && v && r.originalPrice >= v ? r.originalPrice : v })} />,
              },
              {
                title: 'Giá gốc',
                render: (_, r) => <InputNumber<number> min={1000} step={1000} value={r.originalPrice} aria-label="Giá gốc"
                  onChange={(v) => updateSku(r.key, { originalPrice: v })} />,
              },
              {
                title: 'Tồn kho',
                render: (_, r) => <InputNumber<number> min={0} value={r.stock} aria-label="Tồn kho" onChange={(v) => updateSku(r.key, { stock: v })} />,
              },
              {
                title: 'Kích thước riêng (D×R×C mm)',
                render: (_, r) => (
                  <Space.Compact>
                    {(['l', 'w', 'h'] as const).map((k) => (
                      <InputNumber<number> key={k} min={1} max={5000} value={r.size[k]} style={{ width: 70 }} placeholder={k === 'l' ? 'D' : k === 'w' ? 'R' : 'C'}
                        aria-label={`Kích thước ${k}`} onChange={(v) => updateSku(r.key, { size: { ...r.size, [k]: v } })} />
                    ))}
                  </Space.Compact>
                ),
              },
              {
                title: 'Mã SKU',
                render: (_, r) => <Input value={r.sellerSku} maxLength={50} onChange={(e) => updateSku(r.key, { sellerSku: e.target.value })} />,
              },
              ...(activeTiers.length ? [{
                title: 'Bán',
                render: (_: unknown, r: SkuRow) => <Switch size="small" checked={r.isActive} onChange={(v) => updateSku(r.key, { isActive: v })} />,
              }] : []),
            ]}
          />
        </Space>
      </Card>

      <Card title="Vận chuyển">
        <Space wrap>
          <InputNumber<number> addonBefore="Cân nặng" addonAfter="g" min={1} value={weightG} onChange={setWeightG} aria-label="Cân nặng" />
          <InputNumber<number> addonBefore="Dài" addonAfter="mm" min={0} value={dims.l} onChange={(v) => setDims((d) => ({ ...d, l: v ?? 0 }))} />
          <InputNumber<number> addonBefore="Rộng" addonAfter="mm" min={0} value={dims.w} onChange={(v) => setDims((d) => ({ ...d, w: v ?? 0 }))} />
          <InputNumber<number> addonBefore="Cao" addonAfter="mm" min={0} value={dims.h} onChange={(v) => setDims((d) => ({ ...d, h: v ?? 0 }))} />
        </Space>
        <div style={{ marginTop: 12 }}>
          <Checkbox checked={isPreorder} onChange={(e) => setIsPreorder(e.target.checked)}>Hàng đặt trước</Checkbox>
          {isPreorder && <InputNumber<number> min={7} max={30} value={preorderDays} onChange={(v) => setPreorderDays(v ?? 7)} addonAfter="ngày chuẩn bị" />}
        </div>
        <div style={{ marginTop: 12 }}>
          <InputNumber<number> min={1} max={999} value={maxPerBuyer} onChange={(v) => setMaxPerBuyer(v ?? null)} placeholder="Không giới hạn"
            addonBefore="Giới hạn mua mỗi người" addonAfter="sản phẩm" style={{ width: 380 }} data-testid="max-per-buyer" />
        </div>
        {logistics.data?.multiWarehouse && (
          <div style={{ marginTop: 12 }}>
            <Typography.Text>Kho gửi </Typography.Text>
            <Select style={{ width: 320 }} value={warehouseId ?? ''} onChange={(v) => setWarehouseId(v || null)} aria-label="Kho gửi" data-testid="product-warehouse"
              options={[
                { value: '', label: 'Kho lấy hàng mặc định' },
                ...logistics.data.warehouses.filter((w) => !w.isPickupDefault).map((w) => ({ value: w.id, label: w.name })),
              ]} />
          </div>
        )}
        <div style={{ marginTop: 12 }}>
          <Typography.Text>Đơn vị vận chuyển cho sản phẩm </Typography.Text>
          <Select mode="multiple" allowClear style={{ minWidth: 320 }} value={carrierCodes} onChange={setCarrierCodes} placeholder="Mọi đơn vị shop đang dùng"
            aria-label="Đơn vị vận chuyển cho sản phẩm"
            options={(logistics.data?.channels ?? []).filter((c) => c.carrierActive && c.isEnabled).map((c) => ({ value: c.carrierCode, label: c.name }))} />
        </div>
      </Card>

      <Space>
        <Button onClick={() => navigate('/san-pham')}>Huỷ</Button>
        <Button onClick={() => save(false)} loading={busy} data-testid="save-draft">Lưu</Button>
        <Button type="primary" onClick={() => save(true)} loading={busy} data-testid="save-submit">Lưu &amp; gửi duyệt</Button>
      </Space>
    </Space>
  )
}

export default ProductEditorPage
