import { useState } from 'react'
import { App, Button, Card, Input, InputNumber, Modal, Popconfirm, Space, Switch, Table, Typography } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/http'
import { designApi, type ShopCategory } from '../api/design'
import ProductPicker, { type PickedProduct } from '../components/ProductPicker'

const MAX_PRODUCTS = 500

type Draft = { id?: string; name: string; sortOrder: number; isVisible: boolean }

/** Thiết lập shop → Danh mục của shop: the shop's own tabs on its page, with the products in each. */
const ShopCategoriesPage = ({ shopId }: { shopId: string }) => {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: ['shop-categories', shopId], queryFn: () => designApi.categories(shopId) })
  const [draft, setDraft] = useState<Draft | null>(null)
  const [members, setMembers] = useState<{ category: ShopCategory; products: PickedProduct[] } | null>(null)

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['shop-categories', shopId] })
  const fail = (e: unknown) => message.error(e instanceof ApiError ? e.message : 'Không lưu được.')
  const save = useMutation({
    mutationFn: (d: Draft) => {
      const body = { name: d.name.trim(), sortOrder: d.sortOrder, isVisible: d.isVisible }
      return d.id ? designApi.updateCategory(shopId, d.id, body) : designApi.createCategory(shopId, body)
    },
    onSuccess: (r) => { message.success(r.message); setDraft(null); void refresh() },
    onError: fail,
  })
  const remove = useMutation({
    mutationFn: (id: string) => designApi.deleteCategory(shopId, id),
    onSuccess: (r) => { message.success(r.message); void refresh() },
    onError: fail,
  })
  const setProducts = useMutation({
    mutationFn: ({ id, ids }: { id: string; ids: string[] }) => designApi.setMembers(shopId, id, ids),
    onSuccess: (r) => { message.success(r.message); setMembers(null); void refresh() },
    onError: fail,
  })

  const openMembers = async (category: ShopCategory) => {
    try {
      const placed = await designApi.members(shopId, category.id)
      setMembers({ category, products: placed.map((p) => ({ id: p.id, name: p.name, imageUrl: p.imageUrl })) })
    } catch (e) {
      fail(e)
    }
  }

  const rows = list.data ?? []
  return (
    <Card
      title="Danh mục của shop"
      extra={<Button type="primary" data-testid="shop-category-add"
        onClick={() => setDraft({ name: '', sortOrder: (rows.at(-1)?.sortOrder ?? 0) + 1, isVisible: true })}>Thêm danh mục</Button>}
    >
      <Typography.Paragraph type="secondary">
        Mỗi danh mục đang hiển thị và có sản phẩm đang bán là một tab trên trang shop, theo thứ tự nhỏ → lớn.
      </Typography.Paragraph>
      <Table<ShopCategory>
        rowKey="id"
        loading={list.isLoading}
        dataSource={rows}
        pagination={false}
        locale={{ emptyText: 'Chưa có danh mục nào' }}
        columns={[
          { title: 'Thứ tự', dataIndex: 'sortOrder', width: 80 },
          { title: 'Tên danh mục', dataIndex: 'name' },
          { title: 'Sản phẩm', dataIndex: 'productCount', width: 100 },
          { title: 'Hiển thị', width: 100, render: (_, c) => (c.isVisible ? 'Có' : 'Ẩn') },
          {
            title: '',
            width: 260,
            render: (_, c) => (
              <Space>
                <Button size="small" onClick={() => void openMembers(c)} data-testid="shop-category-products">Sản phẩm</Button>
                <Button size="small" onClick={() => setDraft({ id: c.id, name: c.name, sortOrder: c.sortOrder, isVisible: c.isVisible })}>Sửa</Button>
                <Popconfirm title={`Xoá danh mục "${c.name}"? Sản phẩm vẫn còn, chỉ bỏ khỏi danh mục.`} okText="Xoá" cancelText="Không"
                  onConfirm={() => remove.mutate(c.id)}>
                  <Button size="small" danger>Xoá</Button>
                </Popconfirm>
              </Space>
            ),
          },
        ]}
      />

      <Modal open={draft !== null} title={draft?.id ? 'Sửa danh mục' : 'Thêm danh mục'} okText="Lưu" cancelText="Huỷ"
        confirmLoading={save.isPending} onCancel={() => setDraft(null)} onOk={() => draft && save.mutate(draft)}
        okButtonProps={{ disabled: !draft?.name.trim() }} destroyOnClose>
        {draft && (
          <Space direction="vertical" style={{ width: '100%' }}>
            <Input placeholder="Tên danh mục (tối đa 40 ký tự)" maxLength={40} value={draft.name} autoFocus data-testid="shop-category-name"
              onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
            <Space>
              <span>Thứ tự</span>
              <InputNumber min={0} max={999} value={draft.sortOrder} onChange={(v) => setDraft({ ...draft, sortOrder: v ?? 0 })} />
              <span>Hiển thị</span>
              <Switch checked={draft.isVisible} onChange={(v) => setDraft({ ...draft, isVisible: v })} />
            </Space>
          </Space>
        )}
      </Modal>

      {members && (
        <ProductPicker shopId={shopId} open title={`Sản phẩm của "${members.category.name}"`} value={members.products} max={MAX_PRODUCTS}
          onClose={() => setMembers(null)}
          onPick={(picked) => setProducts.mutate({ id: members.category.id, ids: picked.map((p) => p.id) })} />
      )}
    </Card>
  )
}

export default ShopCategoriesPage
