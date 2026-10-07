import { useState } from 'react'
import { App as AntApp, Upload } from 'antd'
import { uploadMedia, type MediaAsset, type MediaPurpose } from '../api/seller'
import { ApiError } from '../api/http'

interface Props {
  purpose: MediaPurpose
  label: string
  accept?: string
  value: MediaAsset | null
  onChange: (asset: MediaAsset | null) => void
  testId?: string
}

const readAsDataUrl = (file: File) =>
  new Promise<string | null>((resolve) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => resolve(null)
    reader.readAsDataURL(file)
  })

/** One-file uploader; the server validates the bytes and returns the stored asset. */
const UploadBox = ({ purpose, label, accept = 'image/*', value, onChange, testId }: Props) => {
  const { message } = AntApp.useApp()
  const [busy, setBusy] = useState(false)
  const [localPreview, setLocalPreview] = useState<string | null>(null)

  // The test id sits on a wrapper: Upload does not put it on an element that contains its file input
  return (
    <div data-testid={testId}>
    <Upload
      accept={accept}
      listType="picture-card"
      showUploadList={false}
      beforeUpload={async (file) => {
        setBusy(true)
        try {
          const asset = await uploadMedia(purpose, file)
          // Private files (KYC) have no public URL: preview the local copy instead — as a data: URL, the CSP blocks blob: images
          setLocalPreview(asset.url ? null : await readAsDataUrl(file))
          onChange(asset)
        } catch (err) {
          void message.error(err instanceof ApiError ? err.message : 'Tải tệp thất bại.')
        } finally {
          setBusy(false)
        }
        return false
      }}
    >
      {value ? (
        <img src={value.thumbnailUrl ?? value.url ?? localPreview ?? ''} alt={label} className="upload-preview" />
      ) : (
        <div className="upload-empty">{busy ? 'Đang tải…' : `+ ${label}`}</div>
      )}
    </Upload>
    </div>
  )
}

export default UploadBox
