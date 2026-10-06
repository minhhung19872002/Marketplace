import { StrictMode } from 'react'
import ReactDOM from 'react-dom/client'
import { ConfigProvider } from 'antd'
import viVN from 'antd/locale/vi_VN'
import App from './App'
import { theme } from './theme'

const root = document.getElementById('root')
if (!root) throw new Error('Missing #root element')

ReactDOM.createRoot(root).render(
  <StrictMode>
    <ConfigProvider locale={viVN} theme={theme}>
      <App />
    </ConfigProvider>
  </StrictMode>
)
