import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { chatApi, type ChatMessage, type Conversation } from '../../api/chat';
import { uploadMedia } from '../../api/aftercare';
import { ApiError } from '../../api/http';
import { useAuth } from '../../context/AuthContext';
import { realtime, useRealtimeEvent } from '../../lib/realtime';
import { formatPrice } from '../../lib/money';
import { formatDateTime } from '../../lib/datetime';
import { handleImgError, imageOrPlaceholder } from '../../lib/image';
import './Chat.css';

interface ChatState {
  open: boolean;
  conversationId: string | null;
  // A product the buyer came from ("Chat ngay" on a product page), offered as a card to send
  productId: string | null;
}

interface ChatApi extends ChatState {
  openWith: (shopId: string, productId?: string) => Promise<void>;
  show: (conversationId?: string) => void;
  close: () => void;
}

const ChatContext = createContext<ChatApi | null>(null);

export const useChat = () => {
  const c = useContext(ChatContext);
  if (!c) throw new Error('useChat outside ChatProvider');
  return c;
};

export const ChatProvider = ({ children }: { children: ReactNode }) => {
  const [state, setState] = useState<ChatState>({ open: false, conversationId: null, productId: null });
  const openWith = useCallback(async (shopId: string, productId?: string) => {
    const c = await chatApi.start(shopId);
    setState({ open: true, conversationId: c.id, productId: productId ?? null });
  }, []);
  const show = useCallback((conversationId?: string) => setState((s) => ({ ...s, open: true, conversationId: conversationId ?? s.conversationId })), []);
  const close = useCallback(() => setState((s) => ({ ...s, open: false })), []);
  const value = useMemo(() => ({ ...state, openWith, show, close }), [state, openWith, show, close]);
  return <ChatContext.Provider value={value}>{children}</ChatContext.Provider>;
};

export const CONVERSATIONS_KEY = ['chat', 'conversations'] as const;

const Card = ({ m }: { m: ChatMessage }) => {
  const p = m.payload as Record<string, string | number> | null;
  switch (m.type) {
    case 'Image':
      return <a href={String(p?.url ?? '')} target="_blank" rel="noreferrer"><img className="chat-image" src={String(p?.url ?? '')} alt="Ảnh" onError={handleImgError} /></a>;
    case 'Product':
      return (
        <Link to={`/san-pham/${p?.productId}`} className="chat-card" data-testid="chat-product-card">
          <img src={imageOrPlaceholder(p?.imageUrl as string | null)} alt="" onError={handleImgError} />
          <span><b>{String(p?.name ?? m.body)}</b><small>{formatPrice(Number(p?.price ?? 0))}</small></span>
        </Link>
      );
    case 'Order':
      return (
        <Link to={`/tai-khoan/don-mua/${p?.code}`} className="chat-card">
          <span><b>Đơn {String(p?.code)}</b><small>{String(p?.status)} · {formatPrice(Number(p?.total ?? 0))}</small></span>
        </Link>
      );
    case 'Voucher':
      return <span className="chat-card"><span><b>Voucher {String(p?.code)}</b><small>{String(p?.name ?? '')}</small></span></span>;
    default:
      return <span className="chat-text">{m.body}</span>;
  }
};

/** One conversation: history (older on demand), live messages, "đã xem", "đang gõ", composer. */
export const ChatThread = ({ conversation, productId, onProductSent }: { conversation: Conversation; productId?: string | null; onProductSent?: () => void }) => {
  const queryClient = useQueryClient();
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [text, setText] = useState('');
  const [error, setError] = useState('');
  const [typing, setTyping] = useState(false);
  const [more, setMore] = useState(true);
  const bottom = useRef<HTMLDivElement>(null);
  const lastTyping = useRef(0);
  const id = conversation.id;

  const markRead = useCallback(() => {
    void chatApi.read(id).then(() => queryClient.invalidateQueries({ queryKey: CONVERSATIONS_KEY }));
  }, [id, queryClient]);

  useEffect(() => {
    let alive = true;
    setMessages([]);
    void chatApi.messages(id).then((list) => {
      if (!alive) return;
      setMessages([...list].reverse());
      setMore(list.length === 30);
      markRead();
    });
    return () => {
      alive = false;
    };
  }, [id, markRead]);

  useEffect(() => {
    bottom.current?.scrollIntoView({ block: 'end' });
  }, [messages.length]);

  useRealtimeEvent<ChatMessage>('chat.message', (m) => {
    if (m.conversationId !== id) return;
    setMessages((list) => (list.some((x) => x.id === m.id) ? list : [...list, m]));
    setTyping(false);
    if (m.senderRole !== 'Buyer') markRead();
  });
  useRealtimeEvent<{ conversationId: string; side: string; at: string }>('chat.read', (e) => {
    if (e.conversationId !== id || e.side !== 'Shop') return;
    setMessages((list) => list.map((m) => (m.senderRole === 'Buyer' && !m.readAt ? { ...m, readAt: e.at } : m)));
  });
  useRealtimeEvent<{ conversationId: string; side: string }>('chat.typing', (e) => {
    if (e.conversationId !== id || e.side !== 'Shop') return;
    setTyping(true);
    window.setTimeout(() => setTyping(false), 3_000);
  });

  const send = async (input: Parameters<typeof chatApi.send>[1]) => {
    setError('');
    try {
      const m = await chatApi.send(id, input);
      setMessages((list) => (list.some((x) => x.id === m.id) ? list : [...list, m]));
      void queryClient.invalidateQueries({ queryKey: CONVERSATIONS_KEY });
      return true;
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Không gửi được tin nhắn.');
      return false;
    }
  };
  const older = async () => {
    const first = messages[0];
    if (!first) return;
    const list = await chatApi.messages(id, first.createdAt);
    setMessages((cur) => [...[...list].reverse(), ...cur]);
    setMore(list.length === 30);
  };
  const onType = (value: string) => {
    setText(value);
    if (Date.now() - lastTyping.current > 2_000) {
      lastTyping.current = Date.now();
      void realtime().then((c) => c?.invoke('Typing', id).catch(() => undefined));
    }
  };
  const lastMine = [...messages].reverse().find((m) => m.senderRole === 'Buyer');

  return (
    <div className="chat-thread" data-testid="chat-thread">
      <div className="chat-thread-head">
        <Link to={`/shop/${conversation.shopSlug}`}><b>{conversation.shopName}</b></Link>
        <ThreadActions conversation={conversation} onNotice={setError} />
      </div>
      {conversation.blockedByBuyer && <div className="chat-notice" role="status">Bạn đã chặn shop này — shop không gửi được tin cho bạn.</div>}
      <div className="chat-messages" data-testid="chat-messages">
        {more && messages.length > 0 && <button className="chat-older" onClick={older}>Xem tin cũ hơn</button>}
        {messages.map((m) => (
          <div key={m.id} className={`chat-msg chat-msg-${m.senderRole.toLowerCase()}`} data-testid="chat-message">
            {m.flagged && <span className="chat-warning">⚠ Tin này có thông tin liên hệ ngoài sàn — giao dịch ngoài ShopHub không được bảo vệ.</span>}
            <Card m={m} />
            <small>{formatDateTime(m.createdAt)}</small>
          </div>
        ))}
        {lastMine?.readAt && <div className="chat-seen" data-testid="chat-seen">Đã xem</div>}
        {typing && <div className="chat-typing" data-testid="chat-typing">Shop đang soạn tin…</div>}
        <div ref={bottom} />
      </div>
      {productId && (
        <div className="chat-offer">
          <span>Hỏi về sản phẩm bạn đang xem?</span>
          <button onClick={async () => { if (await send({ type: 'Product', productId })) onProductSent?.(); }} data-testid="chat-send-product">Gửi sản phẩm</button>
        </div>
      )}
      {error && <div className="chat-error" role="alert">{error}</div>}
      <form className="chat-composer" onSubmit={async (e) => {
        e.preventDefault();
        if (!text.trim()) return;
        if (await send({ type: 'Text', text: text.trim() })) setText('');
      }}>
        <label className="chat-attach" title="Gửi ảnh">
          📷
          <input type="file" accept="image/*" hidden onChange={async (e) => {
            const file = e.target.files?.[0];
            if (!file) return;
            try {
              const asset = await uploadMedia('chat', file);
              await send({ type: 'Image', imageAssetId: asset.id });
            } catch (err) {
              setError(err instanceof ApiError ? err.message : 'Không gửi được ảnh.');
            }
          }} />
        </label>
        <input value={text} onChange={(e) => onType(e.target.value)} maxLength={2000} placeholder="Nhập tin nhắn…" aria-label="Tin nhắn" data-testid="chat-input" />
        <button type="submit" data-testid="chat-send">Gửi</button>
      </form>
    </div>
  );
};

/** Chặn / bỏ chặn shop and báo cáo shop vi phạm (spec II.11); the platform reviews reports in admin. */
const ThreadActions = ({ conversation, onNotice }: { conversation: Conversation; onNotice: (text: string) => void }) => {
  const queryClient = useQueryClient();
  const [reporting, setReporting] = useState(false);
  const [reason, setReason] = useState('');
  const run = async (work: () => Promise<{ message: string }>) => {
    try {
      const r = await work();
      onNotice(r.message);
      void queryClient.invalidateQueries({ queryKey: CONVERSATIONS_KEY });
      return true;
    } catch (e) {
      onNotice(e instanceof ApiError ? e.message : 'Không thực hiện được, vui lòng thử lại.');
      return false;
    }
  };
  return (
    <span className="chat-thread-actions">
      <button type="button" data-testid="chat-block" onClick={() => void run(() => chatApi.block(conversation.id, !conversation.blockedByBuyer))}>
        {conversation.blockedByBuyer ? 'Bỏ chặn' : 'Chặn'}
      </button>
      <button type="button" data-testid="chat-report" onClick={() => setReporting((v) => !v)}>Báo cáo</button>
      {reporting && (
        <form className="chat-report-form" onSubmit={async (e) => {
          e.preventDefault();
          if (reason.trim() && await run(() => chatApi.report(conversation.id, reason.trim()))) { setReporting(false); setReason(''); }
        }}>
          <select value={reason} onChange={(e) => setReason(e.target.value)} aria-label="Lý do báo cáo" data-testid="chat-report-reason">
            <option value="">Chọn lý do…</option>
            <option>Yêu cầu giao dịch / chuyển khoản ngoài sàn</option>
            <option>Ngôn từ xúc phạm, quấy rối</option>
            <option>Lừa đảo, hàng giả</option>
            <option>Spam, quảng cáo</option>
          </select>
          <button type="submit" disabled={!reason} data-testid="chat-report-send">Gửi báo cáo</button>
        </form>
      )}
    </span>
  );
};

/** Conversation list + the open conversation (used by the floating window and the /chat page). */
export const ChatPanel = () => {
  const chat = useChat();
  const queryClient = useQueryClient();
  const [q, setQ] = useState('');
  const list = useQuery({ queryKey: [...CONVERSATIONS_KEY, q], queryFn: () => chatApi.list(q) });
  useRealtimeEvent('chat.message', () => void queryClient.invalidateQueries({ queryKey: CONVERSATIONS_KEY }));
  const items = list.data?.items ?? [];
  const current = items.find((c) => c.id === chat.conversationId) ?? null;
  return (
    <div className="chat-panel">
      <div className="chat-list">
        <input className="chat-search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Tìm theo tên shop" aria-label="Tìm hội thoại" />
        {items.length === 0 && <p className="chat-empty">Chưa có cuộc trò chuyện nào.</p>}
        {items.map((c) => (
          <button key={c.id} className={`chat-item ${c.id === chat.conversationId ? 'active' : ''}`} onClick={() => chat.show(c.id)} data-testid="chat-conversation">
            <img src={imageOrPlaceholder(c.shopLogoUrl)} alt="" onError={handleImgError} />
            <span>
              <b>{c.shopName}</b>
              <small>{c.lastMessagePreview ?? 'Bắt đầu trò chuyện'}</small>
            </span>
            {c.unread > 0 && <em className="chat-unread">{c.unread}</em>}
          </button>
        ))}
      </div>
      {current ? (
        <ChatThread key={current.id} conversation={current} productId={chat.productId} onProductSent={() => chat.show(current.id)} />
      ) : (
        <div className="chat-thread chat-placeholder">Chọn một cuộc trò chuyện để bắt đầu.</div>
      )}
    </div>
  );
};

/** Floating chat window bottom-right (spec II.11), with the unread counter. */
export const ChatWidget = () => {
  const { isLoggedIn } = useAuth();
  const chat = useChat();
  const queryClient = useQueryClient();
  const unread = useQuery({ queryKey: ['chat', 'unread'], queryFn: chatApi.unread, enabled: isLoggedIn, refetchInterval: 300_000 });
  useRealtimeEvent('chat.message', () => void queryClient.invalidateQueries({ queryKey: ['chat', 'unread'] }), isLoggedIn);
  if (!isLoggedIn) return null;
  return (
    <div className="chat-widget">
      {chat.open ? (
        <div className="chat-window" data-testid="chat-window">
          <div className="chat-window-head">
            <span>Chat</span>
            <Link to="/chat" onClick={chat.close}>Mở rộng</Link>
            <button onClick={chat.close} aria-label="Thu nhỏ chat">—</button>
          </div>
          <ChatPanel />
        </div>
      ) : (
        <button className="chat-launcher" onClick={() => chat.show()} data-testid="chat-launcher">
          💬 Chat{(unread.data ?? 0) > 0 && <em className="chat-unread">{unread.data}</em>}
        </button>
      )}
    </div>
  );
};

/** "Chat ngay" on the product page / shop page; guests go to sign-in first. */
export const ChatNowButton = ({ shopId, productId, className }: { shopId: string; productId?: string; className?: string }) => {
  const { isLoggedIn } = useAuth();
  const chat = useChat();
  const navigate = useNavigate();
  const location = useLocation();
  const [error, setError] = useState('');
  return (
    <>
      <button className={className} data-testid="chat-now" onClick={async () => {
        if (!isLoggedIn) {
          navigate('/dang-nhap', { state: { from: location.pathname } });
          return;
        }
        try {
          setError('');
          await chat.openWith(shopId, productId);
        } catch (e) {
          setError(e instanceof ApiError ? e.message : 'Không mở được chat.');
        }
      }}>💬 Chat Ngay</button>
      {error && <span className="chat-error" role="alert">{error}</span>}
    </>
  );
};

/** Response rate / time shown with the shop (spec II.11). */
export const ChatStats = ({ shopId }: { shopId: string }) => {
  const stats = useQuery({ queryKey: ['chat-stats', shopId], queryFn: () => chatApi.stats(shopId), staleTime: 600_000 });
  if (!stats.data) return null;
  return (
    <>
      <div><strong data-testid="chat-response-rate">{stats.data.responseRatePercent}%</strong><span>Tỉ Lệ Phản Hồi</span></div>
      <div><strong>{stats.data.responseTime}</strong><span>Thời Gian Phản Hồi</span></div>
    </>
  );
};
