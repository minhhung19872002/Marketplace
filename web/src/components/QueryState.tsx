import type { ReactNode } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import { ApiError } from '../api/http';
import './QueryState.css';

interface Props<T> {
  query: Pick<UseQueryResult<T>, 'data' | 'isPending' | 'isError' | 'error' | 'refetch' | 'isFetching'>;
  /** True when the loaded data has nothing to show (an empty list…) */
  isEmpty?: (data: T) => boolean;
  emptyText?: ReactNode;
  /** Shown while the first load is running (default: a spinner) */
  loading?: ReactNode;
  children: (data: T) => ReactNode;
}

/**
 * Loading / error + "Thử lại" / empty / data for one query (spec 6.5, F3) — a network error never shows as an empty
 * list. Data already on screen stays when a refresh fails (the error then shows above it).
 */
const QueryState = <T,>({ query, isEmpty, emptyText = 'Chưa có dữ liệu.', loading, children }: Props<T>) => {
  if (query.isPending) return <>{loading ?? <div className="page-loader" role="status" aria-label="Đang tải"><div className="loading-spinner" /></div>}</>;
  const errorBox = query.isError && (
    <div className="query-error" role="alert" data-testid="query-error">
      <span>{query.error instanceof ApiError ? query.error.message : 'Không tải được dữ liệu. Vui lòng kiểm tra kết nối.'}</span>
      <button type="button" onClick={() => void query.refetch()} disabled={query.isFetching} data-testid="query-retry">Thử lại</button>
    </div>
  );
  if (query.data === undefined) return <>{errorBox}</>;
  const data = query.data;
  return (
    <>
      {errorBox}
      {isEmpty?.(data) ? <div className="query-empty" data-testid="query-empty">{emptyText}</div> : children(data)}
    </>
  );
};

export default QueryState;
