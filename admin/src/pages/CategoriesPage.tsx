import { useMemo, useState } from 'react'
import { App as AntApp, Button, Card, Checkbox, Col, Empty, Form, Input, InputNumber, Modal, Row, Select, Space, Switch, Tree, Typography } from 'antd'
import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { catalogApi, type AttributeInputType, type Brand, type CategoryAttribute, type CategoryNode } from '../api/catalog'
import { ApiError } from '../api/http'
import { platformApi } from '../api/platform'
import DataTable from '../components/DataTable'
import PageHeader from '../components/PageHeader'
import RowActions from '../components/RowActions'
import { ToneTag } from '../components/StatusTag'

const INPUT_TYPES: { value: AttributeInputType; label: string }[] = [
  { value: 'SingleSelect', label: 'Chọn một' },
  { value: 'MultiSelect', label: 'Chọn nhiều' },
  { value: 'Text', label: 'Chữ' },
  { value: 'Number', label: 'Số + đơn vị' },
]

interface CategoryForm {
  name: string
  commissionPercent: number
  sortOrder: number
  isActive: boolean
  isVisible: boolean
}

type AttributeForm = Omit<CategoryAttribute, 'id' | 'categoryId'>

interface TreeItem {
  key: string
  title: JSX.Element
  children: TreeItem[]
}

const flatten = (nodes: CategoryNode[]): CategoryNode[] => nodes.flatMap((n) => [n, ...flatten(n.children)])

const errorText = (e: unknown) => (e instanceof ApiError ? e.fieldErrors[0]?.message ?? e.message : 'Thao tác thất bại.')

/** Category tree, per-leaf attributes, and brands. */
const CategoriesPage = () => {
  const { message } = AntApp.useApp()
  const queryClient = useQueryClient()
  const tree = useQuery({ queryKey: ['admin-categories'], queryFn: catalogApi.categories })
  const all = useMemo(() => flatten(tree.data ?? []), [tree.data])
  const [selected, setSelected] = useState<CategoryNode | null>(null)
  const [editing, setEditing] = useState<{ parentId: string | null; node: CategoryNode | null } | null>(null)
  const [attrEditing, setAttrEditing] = useState<CategoryAttribute | 'new' | null>(null)
  const [catForm] = Form.useForm<CategoryForm>()
  const [attrForm] = Form.useForm<AttributeForm>()

  const attributes = useQuery({
    queryKey: ['admin-attributes', selected?.id],
    queryFn: () => catalogApi.attributes(selected!.id),
    enabled: !!selected?.isLeaf,
  })

  const saveCategory = useMutation({
    mutationFn: (v: CategoryForm) => catalogApi.saveCategory({
      id: editing?.node?.id, parentId: editing?.node ? editing.node.parentId : editing?.parentId ?? null, name: v.name,
      iconUrl: editing?.node?.iconUrl ?? null, sortOrder: v.sortOrder, commissionRateBp: Math.round(v.commissionPercent * 100), isActive: v.isActive,
      isVisible: v.isVisible,
    }),
    onSuccess: (r) => { void message.success(r.message); setEditing(null); void queryClient.invalidateQueries({ queryKey: ['admin-categories'] }) },
    onError: (e) => void message.error(errorText(e)),
  })
  const saveAttribute = useMutation({
    mutationFn: (v: AttributeForm) => catalogApi.saveAttribute({
      ...v, id: attrEditing === 'new' ? undefined : attrEditing?.id, categoryId: selected!.id, options: v.options ?? [], unit: v.unit ?? null,
    }),
    onSuccess: (r) => { void message.success(r.message); setAttrEditing(null); void attributes.refetch() },
    onError: (e) => void message.error(errorText(e)),
  })
  const deleteAttribute = useMutation({
    mutationFn: catalogApi.deleteAttribute,
    onSuccess: (r) => { void message.success(r.message); void attributes.refetch() },
    onError: (e) => void message.error(errorText(e)),
  })

  const openCategory = (parentId: string | null, node: CategoryNode | null) => {
    setEditing({ parentId, node })
    catForm.setFieldsValue({
      name: node?.name ?? '', commissionPercent: (node?.commissionRateBp ?? 500) / 100, sortOrder: node?.sortOrder ?? 0, isActive: node?.isActive ?? true,
      // The node's own flag (the tree reports the effective one: a hidden parent hides it too)
      isVisible: node?.isVisible ?? true,
    })
  }
  const openAttribute = (a: CategoryAttribute | 'new') => {
    setAttrEditing(a)
    attrForm.setFieldsValue(a === 'new'
      ? { name: '', inputType: 'SingleSelect', unit: null, isRequired: false, isFilterable: true, options: [], sortOrder: attributes.data?.length ?? 0 }
      : { ...a })
  }
  const inputType = Form.useWatch('inputType', attrForm)

  const toTree = (nodes: CategoryNode[]): TreeItem[] =>
    nodes.map((n) => ({
      key: n.id,
      title: <span>{n.name} {!n.isActive && <ToneTag tone="default">Ẩn</ToneTag>} {n.isActive && !n.isVisible && <ToneTag tone="warning">Ẩn với người mua</ToneTag>} <Typography.Text type="secondary">{n.commissionRateBp / 100}%</Typography.Text></span>,
      children: toTree(n.children),
    }))

  return (
    <>
      <PageHeader title="Ngành hàng" description="Cây danh mục 3 cấp, phí cố định theo ngành, thuộc tính bắt buộc của từng danh mục lá và thương hiệu"
        actions={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => openCategory(null, null)}>Danh mục cấp 1</Button>} />
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={10}>
          <Card title="Cây danh mục" className="stat-card tree-card" loading={tree.isPending}>
            <Typography.Text type="secondary">Kéo thả để đổi vị trí hoặc chuyển sang danh mục cha khác (tối đa 3 cấp).</Typography.Text>
            <Tree treeData={toTree(tree.data ?? [])} onSelect={(keys) => setSelected(all.find((c) => c.id === keys[0]) ?? null)} height={560}
              draggable={{ icon: false }}
              onDrop={async (info) => {
                const dragged = all.find((c) => c.id === String(info.dragNode.key))
                const target = all.find((c) => c.id === String(info.node.key))
                if (!dragged || !target) return
                // Dropped on a node = becomes its child; dropped in a gap = sibling of that node
                const parentId = info.dropToGap ? target.parentId : target.id
                // antd gives an absolute position: compare it with the target's own index to know before / after
                const after = info.dropPosition - Number(String(info.node.pos).split('-').pop()) > 0
                const sortOrder = info.dropToGap ? target.sortOrder + (after ? 1 : -1) : 0
                try {
                  const r = await platformApi.moveCategory(dragged.id, parentId, sortOrder)
                  message.success(r.message)
                  void queryClient.invalidateQueries({ queryKey: ['admin-categories'] })
                } catch (e) {
                  message.error(errorText(e))
                }
              }} />
          </Card>
        </Col>
        <Col xs={24} lg={14}>
          {selected ? (
            <Card title={selected.name} className="stat-card" extra={
              <Space>
                <Button size="small" icon={<EditOutlined aria-hidden />} onClick={() => openCategory(null, selected)}>Sửa</Button>
                {selected.level < 3 && <Button size="small" icon={<PlusOutlined aria-hidden />} onClick={() => openCategory(selected.id, null)}>Danh mục con</Button>}
              </Space>
            }>
              {selected.isLeaf ? (
                <DataTable<CategoryAttribute> size="small" rowKey="id" loading={attributes.isPending} error={attributes.error} dataSource={attributes.data}
                  filters={<Typography.Text strong>Thuộc tính ngành hàng</Typography.Text>}
                  actions={<Button size="small" type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => openAttribute('new')}>Thuộc tính</Button>}
                  emptyText="Danh mục này chưa có thuộc tính"
                  columns={[
                    { title: 'Tên', dataIndex: 'name', render: (v: string, a) => <Space size={6}>{v}{a.isRequired && <ToneTag tone="error">Bắt buộc</ToneTag>}</Space> },
                    { title: 'Kiểu', dataIndex: 'inputType', render: (v: AttributeInputType) => INPUT_TYPES.find((t) => t.value === v)?.label },
                    { title: 'Lựa chọn', render: (_, a) => (a.options.length ? a.options.join(', ') : a.unit ?? '—') },
                    {
                      title: '', key: 'actions', align: 'right',
                      render: (_, a) => (
                        <RowActions name={a.name}
                          primary={<Button size="small" onClick={() => openAttribute(a)}>Sửa</Button>}
                          items={[{
                            key: 'delete', icon: <DeleteOutlined aria-hidden />, label: 'Xoá thuộc tính', danger: true,
                            confirm: { title: `Xoá thuộc tính ${a.name}?`, okText: 'Xoá' }, onClick: () => deleteAttribute.mutateAsync(a.id).catch(() => undefined),
                          }]} />
                      ),
                    },
                  ]} />
              ) : (
                <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chọn danh mục cấp cuối để khai báo thuộc tính" />
              )}
            </Card>
          ) : (
            <Card className="stat-card"><Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chọn một danh mục bên trái để xem và sửa" /></Card>
          )}
          <BrandsCard />
        </Col>
      </Row>

      <Modal title={editing?.node ? 'Sửa danh mục' : 'Thêm danh mục'} open={!!editing} onCancel={() => setEditing(null)}
        okText="Lưu" cancelText="Huỷ" onOk={() => catForm.submit()} okButtonProps={{ loading: saveCategory.isPending }}>
        <Form<CategoryForm> form={catForm} layout="vertical" onFinish={(v) => saveCategory.mutate(v)} requiredMark={false}>
          <Form.Item label="Tên" name="name" rules={[{ required: true, message: 'Nhập tên.' }]}><Input /></Form.Item>
          <Form.Item label="Phí cố định (%)" name="commissionPercent"><InputNumber min={0} max={100} step={0.5} /></Form.Item>
          <Form.Item label="Thứ tự" name="sortOrder"><InputNumber min={0} /></Form.Item>
          <Form.Item label="Hiển thị" name="isActive" valuePropName="checked"><Switch /></Form.Item>
          <Form.Item label="Hiện với người mua" name="isVisible" valuePropName="checked"
            extra="Tắt khi ngành chưa có sản phẩm để trưng bày: người bán vẫn đăng được, người mua chưa thấy (menu, bộ lọc, sitemap…)."><Switch /></Form.Item>
        </Form>
      </Modal>

      <Modal title={attrEditing === 'new' ? 'Thêm thuộc tính' : 'Sửa thuộc tính'} open={!!attrEditing} onCancel={() => setAttrEditing(null)}
        okText="Lưu" cancelText="Huỷ" onOk={() => attrForm.submit()} okButtonProps={{ loading: saveAttribute.isPending }}>
        <Form<AttributeForm> form={attrForm} layout="vertical" onFinish={(v) => saveAttribute.mutate(v)} requiredMark={false}>
          <Form.Item label="Tên" name="name" rules={[{ required: true, message: 'Nhập tên.' }]}><Input /></Form.Item>
          <Form.Item label="Kiểu nhập" name="inputType"><Select options={INPUT_TYPES} /></Form.Item>
          {(inputType === 'SingleSelect' || inputType === 'MultiSelect') && (
            <Form.Item label="Các lựa chọn" name="options"><Select mode="tags" tokenSeparators={[',']} placeholder="Gõ rồi Enter" /></Form.Item>
          )}
          {inputType === 'Number' && <Form.Item label="Đơn vị" name="unit"><Input placeholder="VD: g, ml, W" /></Form.Item>}
          <Space>
            <Form.Item name="isRequired" valuePropName="checked"><Checkbox>Bắt buộc</Checkbox></Form.Item>
            <Form.Item name="isFilterable" valuePropName="checked"><Checkbox>Dùng để lọc</Checkbox></Form.Item>
          </Space>
          <Form.Item label="Thứ tự" name="sortOrder"><InputNumber min={0} /></Form.Item>
        </Form>
      </Modal>
    </>
  )
}

const BrandsCard = () => {
  const { message } = AntApp.useApp()
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<Brand | 'new' | null>(null)
  const [name, setName] = useState('')
  const [verified, setVerified] = useState(true)
  const brands = useQuery({ queryKey: ['admin-brands', search], queryFn: () => catalogApi.brands(search) })
  const save = useMutation({
    mutationFn: () => catalogApi.saveBrand({ id: editing === 'new' ? undefined : editing?.id, name, logoUrl: editing === 'new' ? null : editing?.logoUrl ?? null, isVerified: verified }),
    onSuccess: (r) => { void message.success(r.message); setEditing(null); void brands.refetch() },
    onError: (e) => void message.error(errorText(e)),
  })

  return (
    <Card title="Thương hiệu" className="stat-card" style={{ marginTop: 16 }}>
      <DataTable<Brand> size="small" rowKey="id" dataSource={brands.data} loading={brands.isPending} error={brands.error} paging="client"
        search={{ value: search, onSearch: setSearch, placeholder: 'Tìm thương hiệu', width: 220 }}
        actions={<Button size="small" icon={<PlusOutlined aria-hidden />} onClick={() => { setEditing('new'); setName(''); setVerified(true) }}>Thương hiệu</Button>}
        emptyText="Không có thương hiệu phù hợp"
        columns={[
          { title: 'Tên', dataIndex: 'name', render: (v: string, b) => <Space size={6}>{v}{b.isVerified && <ToneTag tone="info">Chính hãng</ToneTag>}</Space> },
          { title: '', key: 'actions', align: 'right', render: (_, b) => <RowActions name={b.name} primary={<Button size="small" onClick={() => { setEditing(b); setName(b.name); setVerified(b.isVerified) }}>Sửa</Button>} /> },
        ]} />
      <Modal title={editing === 'new' ? 'Thêm thương hiệu' : 'Sửa thương hiệu'} open={!!editing} onCancel={() => setEditing(null)}
        okText="Lưu" cancelText="Huỷ" onOk={() => save.mutate()} okButtonProps={{ disabled: !name.trim(), loading: save.isPending }}>
        <Space direction="vertical" style={{ width: '100%' }}>
          <Input placeholder="Tên thương hiệu" value={name} onChange={(e) => setName(e.target.value)} />
          <Checkbox checked={verified} onChange={(e) => setVerified(e.target.checked)}>Đã xác minh chính hãng</Checkbox>
        </Space>
      </Modal>
    </Card>
  )
}

export default CategoriesPage
