import { useCallback, useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { storefrontApi } from '../api/storefront';
import { useAuth } from './AuthContext';
import { refreshSession } from '../api/http';
import { useAuthStore } from '../stores/auth';

interface WishlistValue {
  ids: string[];
  has: (productId: string) => boolean;
  /** Likes / unlikes on the server; guests are sent to the login page first. */
  toggle: (productId: string) => Promise<void>;
  count: number;
}

const WISHLIST_IDS = ['wishlist', 'ids'] as const;

export const useWishlist = (): WishlistValue => {
  const { isLoggedIn } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data: ids = [] } = useQuery({
    queryKey: WISHLIST_IDS,
    queryFn: storefrontApi.wishlistIds,
    enabled: isLoggedIn,
    staleTime: 60_000,
  });
  const set = useMemo(() => new Set(ids), [ids]);

  const mutation = useMutation({
    mutationFn: ({ productId, on }: { productId: string; on: boolean }) => storefrontApi.like(productId, on),
    // Optimistic: the heart flips at once, the server's answer settles it
    onMutate: async ({ productId, on }) => {
      await queryClient.cancelQueries({ queryKey: WISHLIST_IDS });
      const previous = queryClient.getQueryData<string[]>(WISHLIST_IDS) ?? [];
      queryClient.setQueryData<string[]>(WISHLIST_IDS, on ? [...previous, productId] : previous.filter((x) => x !== productId));
      return { previous };
    },
    onError: (_e, _v, context) => context && queryClient.setQueryData(WISHLIST_IDS, context.previous),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: WISHLIST_IDS });
      void queryClient.invalidateQueries({ queryKey: ['wishlist', 'page'] });
    },
  });

  const toggle = useCallback(
    async (productId: string) => {
      // Right after a page load the session may still be restoring: wait for it instead of treating the user as a guest
      if (useAuthStore.getState().status === 'checking') await refreshSession();
      if (useAuthStore.getState().status !== 'authenticated') {
        navigate('/dang-nhap', { state: { from: window.location.pathname + window.location.search } });
        return;
      }
      mutation.mutate({ productId, on: !set.has(productId) });
    },
    [navigate, mutation, set],
  );

  const has = useCallback((productId: string) => set.has(productId), [set]);
  return { ids, has, toggle, count: ids.length };
};
