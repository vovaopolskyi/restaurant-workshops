import { from, map, mergeMap, Observable, toArray } from 'rxjs';

/** Limit in-flight work while retaining the original display order. */
export function mapWithConcurrency<T, R>(
  values: readonly T[],
  project: (value: T) => Observable<R>,
  concurrency: number
): Observable<R[]> {
  return from(values).pipe(
    mergeMap((value, index) => project(value).pipe(map(result => ({ index, result }))), concurrency),
    toArray(),
    map(results => results.sort((a, b) => a.index - b.index).map(entry => entry.result))
  );
}
