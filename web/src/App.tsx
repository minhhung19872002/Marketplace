import { Suspense, lazy } from 'react';
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom'
import { CartProvider } from './context/CartContext'
import { AuthProvider } from './context/AuthContext'
import Header from './components/Header'
import Footer from './components/Footer'
import ScrollToTop from './components/ScrollToTop'
import BackToTop from './components/BackToTop'
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
const OrderSuccess = lazy(() => import('./pages/OrderSuccess'))
const Wishlist = lazy(() => import('./pages/Wishlist'))
const Notifications = lazy(() => import('./pages/Notifications'))
const ShopPage = lazy(() => import('./pages/ShopPage'))
const ForgotPassword = lazy(() => import('./pages/ForgotPassword'))
const AccountLayout = lazy(() => import('./pages/account/AccountLayout'))
const ProfilePage = lazy(() => import('./pages/account/ProfilePage'))
const AddressesPage = lazy(() => import('./pages/account/AddressesPage'))
const PasswordPage = lazy(() => import('./pages/account/PasswordPage'))
const DevicesPage = lazy(() => import('./pages/account/DevicesPage'))

const PageLoader = () => (
  <div className="page-loader">
    <div className="loading-spinner"></div>
  </div>
)

function App() {
  return (
    <AuthProvider>
        <CartProvider>
          <Router>
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
                    <Route path="/yeu-thich" element={<Wishlist />} />
                    <Route path="/thong-bao" element={<Notifications />} />
                    <Route path="/shop/:slug" element={<ShopPage />} />
                    <Route path="/quen-mat-khau" element={<ForgotPassword />} />
                    <Route path="/tai-khoan" element={<AccountLayout />}>
                      <Route index element={<Navigate to="ho-so" replace />} />
                      <Route path="ho-so" element={<ProfilePage />} />
                      <Route path="dia-chi" element={<AddressesPage />} />
                      <Route path="mat-khau" element={<PasswordPage />} />
                      <Route path="thiet-bi" element={<DevicesPage />} />
                    </Route>
                  </Routes>
                </Suspense>
              </main>
              <Footer />
              <BackToTop />
            </div>
          </Router>
        </CartProvider>
    </AuthProvider>
  )
}

export default App
