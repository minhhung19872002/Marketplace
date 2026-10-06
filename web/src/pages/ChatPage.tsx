import { useEffect } from 'react';
import { Navigate, useLocation, useSearchParams } from 'react-router-dom';
import { ChatPanel, useChat } from '../components/chat/Chat';
import { useAuth } from '../context/AuthContext';

/** Full-page chat (the floating window's "Mở rộng"). ?c=<conversationId> opens one conversation. */
const ChatPage = () => {
  const { isLoggedIn, isChecking } = useAuth();
  const location = useLocation();
  const [params] = useSearchParams();
  const chat = useChat();
  const c = params.get('c');
  useEffect(() => {
    if (c) chat.show(c);
    chat.close();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [c]);
  if (isChecking) return <div className="page-loader"><div className="loading-spinner" /></div>;
  if (!isLoggedIn) return <Navigate to="/dang-nhap" replace state={{ from: location.pathname }} />;
  return (
    <div className="container chat-page">
      <h1>Chat</h1>
      <ChatPanel />
    </div>
  );
};

export default ChatPage;
