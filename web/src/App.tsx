import { Suspense, lazy } from 'react';
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider } from './context/AuthContext'
import Header from './components/Header'
import Footer from './components/Footer'
import ScrollToTop from './components/ScrollToTop'
import BackToTop from './components/BackToTop'
import { ChatProvider, ChatWidget } from './components/chat/Chat'
import Toaster from './components/Toaster'
import './App.css'

// Lazy load pages - Code Splitting
const HomePage = lazy(() => import('./pages/HomePage'))
const ProductDetail = lazy(() => import('./pages/ProductDetail'))
const CartPage = lazy(() => import('./pages/CartPage'))
const SearchResults = lazy(() => import('./pages/SearchResults'))
const CategoryResults = lazy(() => import('./pages/SearchResults').then((m) => ({ default: m.CategoryResults })))
const Login = lazy(() => import('./pages/Login'))
const Register = lazy(() => import('./pages/Register'))
const Checkout = lazy(() => import('./pages/Checkout'))
const OrderSuccess = lazy(() => import('./pages/PaymentPages').then((m) => ({ default: m.OrderSuccessPage })))
const GatewayPage = lazy(() => import('./pages/PaymentPages').then((m) => ({ default: m.SimulatedGatewayPage })))
const PaymentResult = lazy(() => import('./pages/PaymentPages').then((m) => ({ default: m.PaymentResultPage })))
const OrdersPage = lazy(() => import('./pages/account/CommercePages').then((m) => ({ default: m.OrdersPage })))
const ViewedPage = lazy(() => import('./pages/account/BrowsingPages').then((m) => ({ default: m.ViewedPage })))
const FollowedShopsPage = lazy(() => import('./pages/account/BrowsingPages').then((m) => ({ default: m.FollowedShopsPage })))
const OrderDetailPage = lazy(() => import('./pages/account/CommercePages').then((m) => ({ default: m.OrderDetailPage })))
const VouchersPage = lazy(() => import('./pages/account/CommercePages').then((m) => ({ default: m.VouchersPage })))
const TrackingPage = lazy(() => import('./pages/TrackingPage'))
const CoinsPage = lazy(() => import('./pages/account/CommercePages').then((m) => ({ default: m.CoinsPage })))
const OrderReviewPage = lazy(() => import('./pages/account/AftercarePages').then((m) => ({ default: m.OrderReviewPage })))
const ReturnFormPage = lazy(() => import('./pages/account/AftercarePages').then((m) => ({ default: m.ReturnFormPage })))
const ReturnsPage = lazy(() => import('./pages/account/AftercarePages').then((m) => ({ default: m.ReturnsPage })))
const ReturnDetailPage = lazy(() => import('./pages/account/AftercarePages').then((m) => ({ default: m.ReturnDetailPage })))
const WalletPage = lazy(() => import('./pages/account/WalletPage'))
const CampaignPage = lazy(() => import('./pages/CampaignPage'))
const FlashSalePage = lazy(() => import('./pages/FlashSalePage'))
const AppDownloadPage = lazy(() => import('./pages/AppDownloadPage'))
const NotFoundPage = lazy(() => import('./pages/NotFoundPage'))
const Wishlist = lazy(() => import('./pages/Wishlist'))
const Notifications = lazy(() => import('./pages/Notifications'))
const ShopPage = lazy(() => import('./pages/ShopPage'))
const ForgotPassword = lazy(() => import('./pages/ForgotPassword'))
const AccountLayout = lazy(() => import('./pages/account/AccountLayout'))
const ProfilePage = lazy(() => import('./pages/account/ProfilePage'))
const AddressesPage = lazy(() => import('./pages/account/AddressesPage'))
const PasswordPage = lazy(() => import('./pages/account/PasswordPage'))
const DevicesPage = lazy(() => import('./pages/account/DevicesPage'))
const PrivacyPage = lazy(() => import('./pages/account/PrivacyPage'))
const NotificationSettingsPage = lazy(() => import('./pages/account/NotificationSettingsPage'))
const ChatPage = lazy(() => import('./pages/ChatPage'))
const CmsPageView = lazy(() => import('./pages/ContentPages').then((m) => ({ default: m.CmsPageView })))
const HelpCenter = lazy(() => import('./pages/ContentPages').then((m) => ({ default: m.HelpCenter })))

const PageLoader = () => (
  <div className="page-loader">
    <div className="loading-spinner"></div>
  </div>
)

function App() {
  return (
    <AuthProvider>
          <Router>
          <ChatProvider>
            <ScrollToTop />
            <div className="App">
              <Header />
              <main className="main-content">
                <Suspense fallback={<PageLoader />}>
                  <Routes>
                    <Route path="/" element={<HomePage />} />
                    <Route path="/san-pham/:id" element={<ProductDetail />} />
                    <Route path="/gio-hang" element={<CartPage />} />
                    <Route path="/tim-kiem" element={<SearchResults />} />
                    <Route path="/danh-muc/:slug" element={<CategoryResults />} />
                    <Route path="/dang-nhap" element={<Login />} />
                    <Route path="/dang-ky" element={<Register />} />
                    <Route path="/thanh-toan" element={<Checkout />} />
                    <Route path="/dat-hang-thanh-cong" element={<OrderSuccess />} />
                    <Route path="/cong-thanh-toan/:paymentId" element={<GatewayPage />} />
                    <Route path="/thanh-toan/ket-qua/:checkoutId" element={<PaymentResult />} />
                    <Route path="/yeu-thich" element={<Wishlist />} />
                    <Route path="/thong-bao" element={<Notifications />} />
                    <Route path="/chat" element={<ChatPage />} />
                    <Route path="/trang/:slug" element={<CmsPageView />} />
                    <Route path="/tro-giup" element={<HelpCenter />} />
                    <Route path="/tro-giup/:slug" element={<CmsPageView />} />
                    <Route path="/tra-cuu-van-don" element={<TrackingPage />} />
                    <Route path="/tra-cuu-van-don/:trackingNo" element={<TrackingPage />} />
                    <Route path="/shop/:slug" element={<ShopPage />} />
                    <Route path="/su-kien/:slug" element={<CampaignPage />} />
                    <Route path="/flash-sale" element={<FlashSalePage />} />
                    <Route path="/tai-ung-dung" element={<AppDownloadPage />} />
                    <Route path="/quen-mat-khau" element={<ForgotPassword />} />
                    <Route path="/tai-khoan" element={<AccountLayout />}>
                      <Route index element={<Navigate to="ho-so" replace />} />
                      <Route path="don-mua" element={<OrdersPage />} />
                      <Route path="don-mua/:code" element={<OrderDetailPage />} />
                      <Route path="don-mua/:code/danh-gia" element={<OrderReviewPage />} />
                      <Route path="don-mua/:code/tra-hang" element={<ReturnFormPage />} />
                      <Route path="tra-hang" element={<ReturnsPage />} />
                      <Route path="tra-hang/:code" element={<ReturnDetailPage />} />
                      <Route path="voucher" element={<VouchersPage />} />
                      <Route path="da-xem" element={<ViewedPage />} />
                      <Route path="shop-theo-doi" element={<FollowedShopsPage />} />
                      <Route path="xu" element={<CoinsPage />} />
                      <Route path="vi" element={<WalletPage />} />
                      <Route path="ho-so" element={<ProfilePage />} />
                      <Route path="dia-chi" element={<AddressesPage />} />
                      <Route path="mat-khau" element={<PasswordPage />} />
                      <Route path="thiet-bi" element={<DevicesPage />} />
                      <Route path="quyen-rieng-tu" element={<PrivacyPage />} />
                      <Route path="thong-bao" element={<NotificationSettingsPage />} />
                    </Route>
                    <Route path="*" element={<NotFoundPage />} />
                  </Routes>
                </Suspense>
              </main>
              <Footer />
              <BackToTop />
              <Toaster />
              <ChatWidget />
            </div>
          </ChatProvider>
          </Router>
    </AuthProvider>
  )
}

export default App
