import { useEffect, useState } from 'react'
import { Input, Modal, Table, Tag, Typography } from 'antd'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { sellerApi, type ProductRow } from '../api/seller'
import { formatPrice } from '../lib/money'

export interface PickedProduct {
  id: string
  name: string
  imageUrl: string | null
}

interface Props {
  shopId: string
  open: boolean
  title: string
  value: PickedProduct[]
  max: number
  onClose: () => void
  onPick: (products: PickedProduct[]) => void
}

/** Pick products of the shop (search + multi-select, keeps the order they were picked in). */
const ProductPicker = ({ shopId, open, title, value, max, onClose, onPick }: Props) => {
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [picked, setPicked] = useState<PickedProduct[]>(value)
  useEffect(() => { if (open) setPicked(value) }, [open, value])
  const list = useQuery({
    queryKey: ['picker-products', shopId, q, page],
    queryFn: () => sellerApi.products(shopId, { tab: 'All', q, page, pageSize: 10 }),
    enabled: open,
    placeholderData: keepPreviousData,
  })
  const ids = picked.map((p) => p.id)

  const toggle = (row: ProductRow, on: boolean) =>
    setPicked((cur) => on
      ? (cur.some((p) => p.id === row.id) || cur.length >= max ? cur : [...cur, { id: row.id, name: row.name, imageUrl: row.imageUrl }])
      : cur.filter((p) => p.id !== row.id))

  return (
    <Modal open={open} title={title} width={760} okText={`Chọn (${picked.length})`} cancelText="Huỷ" onCancel={onClose}
      onOk={() => onPick(picked)} destroyOnClose>
      <Input.Search placeholder="Tìm theo tên sản phẩm" allowClear onSearch={(v) => { setQ(v); setPage(1) }} style={{ marginBottom: 12 }}
        data-testid="picker-search" />
      <Typography.Paragraph type="secondary">Đã chọn {picked.length} / tối đa {max}. Thứ tự hiển thị theo thứ tự chọn.</Typography.Paragraph>
      <Table<ProductRow>
        rowKey="id"
        size="small"
        loading={list.isLoading}
        dataSource={list.data?.items ?? []}
        rowSelection={{
          selectedRowKeys: ids,
          preserveSelectedRowKeys: true,
          onSelect: (row, on) => toggle(row, on),
          onSelectAll: (on, _rows, changed) => changed.forEach((r) => toggle(r, on)),
          getCheckboxProps: (r) => ({ disabled: !ids.includes(r.id) && picked.length >= max }),
        }}
        pagination={{ current: page, pageSize: 10, total: list.data?.totalCount ?? 0, onChange: setPage, showSizeChanger: false }}
        columns={[
          { title: 'Sản phẩm', render: (_, r) => r.name },
          { title: 'Giá', width: 140, render: (_, r) => formatPrice(r.minPrice) },
          { title: 'Trạng thái', width: 120, render: (_, r) => <Tag color={r.status === 'Active' ? 'green' : undefined}>{r.status === 'Active' ? 'Đang bán' : r.status}</Tag> },
        ]}
      />
    </Modal>
  )
}

export default ProductPicker
