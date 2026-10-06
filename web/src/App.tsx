import { Suspense, lazy } from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom'
import { CartProvider } from './context/CartContext'
import { AuthProvider } from './context/AuthContext'
import { WishlistProvider } from './context/WishlistContext'
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
const Login = lazy(() => import('./pages/Login'))
const Register = lazy(() => import('./pages/Register'))
const Checkout = lazy(() => import('./pages/Checkout'))
const OrderSuccess = lazy(() => import('./pages/OrderSuccess'))
const Wishlist = lazy(() => import('./pages/Wishlist'))
const Notifications = lazy(() => import('./pages/Notifications'))
const ShopPage = lazy(() => import('./pages/ShopPage'))

const PageLoader = () => (
  <div className="page-loader">
    <div className="loading-spinner"></div>
  </div>
)

function App() {
  return (
    <AuthProvider>
      <WishlistProvider>
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
                    <Route path="/dang-nhap" element={<Login />} />
                    <Route path="/dang-ky" element={<Register />} />
                    <Route path="/thanh-toan" element={<Checkout />} />
                    <Route path="/dat-hang-thanh-cong" element={<OrderSuccess />} />
                    <Route path="/yeu-thich" element={<Wishlist />} />
                    <Route path="/thong-bao" element={<Notifications />} />
                    <Route path="/shop" element={<ShopPage />} />
                  </Routes>
                </Suspense>
              </main>
              <Footer />
              <BackToTop />
            </div>
          </Router>
        </CartProvider>
      </WishlistProvider>
    </AuthProvider>
  )
}

export default App
